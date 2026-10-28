using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMXScenarioDesigner
{
    public partial class ControlBarPanel : UserControl
    {
        // Khai báo bộ theo dõi sự kiện thiết bị
        private ManagementEventWatcher _deviceWatcher;

        // Quản lý cổng COM
        private SerialPort _serialPort;
        private bool _isConnected = false;

        // Sự kiện yêu cầu (Request) dữ liệu Frame của kịch bản từ MainWindow để gửi đi
        public delegate byte[] SendScenarioRequestHandler();
        public event SendScenarioRequestHandler OnRequestFrameData;

        public ControlBarPanel()
        {
            InitializeComponent();
            this.Loaded += ControlBarPanel_Loaded;
            this.Unloaded += ControlBarPanel_Unloaded;

            // Khởi tạo thông số cổng COM mặc định
            _serialPort = new SerialPort();
            _serialPort.BaudRate = 115200; // Thay đổi baudrate ở đây nếu STM32 quy định khác
            _serialPort.DataBits = 8;
            _serialPort.Parity = Parity.None;
            _serialPort.StopBits = StopBits.One;
            _serialPort.DtrEnable = true;  // Kích hoạt DTR cho chip nạp / STM32 USB CDC nhận diện dữ liệu tốt hơn
            _serialPort.RtsEnable = true;  // Kích hoạt RTS phối hợp bắt tay phần cứng truyền dẫn
            _serialPort.WriteTimeout = 1000;
            _serialPort.ReadTimeout = 1000;
        }

        private void ControlBarPanel_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
            StartDeviceWatcher();
        }

        private void ControlBarPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            StopDeviceWatcher(); // Dừng theo dõi khi UserControl bị hủy

            // Xóa kết nối COM nếu đang mở
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }
        }

        private void StartDeviceWatcher()
        {
            try
            {
                WqlEventQuery query = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");
                _deviceWatcher = new ManagementEventWatcher(query);
                _deviceWatcher.EventArrived += (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() => { RefreshComPorts(); }));
                };
                _deviceWatcher.Start();
            }
            catch (Exception) { }
        }

        private void StopDeviceWatcher()
        {
            if (_deviceWatcher != null)
            {
                _deviceWatcher.Stop();
                _deviceWatcher.Dispose();
                _deviceWatcher = null;
            }
        }

        private void RefreshComPorts()
        {
            if (_isConnected) return; // Không refresh (không làm mất chọn lựa) nếu đang kết nối 

            try
            {
                ComboComPorts.Items.Clear();
                var stm32Ports = GetSTM32ComPorts();

                if (stm32Ports != null && stm32Ports.Count > 0)
                {
                    foreach (var port in stm32Ports) ComboComPorts.Items.Add(port);
                    ComboComPorts.SelectedIndex = 0;
                }
                else
                {
                    ComboComPorts.Items.Add("--- Không tìm thấy DMX Controller ---");
                    ComboComPorts.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi khi quét thiết bị: " + ex.Message);
            }
        }

        private List<string> GetSTM32ComPorts()
        {
            List<string> portList = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        string name = device["Name"]?.ToString() ?? "";
                        string pnpId = device["PNPDeviceID"]?.ToString() ?? "";

                        // Windows 10/11 tự động dùng driver gốc nên tên thường bị đổi thành "USB Serial Device"
                        // Mặc định STM32 USB CDC có VID là 0483 và PID là 5740. ST-Link V2-1 có PID là 374B.
                        if (name.Contains("DMX") || pnpId.Contains("VID_0483&PID_5740") || pnpId.Contains("VID_0483&PID_374B"))
                        {
                            var match = Regex.Match(name, @"\((COM\d+)\)");
                            if (match.Success) portList.Add(match.Groups[1].Value);
                        }
                    }
                }
            }
            catch (Exception) { }
            return portList;
        }

        private void BtnRefreshCom_Click(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) DisconnectComPort();
            else ConnectComPort();
        }

        private void ConnectComPort()
        {
            if (ComboComPorts.SelectedItem == null) return;
            string selectedPort = ComboComPorts.SelectedItem.ToString();

            if (selectedPort.Contains("---"))
            {
                MessageBox.Show("Vui lòng cắm và chọn Board DMX Controller nhận diện được!", "Chưa chọn cổng", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _serialPort.PortName = selectedPort;
                _serialPort.BaudRate = 115200;
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;
                _serialPort.Open();
                _isConnected = true;

                // Cập nhật lại UI thông báo trạng thái
                BtnConnect.Content = "Ngắt kết nối";
                BtnConnect.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Màu đỏ (Danger)
                ComboComPorts.IsEnabled = false; // Khoá chọn thiết bị
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể kết nối {selectedPort}.\n\nNguyên nhân thường gặp: Cổng COM đang bị phần mềm khác (như STM32CubeIDE, Hercules, Serial Terminal) chiếm dụng.\n\nChi tiết lỗi: {ex.GetType().Name} - {ex.Message}", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisconnectComPort()
        {
            try
            {
                if (_serialPort.IsOpen) _serialPort.Close();
            }
            catch (Exception) { }
            finally
            {
                _isConnected = false;
                BtnConnect.Content = "Kết nối";
                BtnConnect.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // Màu xanh dương gốc
                ComboComPorts.IsEnabled = true;
                RefreshComPorts();
            }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            // 1. Kiểm tra xem người dùng đã chọn đúng cổng COM chưa
            if (ComboComPorts.SelectedItem == null || ComboComPorts.SelectedItem.ToString().Contains("---"))
            {
                MessageBox.Show("Cần kết nối DMX controller trước!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedPort = ComboComPorts.SelectedItem.ToString();

            // 2. Truy cập vào MainWindow để gọi hàm lấy Frame
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            byte[] frameData = mainWindow.GenerateFrameData();
            if (frameData == null || frameData.Length == 0)
            {
                MessageBox.Show("Kịch bản trống, không có lệnh nào để gửi!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Xuất ra khung debug ở timeline khi gửi kịch bản
            mainWindow.UpdateDebugHex(frameData);

            // Tự động sao chép kịch bản (frame dạng chuỗi Hex) vào clipboard
            try
            {
                string hexFrame = BitConverter.ToString(frameData).Replace("-", " ");
                System.Windows.Clipboard.SetText(hexFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi sao chép vào Clipboard: " + ex.Message);
            }

            // 3. Tiến hành kết nối COM và Gửi dữ liệu đi
            try
            {
                if (_serialPort == null)
                {
                    // Khởi tạo cổng COM (Baudrate để mặc định là 115200, có thể chỉnh lại cho khớp board của bạn)
                    _serialPort = new SerialPort(selectedPort, 115200, Parity.None, 8, StopBits.One);
                    _serialPort.DtrEnable = true;
                    _serialPort.RtsEnable = true;
                    _serialPort.WriteTimeout = 1000;
                    _serialPort.ReadTimeout = 1000;
                }
                else if (_serialPort.PortName != selectedPort)
                {
                    // Nếu chọn qua cổng khác, phải đóng cổng cũ và đổi tên cổng
                    if (_serialPort.IsOpen) _serialPort.Close();
                    _serialPort.PortName = selectedPort;
                    _serialPort.DtrEnable = true;
                    _serialPort.RtsEnable = true;
                    _serialPort.WriteTimeout = 1000;
                    _serialPort.ReadTimeout = 1000;
                }

                // Mở cổng nếu chưa mở
                if (!_serialPort.IsOpen)
                {
                    _serialPort.BaudRate = 115200;
                    _serialPort.DtrEnable = true;
                    _serialPort.RtsEnable = true;
                    _serialPort.Open();
                }

                // Gửi mảng byte frame xuống bo mạch theo dạng từng gói nhỏ (Chunking) nhằm tối ưu bộ đệm USB của microcontroller
                const int ChunkSize = 64; // Kích thước gói tin chuẩn Endpoint USB là 64 bytes
                for (int i = 0; i < frameData.Length; i += ChunkSize)
                {
                    int currentLength = Math.Min(ChunkSize, frameData.Length - i);
                    _serialPort.Write(frameData, i, currentLength);
                    System.Threading.Thread.Sleep(5); // Chờ 5ms giúp vi điều khiển xử lý kịp thời và không bị tràn bộ đệm nhận
                }

                MessageBox.Show("Gửi kịch bản thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi kết nối cổng COM:\n" + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e) { MessageBox.Show("Tính năng sẽ được phát triển sau."); }
        private void BtnOpen_Click(object sender, RoutedEventArgs e) { MessageBox.Show("Tính năng sẽ được phát triển sau."); }
        private void BtnSave_Click(object sender, RoutedEventArgs e) { MessageBox.Show("Tính năng sẽ được phát triển sau."); }
    }
}
