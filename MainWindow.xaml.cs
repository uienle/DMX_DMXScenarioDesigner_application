using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMXScenarioDesigner
{
    public class TimelineEffectItem : INotifyPropertyChanged
    {
        private double _startTime;
        private double _duration = 5000;
        private string _effectName;

        public EffectConfigData ConfigData { get; set; }

        public double StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime != value)
                {
                    _startTime = value;
                    OnPropertyChanged(nameof(StartTime));
                }
            }
        }

        public double Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        public string EffectName
        {
            get => _effectName;
            set
            {
                if (_effectName != value)
                {
                    _effectName = value;
                    OnPropertyChanged(nameof(EffectName));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TimelineTrack : INotifyPropertyChanged
    {
        private string _trackName;
        private bool _isPlaceholder;
        public string TrackName { get => _trackName; set { _trackName = value; OnPropertyChanged("TrackName"); } }
        public bool IsPlaceholder { get => _isPlaceholder; set { _isPlaceholder = value; OnPropertyChanged("IsPlaceholder"); } }
        public ObservableCollection<TimelineEffectItem> Effects { get; set; } = new ObservableCollection<TimelineEffectItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PlacedDeviceData
    {
        public int StartChannel { get; set; }
        public int EndChannel { get; set; }
        public Grid UIElement { get; set; }
        public string DeviceName { get; set; }
        public string GroupId { get; set; }
        public string GroupName { get; set; }
        public string DisplayText => DeviceName == "LED RGB" ? $"{DeviceName} [{StartChannel:D3}:{EndChannel:D3}]" : $"{DeviceName} [{StartChannel:D3}]";
    }

    public partial class MainWindow : Window
    {
        private bool _isDraggingEffect = false;
        private EffectConfigData _lastConfigData;
        private TimelineEffectItem _selectedTimelineEffect;
        public ObservableCollection<TimelineTrack> TimelineTracks { get; set; } = new ObservableCollection<TimelineTrack>();

        public MainWindow()
        {
            InitializeComponent();

            // Khởi tạo Timeline mặc định
            TimelineTracks.Add(new TimelineTrack { TrackName = "Timeline 1", IsPlaceholder = false });
            TimelineTracks.Add(new TimelineTrack { TrackName = "Thêm Timeline...", IsPlaceholder = true });

            TimelinePanel.SetData(TimelineTracks);
            TimelineEditor.SetData(TimelineTracks);

            // Đồng bộ cuộn dọc
            TimelinePanel.OnScrollChanged += (s, offset) => TimelineEditor.SyncVerticalScroll(offset);
            TimelineEditor.OnScrollChanged += (s, offset) => TimelinePanel.SyncVerticalScroll(offset);

            MainCanvasPanel.IsPlaying = TimelineEditor.IsPlaying;
            TimelineEditor.OnPlayStateChanged += (s, isPlaying) => {
                MainCanvasPanel.IsPlaying = isPlaying;
            };

            // Chạy mô phỏng khi Playhead đi qua
            TimelineEditor.OnActiveEffectsChanged += (s, activeEffects) => {
                MainCanvasPanel.UpdateActiveTimelineEffects(activeEffects, TimelineEditor.CurrentTime);
            };

            // Highlight thiết bị khi chọn khối hiệu ứng trên Timeline
            TimelineEditor.OnEffectSelected += (s, effect) => {
                _selectedTimelineEffect = effect;
                if (effect != null && effect.ConfigData != null)
                {
                    MainCanvasPanel.SelectDevicesFromEffect(effect.ConfigData.TargetDevices);
                    ConfigPanel.UpdateSelection(effect.ConfigData.TargetDevices);
                    ConfigPanel.LoadEffectConfig(effect.ConfigData);
                    MainCanvasPanel.UpdatePreviewEffect(effect.ConfigData);
                }
                else
                {
                    MainCanvasPanel.UpdatePreviewEffect(null);
                }
            };

            // Kết nối các Panel khác
            ConfigPanel.OnDeviceSelected += (sender, deviceName) => MainCanvasPanel.StartPlacingDevice(deviceName);
            ConfigPanel.OnGridToggled += (sender, isVisible) => MainCanvasPanel.ToggleGrid(isVisible);
            this.KeyDown += (sender, e) => { if (e.Key == Key.Delete) MainCanvasPanel.TriggerDelete(); };
            MainCanvasPanel.OnDeviceListChanged += (sender, devices) => {
                TreePanel.UpdateList(devices);
                TimelineEditor.RemoveEffectsWithoutDevices(devices);
            };
            MainCanvasPanel.OnSelectionChangedFromCanvas += (sender, selectedDevices) => {
                _selectedTimelineEffect = null;
                TreePanel.SyncSelectionFromCanvas(selectedDevices);
                ConfigPanel.UpdateSelection(selectedDevices);
            };
            TreePanel.OnSelectionChangedFromTree += (sender, selectedDevices) => {
                _selectedTimelineEffect = null;
                MainCanvasPanel.SyncSelectionFromTree(selectedDevices);
                ConfigPanel.UpdateSelection(selectedDevices);
            };
            TreePanel.OnGroupRenamed += (sender, args) => MainCanvasPanel.RenameGroup(args.GroupId, args.NewName);
            ConfigPanel.OnEffectConfigChanged += (sender, configData) => {
                MainCanvasPanel.UpdatePreviewEffect(configData);
                if (_selectedTimelineEffect != null && configData != null)
                {
                    _selectedTimelineEffect.ConfigData = configData;
                    _selectedTimelineEffect.EffectName = configData.EffectName;
                }
            };

            ControlBar.OnRequestFrameData += () => {
                return GenerateFrameData();
            };

            // Xử lý kéo thả hiệu ứng từ ConfigPanel
            ConfigPanel.OnAddEffectClicked += (sender, configData) => {
                _isDraggingEffect = true;
                _lastConfigData = (EffectConfigData)configData;
                EffectGhostBlock.Visibility = Visibility.Visible;
                EffectGhostText.Text = _lastConfigData.EffectName;
                UpdateGhostPosition(Mouse.GetPosition(GlobalEffectGhostCanvas));
            };

            this.PreviewMouseMove += (sender, e) => {
                if (_isDraggingEffect)
                {
                    Point pos = e.GetPosition(GlobalEffectGhostCanvas);
                    UpdateGhostPosition(pos);

                    Point posInTimeline = e.GetPosition(TimelineEditor);
                    bool isOverTimelineGrid = posInTimeline.X >= 0 && posInTimeline.X <= TimelineEditor.ActualWidth &&
                                              posInTimeline.Y >= 65 && posInTimeline.Y <= TimelineEditor.ActualHeight;

                    if (isOverTimelineGrid)
                    {
                        EffectGhostBlock.Visibility = Visibility.Collapsed;
                        TimelineEditor.ShowSnapPreview(posInTimeline);
                    }
                    else
                    {
                        EffectGhostBlock.Visibility = Visibility.Visible;
                        TimelineEditor.HideSnapPreview();
                    }
                }
            };

            this.PreviewMouseLeftButtonUp += (sender, e) => {
                if (_isDraggingEffect)
                {
                    Point posInTimeline = e.GetPosition(TimelineEditor);
                    if (posInTimeline.Y >= 65)
                    {
                        TimelineEditor.HandleDrop(posInTimeline, EffectGhostText.Text, _lastConfigData);
                    }
                    _isDraggingEffect = false;
                    EffectGhostBlock.Visibility = Visibility.Collapsed;
                    TimelineEditor.HideSnapPreview();
                }
            };

            this.PreviewMouseRightButtonDown += (sender, e) => { if (_isDraggingEffect) CancelDrag(); };
            this.PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape && _isDraggingEffect) CancelDrag(); };
        }

        private void UpdateGhostPosition(Point pos)
        {
            Canvas.SetLeft(EffectGhostBlock, pos.X - 90);
            Canvas.SetTop(EffectGhostBlock, pos.Y - 15);
        }

        private void CancelDrag()
        {
            _isDraggingEffect = false;
            EffectGhostBlock.Visibility = Visibility.Collapsed;
            TimelineEditor.HideSnapPreview();
        }

        public void UpdateDebugHex(byte[] frameData)
        {
            try
            {
                if (frameData != null && frameData.Length > 0 && DebugHexOutput != null)
                {
                    DebugHexOutput.Text = BitConverter.ToString(frameData).Replace("-", " ");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi ghi gỡ lỗi Hex: " + ex.Message);
            }
        }

        private void BtnGenerateFrame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                byte[] frame = GenerateFrameData();
                UpdateDebugHex(frame);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hiển thị Hex: " + ex.Message);
            }
        }

        private class DeviceStateChange
        {
            public ushort Address { get; set; }
            public byte EffectID { get; set; }
            public byte Speed { get; set; }
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }
            public byte Threshold { get; set; }
        }

        private byte CalculateBpmByte(int speedLevel)
        {
            double baseBpm = DMXScenarioDesigner.Services.AudioAnalysisService.CurrentBpm;
            double multiplier = 1.0;
            switch (speedLevel)
            {
                case 1: multiplier = 0.5; break;
                case 2: multiplier = 1.0; break;
                case 3: multiplier = 1.5; break;
                case 4: multiplier = 2.0; break;
                case 5: multiplier = 2.5; break;
            }
            int actualBpm = (int)(baseBpm * multiplier);
            return (byte)Math.Min(255, Math.Max(1, actualBpm));
        }

        public byte[] GenerateFrameData()
        {
            try
            {
                List<byte> frame = new List<byte>();

                // 1. StartByte
                frame.Add(0xAA);

                // --- CHUẨN BỊ DỮ LIỆU SỰ KIỆN ---
                var timelineEvents = new SortedDictionary<uint, List<DeviceStateChange>>();

                foreach (var track in TimelineTracks)
                {
                    if (track.IsPlaceholder) continue;
                    foreach (var effect in track.Effects)
                    {
                        if (effect.ConfigData == null || effect.ConfigData.TargetDevices == null) continue;

                        uint startTime = (uint)effect.StartTime;
                        uint endTime = (uint)(effect.StartTime + effect.Duration);

                        if (!timelineEvents.ContainsKey(startTime)) timelineEvents[startTime] = new List<DeviceStateChange>();
                        foreach (var dev in effect.ConfigData.TargetDevices)
                        {
                            timelineEvents[startTime].Add(new DeviceStateChange
                            {
                                Address = (ushort)dev.StartChannel,
                                EffectID = (byte)effect.ConfigData.EffectIndex, // Vì EffectIndex giờ bắt đầu từ 1 (Tránh ID 0 là OFF)
                                Speed = CalculateBpmByte(effect.ConfigData.Speed),
                                R = effect.ConfigData.SelectedColor.R,
                                G = effect.ConfigData.SelectedColor.G,
                                B = effect.ConfigData.SelectedColor.B,
                                Threshold = (byte)effect.ConfigData.Threshold
                            });
                        }

                        if (!timelineEvents.ContainsKey(endTime)) timelineEvents[endTime] = new List<DeviceStateChange>();
                        foreach (var dev in effect.ConfigData.TargetDevices)
                        {
                            timelineEvents[endTime].Add(new DeviceStateChange
                            {
                                Address = (ushort)dev.StartChannel,
                                EffectID = 0, // TẮT
                                Speed = 0,
                                R = 0,
                                G = 0,
                                B = 0,
                                Threshold = 0
                            });
                        }
                    }
                }

                // GỘP & KHỬ TRÙNG LẶP SỰ KIỆN
                var filteredEvents = new SortedDictionary<uint, List<DeviceStateChange>>();
                foreach (var kvp in timelineEvents)
                {
                    uint timeMs = kvp.Key;
                    var changes = kvp.Value;

                    var uniqueChanges = new Dictionary<ushort, DeviceStateChange>();
                    foreach (var change in changes)
                    {
                        if (!uniqueChanges.ContainsKey(change.Address))
                        {
                            uniqueChanges[change.Address] = change;
                        }
                        else
                        {
                            if (change.EffectID > 0)
                            {
                                uniqueChanges[change.Address] = change;
                            }
                        }
                    }

                    if (uniqueChanges.Count > 0)
                    {
                        filteredEvents[timeMs] = uniqueChanges.Values.ToList();
                    }
                }

                ushort totalEvents = (ushort)filteredEvents.Count;
                byte[] countBytes = BitConverter.GetBytes(totalEvents);
                if (BitConverter.IsLittleEndian) Array.Reverse(countBytes);
                frame.AddRange(countBytes);

                foreach (var kvp in filteredEvents)
                {
                    uint timeMs = kvp.Key;
                    var changes = kvp.Value;

                    byte[] timeBytes = BitConverter.GetBytes(timeMs);
                    if (BitConverter.IsLittleEndian) Array.Reverse(timeBytes);
                    frame.AddRange(timeBytes);

                    frame.Add((byte)changes.Count);

                    foreach (var change in changes)
                    {
                        byte[] addressBytes = BitConverter.GetBytes(change.Address);
                        if (BitConverter.IsLittleEndian) Array.Reverse(addressBytes);
                        frame.AddRange(addressBytes);

                        frame.Add(change.EffectID);
                        frame.Add(change.Speed);

                        if (change.EffectID == 0) // OFF
                        {
                            frame.Add(0); frame.Add(0); frame.Add(0);
                        }
                        else if (change.EffectID >= 13) // Motor
                        {
                            frame.Add(change.Threshold); frame.Add(0); frame.Add(0);
                        }
                        else // LED RGB (EffectID 1 -> 12)
                        {
                            frame.Add(change.R); frame.Add(change.G); frame.Add(change.B);
                        }
                    }
                }

                byte checksum = 0;
                foreach (byte b in frame) checksum ^= b;
                frame.Add(checksum);

                return frame.ToArray();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tạo Frame: " + ex.Message);
                return null;
            }
        }
    }
}