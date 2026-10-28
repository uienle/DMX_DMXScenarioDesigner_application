using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMXScenarioDesigner
{
    public class EffectConfigData
    {
        public string DeviceType { get; set; }
        public int EffectIndex { get; set; }
        public string EffectName { get; set; }
        public Color SelectedColor { get; set; }
        public int Speed { get; set; }
        public int Threshold { get; set; }
        public int SingleValue { get; set; }

        // Lưu lại những thiết bị được đánh dấu để chạy effect
        public List<PlacedDeviceData> TargetDevices { get; set; } = new List<PlacedDeviceData>();

        public EffectConfigData Clone()
        {
            return new EffectConfigData
            {
                DeviceType = this.DeviceType,
                EffectIndex = this.EffectIndex,
                EffectName = this.EffectName,
                SelectedColor = this.SelectedColor,
                Speed = this.Speed,
                Threshold = this.Threshold,
                SingleValue = this.SingleValue,
                TargetDevices = this.TargetDevices != null ? new List<PlacedDeviceData>(this.TargetDevices) : new List<PlacedDeviceData>()
            };
        }
    }

    public partial class DeviceAndConfigPanel : UserControl
    {
        public event EventHandler<string> OnDeviceSelected;
        public event EventHandler<bool> OnGridToggled;
        public event EventHandler<EffectConfigData> OnEffectConfigChanged;
        public event EventHandler<EffectConfigData> OnAddEffectClicked;

        private Color _selectedColor = Colors.Red;
        private string _currentDeviceType = "";
        private bool _isUpdatingUI = false;
        private bool _isUpdatingColor = false;

        private List<PlacedDeviceData> _selectedDevices = new List<PlacedDeviceData>();
        public DeviceAndConfigPanel()
        {
            InitializeComponent();
        }

        private void DeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) OnDeviceSelected?.Invoke(this, btn.Content.ToString());
        }

        private void ToggleGridCheckBox_Checked(object sender, RoutedEventArgs e) => OnGridToggled?.Invoke(this, true);
        private void ToggleGridCheckBox_Unchecked(object sender, RoutedEventArgs e) => OnGridToggled?.Invoke(this, false);

        // ====================================================================
        // HÀM RESET CẤU HÌNH VỀ MẶC ĐỊNH
        // ====================================================================
        private void ResetToDefault()
        {
            _isUpdatingUI = true;

            if (LedEffectComboBox != null) LedEffectComboBox.SelectedIndex = 11;
            if (MotorEffectComboBox != null) MotorEffectComboBox.SelectedIndex = 1;
            if (SpeedComboBox != null) SpeedComboBox.SelectedIndex = 1;
            if (ThresholdComboBox != null) ThresholdComboBox.SelectedIndex = 6;
            if (SingleValueTextBox != null) SingleValueTextBox.Text = "255";

            _selectedColor = Colors.Red;
            if (SelectedColorBox != null) SelectedColorBox.Background = new SolidColorBrush(_selectedColor);

            _isUpdatingUI = false;
        }

        public void LoadEffectConfig(EffectConfigData data)
        {
            if (data == null) return;

            _isUpdatingUI = true;

            // Xóa rác UI cũ

            _currentDeviceType = data.DeviceType;
            if (ConfigContentPanel != null) ConfigContentPanel.Visibility = Visibility.Visible;
            if (BtnAddEffect != null) BtnAddEffect.IsEnabled = true;

            if (LedConfigPanel != null) LedConfigPanel.Visibility = Visibility.Collapsed;
            if (MotorConfigPanel != null) MotorConfigPanel.Visibility = Visibility.Collapsed;
            if (SingleChannelConfigPanel != null) SingleChannelConfigPanel.Visibility = Visibility.Collapsed;
            if (SpeedPanel != null) SpeedPanel.Visibility = Visibility.Visible;

            if (_currentDeviceType == "LED RGB")
            {
                if (LedConfigPanel != null) LedConfigPanel.Visibility = Visibility.Visible;
                if (LedEffectComboBox != null) LedEffectComboBox.SelectedIndex = data.EffectIndex - 1;

                _selectedColor = data.SelectedColor;
                if (SelectedColorBox != null) SelectedColorBox.Background = new SolidColorBrush(_selectedColor);
            }
            else if (_currentDeviceType == "MOTOR")
            {
                if (MotorConfigPanel != null) MotorConfigPanel.Visibility = Visibility.Visible;
                if (MotorEffectComboBox != null) MotorEffectComboBox.SelectedIndex = data.EffectIndex - 13;
                if (ThresholdComboBox != null) ThresholdComboBox.SelectedIndex = (data.Threshold / 10) - 1;
            }
            else
            {
                if (SingleChannelConfigPanel != null) SingleChannelConfigPanel.Visibility = Visibility.Visible;
                if (SpeedPanel != null) SpeedPanel.Visibility = Visibility.Collapsed;
                if (SingleValueTextBox != null) SingleValueTextBox.Text = data.SingleValue.ToString();
            }

            if (SpeedComboBox != null) SpeedComboBox.SelectedIndex = data.Speed - 1;

            _isUpdatingUI = false;
        }

        public void UpdateSelection(List<PlacedDeviceData> selectedDevices)
        {
            _selectedDevices = selectedDevices;
            if (LedEffectComboBox == null || ConfigContentPanel == null) return;

            // Dừng xem trước nếu click ra ngoài hoặc chọn thiết bị khác
            StopPreview();

            // 1. NẾU CLICK RA NGOÀI (Không chọn gì cả)
            if (selectedDevices == null || selectedDevices.Count == 0)
            {
                ConfigContentPanel.Visibility = Visibility.Collapsed;
                if (BtnAddEffect != null) BtnAddEffect.IsEnabled = false;

                // KHÔNG reset _currentDeviceType ở đây để giữ lại lịch sử cấu hình
                return;
            }

            string firstType = selectedDevices.First().DeviceName;
            bool isSameType = selectedDevices.All(d => d.DeviceName == firstType);

            // 2. NẾU CHỌN NHIỀU THIẾT BỊ KHÁC LOẠI NHAU
            if (!isSameType)
            {
                ConfigContentPanel.Visibility = Visibility.Collapsed;
                if (BtnAddEffect != null) BtnAddEffect.IsEnabled = false;
                return;
            }

            // 3. CHỈ RESET KHI CHỌN SANG MỘT LOẠI THIẾT BỊ HOÀN TOÀN KHÁC
            if (_currentDeviceType != firstType)
            {
                ResetToDefault();
            }

            _currentDeviceType = firstType;
            ConfigContentPanel.Visibility = Visibility.Visible;
            if (BtnAddEffect != null) BtnAddEffect.IsEnabled = true;

            if (LedConfigPanel != null) LedConfigPanel.Visibility = Visibility.Collapsed;
            if (MotorConfigPanel != null) MotorConfigPanel.Visibility = Visibility.Collapsed;
            if (SingleChannelConfigPanel != null) SingleChannelConfigPanel.Visibility = Visibility.Collapsed;
            if (SpeedPanel != null) SpeedPanel.Visibility = Visibility.Visible;

            if (firstType == "LED RGB" && LedConfigPanel != null) LedConfigPanel.Visibility = Visibility.Visible;
            else if (firstType == "MOTOR" && MotorConfigPanel != null) MotorConfigPanel.Visibility = Visibility.Visible;
            else
            {
                if (SingleChannelConfigPanel != null) SingleChannelConfigPanel.Visibility = Visibility.Visible;
                if (SpeedPanel != null) SpeedPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void StopPreview()
        {
            OnEffectConfigChanged?.Invoke(this, null);
        }

        // ====================================================================

        private void LedEffectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorPickerPanel == null || LedEffectComboBox == null) return;
            int idx = LedEffectComboBox.SelectedIndex;
            // Kích hoạt bảng chọn màu cho các hiệu ứng sử dụng màu nền đơn sắc (Chỉ số 2, 3, 5, 6, 8, 9, 11)
            if (idx == 2 || idx == 3 || idx == 5 || idx == 6 || idx == 8 || idx == 9 || idx == 11)
            {
                ColorPickerPanel.Opacity = 1.0;
                ColorPickerPanel.IsEnabled = true;
            }
            else
            {
                ColorPickerPanel.Opacity = 0.5;
                ColorPickerPanel.IsEnabled = false;
            }
            if (!_isUpdatingUI) NotifyConfigChanged();
        }

        private void BtnPickColor_Click(object sender, RoutedEventArgs e)
        {
            if (TxtR == null || TxtG == null || TxtB == null ||
                SliderR == null || SliderG == null || SliderB == null) return;

            _isUpdatingColor = true;
            SliderR.Value = _selectedColor.R;
            SliderG.Value = _selectedColor.G;
            SliderB.Value = _selectedColor.B;

            TxtR.Text = _selectedColor.R.ToString();
            TxtG.Text = _selectedColor.G.ToString();
            TxtB.Text = _selectedColor.B.ToString();

            if (PreviewColorBox != null) PreviewColorBox.Background = new SolidColorBrush(_selectedColor);
            _isUpdatingColor = false;

            if (ColorPickerPopup != null) ColorPickerPopup.IsOpen = true;
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || btn.Background == null) return;
            if (SliderR == null || SliderG == null || SliderB == null ||
                TxtR == null || TxtG == null || TxtB == null) return;

            if (btn.Background is SolidColorBrush brush)
            {
                _isUpdatingColor = true;
                _selectedColor = brush.Color;
                SliderR.Value = _selectedColor.R;
                SliderG.Value = _selectedColor.G;
                SliderB.Value = _selectedColor.B;

                TxtR.Text = _selectedColor.R.ToString();
                TxtG.Text = _selectedColor.G.ToString();
                TxtB.Text = _selectedColor.B.ToString();

                if (PreviewColorBox != null) PreviewColorBox.Background = new SolidColorBrush(_selectedColor);
                _isUpdatingColor = false;
            }
        }

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingColor || TxtR == null || TxtG == null || TxtB == null ||
                SliderR == null || SliderG == null || SliderB == null) return;

            _isUpdatingColor = true;
            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;

            TxtR.Text = r.ToString();
            TxtG.Text = g.ToString();
            TxtB.Text = b.ToString();

            _selectedColor = Color.FromRgb(r, g, b);
            if (PreviewColorBox != null) PreviewColorBox.Background = new SolidColorBrush(_selectedColor);
            _isUpdatingColor = false;
        }

        private void RgbTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingColor || SliderR == null || SliderG == null || SliderB == null) return;
            if (!(sender is TextBox txt)) return;

            if (int.TryParse(txt.Text, out int val))
            {
                if (val > 255) { val = 255; txt.Text = "255"; txt.CaretIndex = 3; }
                else if (val < 0) { val = 0; txt.Text = "0"; txt.CaretIndex = 1; }
            }
            else val = 0;

            _isUpdatingColor = true;
            if (txt == TxtR && SliderR != null) SliderR.Value = val;
            else if (txt == TxtG && SliderG != null) SliderG.Value = val;
            else if (txt == TxtB && SliderB != null) SliderB.Value = val;

            _selectedColor = Color.FromRgb((byte)SliderR.Value, (byte)SliderG.Value, (byte)SliderB.Value);
            if (PreviewColorBox != null) PreviewColorBox.Background = new SolidColorBrush(_selectedColor);
            _isUpdatingColor = false;
        }

        private void ConfirmColor_Click(object sender, RoutedEventArgs e)
        {
            if (ColorPickerPopup != null) ColorPickerPopup.IsOpen = false;
            if (SelectedColorBox != null) SelectedColorBox.Background = new SolidColorBrush(_selectedColor);
            NotifyConfigChanged();
        }

        private void SingleValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SingleValueTextBox == null) return;
            if (int.TryParse(SingleValueTextBox.Text, out int val) && val >= 0 && val <= 255)
            {
                if (SingleValueError != null) SingleValueError.Visibility = Visibility.Collapsed;
                if (!_isUpdatingUI) NotifyConfigChanged();
            }
            else if (SingleValueError != null) SingleValueError.Visibility = Visibility.Visible;
        }

        private void Config_Changed(object sender, SelectionChangedEventArgs e) { if (!_isUpdatingUI) NotifyConfigChanged(); }

        private string GetSelectedEffectName()
        {
            if (_currentDeviceType == "LED RGB" && LedEffectComboBox?.SelectedItem is ComboBoxItem ledItem)
                return ledItem.Content?.ToString() ?? "Tĩnh (Color)";
            if (_currentDeviceType == "MOTOR" && MotorEffectComboBox?.SelectedItem is ComboBoxItem motorItem)
                return motorItem.Content?.ToString() ?? "MOTOR Level";
            if ((_currentDeviceType == "Kênh đơn" || _currentDeviceType == "Khác") && SingleValueTextBox != null)
                return $"Giá trị: {SingleValueTextBox.Text}";
            return "Hiệu ứng mới";
        }

        private void NotifyConfigChanged()
        {
            if (string.IsNullOrEmpty(_currentDeviceType) || SpeedComboBox == null || LedEffectComboBox == null || MotorEffectComboBox == null) return;

            EffectConfigData data = new EffectConfigData
            {

                DeviceType = _currentDeviceType,
                Speed = SpeedComboBox.SelectedIndex + 1,
                SelectedColor = _selectedColor,
                EffectName = GetSelectedEffectName(),
                TargetDevices = _selectedDevices.ToList()
            };
            if (_currentDeviceType == "LED RGB") data.EffectIndex = LedEffectComboBox.SelectedIndex + 1; // 1-based index (tránh ID 0 vì 0 đại diện cho OFF)
            else if (_currentDeviceType == "MOTOR")
            {
                data.EffectIndex = MotorEffectComboBox.SelectedIndex + 13; // Bắt đầu sau ID của LED lớn nhất (LED kết thúc tại 12)
                if (ThresholdComboBox != null) data.Threshold = (ThresholdComboBox.SelectedIndex + 1) * 10;
            }
            else if (SingleValueTextBox != null && int.TryParse(SingleValueTextBox.Text, out int val)) data.SingleValue = val;

            OnEffectConfigChanged?.Invoke(this, data);
        }

        private void BtnAddEffect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentDeviceType) || SpeedComboBox == null) return;

            StopPreview();

            EffectConfigData data = new EffectConfigData
            {
                DeviceType = _currentDeviceType,
                Speed = SpeedComboBox.SelectedIndex + 1,
                SelectedColor = _selectedColor,
                EffectName = GetSelectedEffectName(),
                TargetDevices = _selectedDevices.ToList()
            };
            if (_currentDeviceType == "LED RGB") data.EffectIndex = LedEffectComboBox.SelectedIndex + 1; // 1-based index (tránh ID 0 vì 0 đại diện cho OFF)
            else if (_currentDeviceType == "MOTOR")
            {
                data.EffectIndex = MotorEffectComboBox.SelectedIndex + 13; // Bắt đầu sau ID của LED lớn nhất (LED kết thúc tại 12)
                if (ThresholdComboBox != null) data.Threshold = (ThresholdComboBox.SelectedIndex + 1) * 10;
            }
            else if (SingleValueTextBox != null && int.TryParse(SingleValueTextBox.Text, out int val)) data.SingleValue = val;

            OnAddEffectClicked?.Invoke(this, data);

            // RESET VỀ MẶC ĐỊNH SAU KHI ĐÃ NHẤN THÊM HIỆU ỨNG
            ResetToDefault();
        }
    }
}
