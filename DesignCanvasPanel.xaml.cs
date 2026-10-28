using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DMXScenarioDesigner
{
    public partial class DesignCanvasPanel : UserControl
    {
        public event EventHandler<List<PlacedDeviceData>> OnDeviceListChanged;
        public event EventHandler<List<PlacedDeviceData>> OnSelectionChangedFromCanvas;
        private bool _isSyncing = false;

        private Point? _lastDragPoint;
        private bool _isPanning;
        private double _zoomLevel = 1.0;

        private bool _isPlacingDevice = false;
        private string _selectedDeviceName = "";
        private Point _dropPosition;

        private List<PlacedDeviceData> _placedDevices = new List<PlacedDeviceData>();
        private List<PlacedDeviceData> _selectedDevices = new List<PlacedDeviceData>();
        private bool _isDraggingDevice = false;
        private Point _deviceDragStartPoint;
        private Dictionary<PlacedDeviceData, Point> _originalDevicePositions = new Dictionary<PlacedDeviceData, Point>();

        private bool _isMarqueeSelecting = false;
        private Point _marqueeStartPoint;

        private class Command { public Action UndoAction { get; set; } public Action RedoAction { get; set; } }
        private Stack<Command> _undoStack = new Stack<Command>();
        private Stack<Command> _redoStack = new Stack<Command>();

        private int _groupCounter = 0; // Biến đếm số thứ tự nhóm

        public void SelectDevicesFromEffect(List<PlacedDeviceData> targetDevices)
        {
            _isSyncing = true;
            ClearSelectionInternal();

            if (targetDevices != null)
            {
                foreach (var target in targetDevices)
                {
                    // Tìm instance thực sự đang nằm trong _placedDevices dựa trên địa chỉ và tên
                    var actualDev = _placedDevices.FirstOrDefault(d =>
                        d.StartChannel == target.StartChannel &&
                        d.DeviceName == target.DeviceName);

                    if (actualDev != null)
                    {
                        SelectDeviceInternal(actualDev);
                    }
                }
            }

            UpdateToolbarButtons();
            _isSyncing = false;
        }
        public DesignCanvasPanel()
        {
            InitializeComponent();
            InitPreviewTimer();
        }

        public void StartPlacingDevice(string deviceName)
        {
            _selectedDeviceName = deviceName; _isPlacingDevice = true;
            GhostDeviceName.Text = _selectedDeviceName; GhostDevice.Visibility = Visibility.Visible;
            Point pos = Mouse.GetPosition(GlobalGhostCanvas);
            Canvas.SetLeft(GhostDevice, pos.X - 32); Canvas.SetTop(GhostDevice, pos.Y - 32);
            if (PlaceholderText != null) PlaceholderText.Visibility = Visibility.Hidden;
            ClearSelection();
        }

        public void ToggleGrid(bool isVisible) { if (GridBackground != null) GridBackground.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed; }
        public void TriggerDelete() => BtnDelete_Click(null, null);

        public void SyncSelectionFromTree(List<PlacedDeviceData> treeSelectedDevices)
        {
            _isSyncing = true;
            ClearSelectionInternal();
            foreach (var dev in treeSelectedDevices) SelectDeviceInternal(dev);
            UpdateToolbarButtons();
            _isSyncing = false;
        }

        private void ExecuteCommand(Command cmd)
        {
            cmd.RedoAction.Invoke();
            _undoStack.Push(cmd); _redoStack.Clear();
            UpdateToolbarButtons();
            OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); // ToList() để ép UI cập nhật
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0) { var cmd = _undoStack.Pop(); cmd.UndoAction.Invoke(); _redoStack.Push(cmd); UpdateToolbarButtons(); ClearSelection(); OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count > 0) { var cmd = _redoStack.Pop(); cmd.RedoAction.Invoke(); _undoStack.Push(cmd); UpdateToolbarButtons(); ClearSelection(); OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }
        }

        private void UpdateToolbarButtons()
        {
            BtnUndo.IsEnabled = _undoStack.Count > 0;
            BtnRedo.IsEnabled = _redoStack.Count > 0;

            bool hasMulti = _selectedDevices.Count > 1;
            BtnAlignHorizontal.IsEnabled = hasMulti;
            BtnAlignVertical.IsEnabled = hasMulti;
            BtnAlignSquare.IsEnabled = hasMulti;
            BtnAlignCircle.IsEnabled = hasMulti;
            BtnDelete.IsEnabled = _selectedDevices.Count > 0;

            // --- LOGIC MỚI: CHỈ BẬT NÚT GROUP NẾU CÙNG LOẠI ---
            // Dùng LINQ Distinct() để đếm xem có bao nhiêu loại thiết bị trong danh sách đang chọn
            bool isSameType = _selectedDevices.Select(d => d.DeviceName).Distinct().Count() == 1;
            BtnGroup.IsEnabled = hasMulti && isSameType;

            BtnUngroup.IsEnabled = _selectedDevices.Any(d => !string.IsNullOrEmpty(d.GroupId));
        }

        // --- LOGIC GỘP NHÓM ---
        private void BtnGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevices.Count < 2) return;
            _groupCounter++;
            string newGroupId = Guid.NewGuid().ToString();
            string newGroupName = $"Nhóm #{_groupCounter}";

            var devicesToGroup = _selectedDevices.ToList();
            var oldGroupData = devicesToGroup.Select(d => new { Dev = d, OldId = d.GroupId, OldName = d.GroupName }).ToList();

            Command groupCmd = new Command
            {
                UndoAction = () => { foreach (var item in oldGroupData) { item.Dev.GroupId = item.OldId; item.Dev.GroupName = item.OldName; } OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); },
                RedoAction = () => { foreach (var dev in devicesToGroup) { dev.GroupId = newGroupId; dev.GroupName = newGroupName; } OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }
            };
            ExecuteCommand(groupCmd);
        }

        private void BtnUngroup_Click(object sender, RoutedEventArgs e)
        {
            var devicesToUngroup = _selectedDevices.Where(d => !string.IsNullOrEmpty(d.GroupId)).ToList();
            if (devicesToUngroup.Count == 0) return;

            var oldGroupData = devicesToUngroup.Select(d => new { Dev = d, OldId = d.GroupId, OldName = d.GroupName }).ToList();

            Command ungroupCmd = new Command
            {
                UndoAction = () => { foreach (var item in oldGroupData) { item.Dev.GroupId = item.OldId; item.Dev.GroupName = item.OldName; } OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); },
                RedoAction = () => { foreach (var dev in devicesToUngroup) { dev.GroupId = null; dev.GroupName = null; } OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }
            };
            ExecuteCommand(ungroupCmd);
        }

        // --- XỬ LÝ CHUỘT ---
        private void DesignScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
                double newZoom = _zoomLevel * zoomFactor;
                if (newZoom < 0.2) newZoom = 0.2; if (newZoom > 5.0) newZoom = 5.0;
                if (newZoom == _zoomLevel) return;
                Point mousePos = e.GetPosition(DesignCanvas);
                _zoomLevel = newZoom; CanvasScaleTransform.ScaleX = _zoomLevel; CanvasScaleTransform.ScaleY = _zoomLevel;
                DesignScrollViewer.ScrollToHorizontalOffset(mousePos.X * _zoomLevel - e.GetPosition(DesignScrollViewer).X);
                DesignScrollViewer.ScrollToVerticalOffset(mousePos.Y * _zoomLevel - e.GetPosition(DesignScrollViewer).Y);
            }
        }

        private void DesignScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed) { _isPanning = true; _lastDragPoint = e.GetPosition(DesignScrollViewer); DesignScrollViewer.CaptureMouse(); DesignScrollViewer.Cursor = Cursors.ScrollAll; e.Handled = true; }
            else if (e.LeftButton == MouseButtonState.Pressed && !_isPlacingDevice)
            {
                if (e.OriginalSource == GridBackground || e.OriginalSource == DesignCanvas)
                {
                    ClearSelection(); _isMarqueeSelecting = true; _marqueeStartPoint = e.GetPosition(DesignCanvas);
                    Canvas.SetLeft(SelectionBox, _marqueeStartPoint.X); Canvas.SetTop(SelectionBox, _marqueeStartPoint.Y);
                    SelectionBox.Width = 0; SelectionBox.Height = 0; SelectionBox.Visibility = Visibility.Visible; DesignCanvas.CaptureMouse();
                }
            }
        }

        private void DesignScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Point canvasPos = e.GetPosition(DesignCanvas);
            if (_isPanning && _lastDragPoint.HasValue) { Point currentPoint = e.GetPosition(DesignScrollViewer); DesignScrollViewer.ScrollToHorizontalOffset(DesignScrollViewer.HorizontalOffset - (currentPoint.X - _lastDragPoint.Value.X)); DesignScrollViewer.ScrollToVerticalOffset(DesignScrollViewer.VerticalOffset - (currentPoint.Y - _lastDragPoint.Value.Y)); _lastDragPoint = currentPoint; }
            else if (_isMarqueeSelecting) { Canvas.SetLeft(SelectionBox, Math.Min(canvasPos.X, _marqueeStartPoint.X)); Canvas.SetTop(SelectionBox, Math.Min(canvasPos.Y, _marqueeStartPoint.Y)); SelectionBox.Width = Math.Abs(canvasPos.X - _marqueeStartPoint.X); SelectionBox.Height = Math.Abs(canvasPos.Y - _marqueeStartPoint.Y); }
            else if (_isDraggingDevice) { double deltaX = canvasPos.X - _deviceDragStartPoint.X; double deltaY = canvasPos.Y - _deviceDragStartPoint.Y; foreach (var device in _selectedDevices) { Point origPos = _originalDevicePositions[device]; Canvas.SetLeft(device.UIElement, origPos.X + deltaX); Canvas.SetTop(device.UIElement, origPos.Y + deltaY); } }
        }

        private void DesignScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Released && _isPanning) { _isPanning = false; DesignScrollViewer.ReleaseMouseCapture(); DesignScrollViewer.Cursor = Cursors.Arrow; }
            else if (e.LeftButton == MouseButtonState.Released && _isMarqueeSelecting)
            {
                _isMarqueeSelecting = false; SelectionBox.Visibility = Visibility.Collapsed; DesignCanvas.ReleaseMouseCapture();
                Rect selectionRect = new Rect(Canvas.GetLeft(SelectionBox), Canvas.GetTop(SelectionBox), SelectionBox.Width, SelectionBox.Height);
                _isSyncing = true;
                foreach (var device in _placedDevices)
                {
                    Rect deviceRect = new Rect(Canvas.GetLeft(device.UIElement), Canvas.GetTop(device.UIElement), device.UIElement.Width, device.UIElement.Height);
                    if (selectionRect.IntersectsWith(deviceRect))
                    {
                        // Nếu nằm trong khung chọn, chọn cả nhóm của nó
                        if (!string.IsNullOrEmpty(device.GroupId)) { foreach (var mate in _placedDevices.Where(d => d.GroupId == device.GroupId)) SelectDeviceInternal(mate); }
                        else SelectDeviceInternal(device);
                    }
                }
                _isSyncing = false; UpdateToolbarButtons(); OnSelectionChangedFromCanvas?.Invoke(this, _selectedDevices);
            }
            else if (e.LeftButton == MouseButtonState.Released && _isDraggingDevice)
            {
                _isDraggingDevice = false; DesignCanvas.ReleaseMouseCapture();
                var movedDevices = _selectedDevices.ToList(); var oldPositions = new Dictionary<PlacedDeviceData, Point>(_originalDevicePositions); var newPositions = new Dictionary<PlacedDeviceData, Point>();
                foreach (var dev in movedDevices) newPositions[dev] = new Point(Canvas.GetLeft(dev.UIElement), Canvas.GetTop(dev.UIElement));
                if (oldPositions.Any(kvp => kvp.Value != newPositions[kvp.Key]))
                {
                    Command moveCmd = new Command { UndoAction = () => { foreach (var dev in movedDevices) { Canvas.SetLeft(dev.UIElement, oldPositions[dev].X); Canvas.SetTop(dev.UIElement, oldPositions[dev].Y); } }, RedoAction = () => { foreach (var dev in movedDevices) { Canvas.SetLeft(dev.UIElement, newPositions[dev].X); Canvas.SetTop(dev.UIElement, newPositions[dev].Y); } } };
                    ExecuteCommand(moveCmd);
                }
            }
        }

        private void Device_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isPlacingDevice) return;
            Grid clickedUI = sender as Grid;
            PlacedDeviceData clickedDevice = _placedDevices.FirstOrDefault(d => d.UIElement == clickedUI);

            if (clickedDevice != null)
            {
                bool isMultiSelect = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                // Lấy toàn bộ thiết bị cùng nhóm (hoặc chỉ chính nó nếu ko có nhóm)
                List<PlacedDeviceData> devicesToSelect = string.IsNullOrEmpty(clickedDevice.GroupId)
                    ? new List<PlacedDeviceData> { clickedDevice }
                    : _placedDevices.Where(d => d.GroupId == clickedDevice.GroupId).ToList();

                if (!isMultiSelect && !_selectedDevices.Contains(clickedDevice)) ClearSelectionInternal();

                if (!_selectedDevices.Contains(clickedDevice)) { foreach (var d in devicesToSelect) SelectDeviceInternal(d); }
                else if (isMultiSelect) { foreach (var d in devicesToSelect) DeselectDeviceInternal(d); }

                UpdateToolbarButtons();
                if (!_isSyncing) OnSelectionChangedFromCanvas?.Invoke(this, _selectedDevices);

                _isDraggingDevice = true; _deviceDragStartPoint = e.GetPosition(DesignCanvas); _originalDevicePositions.Clear();
                foreach (var dev in _selectedDevices) _originalDevicePositions[dev] = new Point(Canvas.GetLeft(dev.UIElement), Canvas.GetTop(dev.UIElement));
                DesignCanvas.CaptureMouse(); e.Handled = true;
            }
        }

        private void SelectDeviceInternal(PlacedDeviceData device) { if (!_selectedDevices.Contains(device)) { _selectedDevices.Add(device); Border highlight = new Border { Name = "Highlight", BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(32), IsHitTestVisible = false }; device.UIElement.Children.Add(highlight); } }
        private void DeselectDeviceInternal(PlacedDeviceData device) { if (_selectedDevices.Contains(device)) { _selectedDevices.Remove(device); var highlight = device.UIElement.Children.OfType<Border>().FirstOrDefault(b => b.Name == "Highlight"); if (highlight != null) device.UIElement.Children.Remove(highlight); } }
        private void ClearSelectionInternal() { foreach (var device in _selectedDevices) { var highlight = device.UIElement.Children.OfType<Border>().FirstOrDefault(b => b.Name == "Highlight"); if (highlight != null) device.UIElement.Children.Remove(highlight); } _selectedDevices.Clear(); }
        private void ClearSelection() { ClearSelectionInternal(); UpdateToolbarButtons(); if (!_isSyncing) OnSelectionChangedFromCanvas?.Invoke(this, _selectedDevices); }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevices.Count == 0) return;
            var devicesToDelete = _selectedDevices.ToList();
            Command deleteCmd = new Command { UndoAction = () => { foreach (var dev in devicesToDelete) { DesignCanvas.Children.Add(dev.UIElement); _placedDevices.Add(dev); } CheckPlaceholder(); OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }, RedoAction = () => { foreach (var dev in devicesToDelete) { DesignCanvas.Children.Remove(dev.UIElement); _placedDevices.Remove(dev); } CheckPlaceholder(); OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); } };
            ExecuteCommand(deleteCmd); ClearSelection();
        }

        private void CheckPlaceholder() { if (PlaceholderText != null) PlaceholderText.Visibility = _placedDevices.Count == 0 ? Visibility.Visible : Visibility.Hidden; }

        private void UserControl_PreviewMouseMove(object sender, MouseEventArgs e) { if (_isPlacingDevice && GhostDevice.Visibility == Visibility.Visible) { Point pos = e.GetPosition(GlobalGhostCanvas); Canvas.SetLeft(GhostDevice, pos.X - 32); Canvas.SetTop(GhostDevice, pos.Y - 32); } }
        private void UserControl_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) { if (_isPlacingDevice) { _isPlacingDevice = false; GhostDevice.Visibility = Visibility.Collapsed; CheckPlaceholder(); } }

        private void DesignCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isPlacingDevice && e.LeftButton == MouseButtonState.Pressed)
            {
                _dropPosition = e.GetPosition(DesignCanvas); GhostDevice.Visibility = Visibility.Collapsed; ConfigDialogOverlay.Visibility = Visibility.Visible;
                int channelCount = (_selectedDeviceName == "LED RGB") ? 3 : 1;
                AddressInputTextBox.Text = GetNextAvailableAddress(channelCount).ToString(); AddressInputTextBox.SelectAll();
                ErrorTextBlock.Visibility = Visibility.Collapsed; AddressInputTextBox.Focus(); e.Handled = true;
            }
        }

        private void CancelConfig_Click(object sender, RoutedEventArgs e) { ConfigDialogOverlay.Visibility = Visibility.Collapsed; _isPlacingDevice = false; CheckPlaceholder(); }

        private int GetNextAvailableAddress(int requiredChannels)
        {
            for (int startAddr = 1; startAddr <= 512 - requiredChannels + 1; startAddr++)
            {
                int endAddr = startAddr + requiredChannels - 1; bool isOverlapping = false;
                foreach (var used in _placedDevices) if (Math.Max(startAddr, used.StartChannel) <= Math.Min(endAddr, used.EndChannel)) { isOverlapping = true; break; }
                if (!isOverlapping) return startAddr;
            }
            return 1;
        }

        private void ConfirmConfig_Click(object sender, RoutedEventArgs e)
        {
            string input = AddressInputTextBox.Text.Trim();
            if (!int.TryParse(input, out int startAddress) || startAddress < 1 || startAddress > 512) { ErrorTextBlock.Text = "Vui lòng nhập giá trị trong khoảng từ 1 đến 512."; ErrorTextBlock.Visibility = Visibility.Visible; return; }
            int channelCount = (_selectedDeviceName == "LED RGB") ? 3 : 1; int endAddress = startAddress + channelCount - 1;
            if (endAddress > 512) { ErrorTextBlock.Text = $"Thiết bị này chiếm {channelCount} kênh. Địa chỉ vượt quá 512."; ErrorTextBlock.Visibility = Visibility.Visible; return; }
            foreach (var used in _placedDevices) if (Math.Max(startAddress, used.StartChannel) <= Math.Min(endAddress, used.EndChannel)) { ErrorTextBlock.Text = "Địa chỉ này đã được sử dụng, vui lòng nhập lại."; ErrorTextBlock.Visibility = Visibility.Visible; return; }

            string addressText = (_selectedDeviceName == "LED RGB") ? $"{startAddress:D3}:{endAddress:D3}" : startAddress.ToString("D3");
            Grid newDevice = new Grid { Width = 64, Height = 64, Background = Brushes.Transparent };
            Ellipse circle = new Ellipse { Width = 60, Height = 60, Fill = new SolidColorBrush(Color.FromRgb(80, 80, 80)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            TextBlock nameText = new TextBlock { Text = _selectedDeviceName, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -14, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Width = 55, IsHitTestVisible = false };
            TextBlock addressTextBlock = new TextBlock { Text = addressText, Foreground = Brushes.LightGray, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), IsHitTestVisible = false };
            newDevice.Children.Add(circle); newDevice.Children.Add(nameText); newDevice.Children.Add(addressTextBlock);
            newDevice.MouseLeftButtonDown += Device_MouseLeftButtonDown;
            Canvas.SetLeft(newDevice, _dropPosition.X - 32); Canvas.SetTop(newDevice, _dropPosition.Y - 32);

            PlacedDeviceData newDeviceData = new PlacedDeviceData { DeviceName = _selectedDeviceName, StartChannel = startAddress, EndChannel = endAddress, UIElement = newDevice };
            Command addCmd = new Command { UndoAction = () => { DesignCanvas.Children.Remove(newDevice); _placedDevices.Remove(newDeviceData); CheckPlaceholder(); OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }, RedoAction = () => { DesignCanvas.Children.Add(newDevice); _placedDevices.Add(newDeviceData); CheckPlaceholder(); OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); } };
            ExecuteCommand(addCmd);
            ConfigDialogOverlay.Visibility = Visibility.Collapsed; _isPlacingDevice = false;
        }

        private void ArrangeDevices(string shape)
        {
            if (_selectedDevices.Count < 2) return;
            var devices = _selectedDevices.OrderBy(d => d.StartChannel).ToList();
            int n = devices.Count; double minX = devices.Min(d => Canvas.GetLeft(d.UIElement)); double maxX = devices.Max(d => Canvas.GetLeft(d.UIElement)); double minY = devices.Min(d => Canvas.GetTop(d.UIElement)); double maxY = devices.Max(d => Canvas.GetTop(d.UIElement));
            double centerX = minX + (maxX - minX + 64) / 2; double centerY = minY + (maxY - minY + 64) / 2;
            Point[] newPositions = new Point[n]; double spacingX = 70; double spacingY = 70;
            if (shape == "Horizontal") for (int i = 0; i < n; i++) newPositions[i] = new Point(i * spacingX, 0);
            else if (shape == "Vertical") for (int i = 0; i < n; i++) newPositions[i] = new Point(0, i * spacingY);
            else if (shape == "Square") { int s = 2; while ((4 * s - 4) < n && s < 100) s++; List<Point> outlinePoints = new List<Point>(); for (int r = 0; r < s; r++) for (int c = 0; c < s; c++) if (r == 0 || r == s - 1 || c == 0 || c == s - 1) outlinePoints.Add(new Point(c * spacingX, r * spacingY)); for (int i = 0; i < n; i++) newPositions[i] = outlinePoints[i]; }
            else if (shape == "Circle") { double radius = Math.Max(60, (n * spacingX) / (2 * Math.PI)); for (int i = 0; i < n; i++) { double angle = -Math.PI / 2 + (2 * Math.PI * i / n); newPositions[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)); } }
            double newMinX = newPositions.Min(p => p.X); double newMaxX = newPositions.Max(p => p.X); double newMinY = newPositions.Min(p => p.Y); double newMaxY = newPositions.Max(p => p.Y);
            double newCenterX = newMinX + (newMaxX - newMinX + 64) / 2; double newCenterY = newMinY + (newMaxY - newMinY + 64) / 2;
            double offsetX = centerX - newCenterX; double offsetY = centerY - newCenterY;
            for (int i = 0; i < n; i++) newPositions[i] = new Point(newPositions[i].X + offsetX, newPositions[i].Y + offsetY);
            var oldPositions = new Dictionary<PlacedDeviceData, Point>(); foreach (var dev in devices) oldPositions[dev] = new Point(Canvas.GetLeft(dev.UIElement), Canvas.GetTop(dev.UIElement));
            Command arrangeCmd = new Command { UndoAction = () => { foreach (var dev in devices) { Canvas.SetLeft(dev.UIElement, oldPositions[dev].X); Canvas.SetTop(dev.UIElement, oldPositions[dev].Y); } }, RedoAction = () => { for (int i = 0; i < n; i++) { Canvas.SetLeft(devices[i].UIElement, newPositions[i].X); Canvas.SetTop(devices[i].UIElement, newPositions[i].Y); } } };
            ExecuteCommand(arrangeCmd);
        }
        // --- HÀM ĐỔI TÊN NHÓM DO TREE PANEL GỌI ---
        public void RenameGroup(string groupId, string newName)
        {
            var devicesInGroup = _placedDevices.Where(d => d.GroupId == groupId).ToList();
            if (devicesInGroup.Count == 0) return;

            string oldName = devicesInGroup.First().GroupName;
            if (oldName == newName) return;

            // Tạo lệnh Undo/Redo cho việc đổi tên
            Command renameCmd = new Command
            {
                UndoAction = () => { foreach (var dev in devicesInGroup) dev.GroupName = oldName; OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); },
                RedoAction = () => { foreach (var dev in devicesInGroup) dev.GroupName = newName; OnDeviceListChanged?.Invoke(this, _placedDevices.ToList()); }
            };
            ExecuteCommand(renameCmd);
        }
        // ==========================================
        // HỆ THỐNG MÔ PHỎNG HIỆU ỨNG (PREVIEW)
        // ==========================================
        private DispatcherTimer _previewTimer;
        private List<EffectConfigData> _activeEffects = new List<EffectConfigData>();
        private List<TimelineEffectItem> _activeTimelineEffects = new List<TimelineEffectItem>();
        private double _timelineTimeMs = 0;
        public bool IsPlaying { get; set; } = false;

        // 1. THÊM BIẾN NÀY ĐỂ LƯU TRẠNG THÁI HIỆU ỨNG ĐANG XEM TRƯỚC
        private EffectConfigData _previewEffect = null;

        private System.Diagnostics.Stopwatch _previewStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private double _previewTick = 0;

        // 2. THÊM HÀM NÀY ĐỂ MAINWINDOW GỌI ĐƯỢC TỪ BẢNG CONFIG
        public void UpdatePreviewEffect(EffectConfigData configData)
        {
            if (configData == null)
            {
                _previewEffect = null;
                _previewTick = 0;
                return;
            }

            // Reset previewTick khi chuyển sang hiệu ứng khác hoặc thay đổi thông số cốt lõi để màu sắc/chu kỳ bắt đầu chính xác từ 0
            if (_previewEffect == null ||
                _previewEffect.EffectIndex != configData.EffectIndex ||
                _previewEffect.Speed != configData.Speed ||
                !_previewEffect.SelectedColor.Equals(configData.SelectedColor) ||
                _previewEffect != configData)
            {
                _previewTick = 0;
            }

            _previewEffect = configData;
        }

        // Gọi hàm này trong constructor của DesignCanvasPanel
        private void InitPreviewTimer()
        {
            _previewTimer = new DispatcherTimer();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(16); // 60 FPS để đồng bộ siêu mượt mà với Playhead và loại bỏ răng cưa/mất nhịp hiệu ứng nhanh
            _previewTimer.Tick += PreviewTimer_Tick;
            _previewTimer.Start();
        }

        public void UpdateActiveEffects(List<EffectConfigData> activeEffects)
        {
            _activeEffects = activeEffects;
        }

        public void UpdateActiveTimelineEffects(List<TimelineEffectItem> activeEffects, double timelineTimeMs)
        {
            _activeTimelineEffects = activeEffects;
            _timelineTimeMs = timelineTimeMs;
        }

        // 3. SỬA LẠI HÀM TICK ĐỂ CHẠY CẢ HIỆU ỨNG TIMELINE VÀ PREVIEW CÙNG LÚC
        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            double dt = _previewStopwatch.Elapsed.TotalSeconds;
            _previewStopwatch.Restart();

            double baseBpm = DMXScenarioDesigner.Services.AudioAnalysisService.CurrentBpm;

            // Chỉ tăng _previewTick khi kịch bản ĐANG CHẠY (hoặc nếu chúng ta không dùng chế độ timeline)
            // Để đơn giản và chính xác nhất: nếu IsPlaying = true, ta tăng _previewTick.
            // Nếu IsPlaying = false, ta KHÔNG tăng _previewTick (đóng băng preview của khối đang chọn khi dừng playhead).
            if (IsPlaying || _activeTimelineEffects == null || _activeTimelineEffects.Count == 0)
            {
                _previewTick += (dt * 20.0) * (baseBpm / 600.0);
            }

            // BƯỚC 1: LUÔN LUÔN reset toàn bộ tất cả thiết bị trên canvas về trạng thái tắt (màu xám)
            foreach (var device in _placedDevices)
            {
                var circle = device.UIElement.Children.OfType<Ellipse>().FirstOrDefault();
                if (circle != null) circle.Fill = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            }

            // BƯỚC 2: Xử lý các hiệu ứng đang chạy trên Timeline nếu có
            if (_activeTimelineEffects != null && _activeTimelineEffects.Count > 0)
            {
                foreach (var effect in _activeTimelineEffects)
                {
                    var config = effect.ConfigData;
                    if (config == null || config.TargetDevices == null) continue;

                    // Tính thời gian trôi qua tương đối bên trong khối hiệu ứng này (bắt đầu từ đúng 0)
                    double relativeTimeMs = _timelineTimeMs - effect.StartTime;
                    if (relativeTimeMs < 0) relativeTimeMs = 0;
                    if (relativeTimeMs > effect.Duration) relativeTimeMs = effect.Duration;

                    // Chuyển đổi mili giây tương đối thành nhịp chuyển động (tick) đồng bộ chính xác theo BPM và vạch thời gian thực tế
                    double tick = relativeTimeMs * (baseBpm / 30000.0);

                    double multiplier = 1.0;
                    switch (config.Speed)
                    {
                        case 1: multiplier = 0.5; break;
                        case 2: multiplier = 1.0; break;
                        case 3: multiplier = 1.5; break;
                        case 4: multiplier = 2.0; break;
                        case 5: multiplier = 2.5; break;
                    }
                    double currentAnimTick = tick * multiplier;

                    // Sắp xếp các thiết bị theo kênh bắt đầu để đảm bảo thứ tự chạy đuổi (chase/wipe/meteor) đúng chuẩn DMX
                    var sortedDevices = config.TargetDevices.OrderBy(d => d.StartChannel).ToList();
                    int totalDevices = sortedDevices.Count;

                    // BƯỚC 3: CHỈ chạy animation cho các thiết bị thuộc danh sách cấu hình của khối này
                    foreach (var device in config.TargetDevices)
                    {
                        if (!_placedDevices.Contains(device)) continue;

                        Ellipse circle = device.UIElement.Children.OfType<Ellipse>().FirstOrDefault();
                        if (circle == null) continue;

                        if (device.DeviceName == "LED RGB")
                        {
                            int deviceIndex = sortedDevices.IndexOf(device);
                            ApplyLedEffect(circle, config.EffectIndex, config.SelectedColor, currentAnimTick, deviceIndex, totalDevices);
                        }
                        else if (device.DeviceName == "MOTOR")
                        {
                            int deviceIndex = sortedDevices.IndexOf(device);
                            // Giảm tốc độ preview motor xuống 50% (nhân 0.5) để mô phỏng quán tính vật lý thực tế của cột nước
                            ApplyMotorEffect(circle, config.EffectIndex, config.Threshold, currentAnimTick * 0.5, deviceIndex, totalDevices);
                        }
                    }
                }
            }
            // Nếu không chạy timeline, chỉ hiện preview của hiệu ứng đang chọn (config)
            else if (_previewEffect != null)
            {
                var config = _previewEffect;
                if (config.TargetDevices != null && config.TargetDevices.Count > 0)
                {
                    double multiplier = 1.0;
                    switch (config.Speed)
                    {
                        case 1: multiplier = 0.5; break;
                        case 2: multiplier = 1.0; break;
                        case 3: multiplier = 1.5; break;
                        case 4: multiplier = 2.0; break;
                        case 5: multiplier = 2.5; break;
                    }
                    double currentAnimTick = _previewTick * multiplier;

                    var sortedDevices = config.TargetDevices.OrderBy(d => d.StartChannel).ToList();
                    int totalDevices = sortedDevices.Count;

                    foreach (var device in config.TargetDevices)
                    {
                        if (!_placedDevices.Contains(device)) continue;

                        Ellipse circle = device.UIElement.Children.OfType<Ellipse>().FirstOrDefault();
                        if (circle == null) continue;

                        if (device.DeviceName == "LED RGB")
                        {
                            int deviceIndex = sortedDevices.IndexOf(device);
                            ApplyLedEffect(circle, config.EffectIndex, config.SelectedColor, currentAnimTick, deviceIndex, totalDevices);
                        }
                        else if (device.DeviceName == "MOTOR")
                        {
                            int deviceIndex = sortedDevices.IndexOf(device);
                            // Giảm tốc độ preview motor xuống 50% (nhân 0.5) để mô phỏng quán tính vật lý thực tế của cột nước
                            ApplyMotorEffect(circle, config.EffectIndex, config.Threshold, currentAnimTick * 0.5, deviceIndex, totalDevices);
                        }
                    }
                }
            }
        }
        private void ApplyLedEffect(Ellipse circle, int effectIndex, Color baseColor, double tick, int deviceIndex, int totalDevices)
        {
            Color c = Colors.Black;
            if (totalDevices <= 0) totalDevices = 1;

            switch (effectIndex)
            {
                case 1: // Cầu vồng (Rainbow)
                    double rainbowHue = (tick * 20 + deviceIndex * 25) % 360;
                    c = GetRainbowColor(rainbowHue, 1.0);
                    break;

                case 2: // Cầu vồng quét (Rainbow Wipe)
                    double rainbowWipeTick = tick * 2.0;
                    double rainbowWipeCycle = rainbowWipeTick % (totalDevices * 1.2 + 4);
                    double baseHueWipe = Math.Floor(rainbowWipeTick / (totalDevices * 1.2 + 4)) * 50;
                    if (deviceIndex <= rainbowWipeCycle)
                    {
                        double hue = (baseHueWipe + deviceIndex * 20) % 360;
                        c = GetRainbowColor(hue, 1.0);
                    }
                    else
                    {
                        c = Color.FromRgb(80, 80, 80);
                    }
                    break;

                case 3: // Chạy đuổi (Chase)
                    double chaseTick = tick * 2.0;
                    int chasePos = (int)(chaseTick) % totalDevices;
                    double trail = (chasePos - deviceIndex + totalDevices) % totalDevices;
                    double chaseIntensity = 0;
                    if (trail == 0) chaseIntensity = 1.0;
                    else if (trail == 1) chaseIntensity = 0.6;
                    else if (trail == 2) chaseIntensity = 0.3;
                    else if (trail == 3) chaseIntensity = 0.1;

                    c = Color.FromRgb(
                        (byte)(80 + (baseColor.R - 80) * chaseIntensity),
                        (byte)(80 + (baseColor.G - 80) * chaseIntensity),
                        (byte)(80 + (baseColor.B - 80) * chaseIntensity)
                    );
                    break;

                case 4: // Nhịp thở (Breathing)
                    double breathBrightness = 0.5 * (1 - Math.Cos(tick * 3.14159));
                    c = Color.FromRgb(
                        (byte)(baseColor.R * breathBrightness),
                        (byte)(baseColor.G * breathBrightness),
                        (byte)(baseColor.B * breathBrightness)
                    );
                    break;

                case 5: // Cầu vồng nhịp thở (Rainbow Breathing)
                    double rainbowBreathBrightness = 0.5 * (1 - Math.Cos(tick * 3.14159));
                    double rainbowBreathHue = (tick * 10 + deviceIndex * 15) % 360;
                    c = GetRainbowColor(rainbowBreathHue, rainbowBreathBrightness);
                    break;

                case 6: // Quét màu (Color Wipe)
                    double colorWipeTick = tick * 2.0;
                    double colorWipeCycle = colorWipeTick % (totalDevices * 1.2 + 4);
                    if (deviceIndex <= colorWipeCycle)
                    {
                        double diff = colorWipeCycle - deviceIndex;
                        double intensity = diff >= 1.0 ? 1.0 : diff;
                        c = Color.FromRgb(
                            (byte)(80 + (baseColor.R - 80) * intensity),
                            (byte)(80 + (baseColor.G - 80) * intensity),
                            (byte)(80 + (baseColor.B - 80) * intensity)
                        );
                    }
                    else
                    {
                        c = Color.FromRgb(80, 80, 80);
                    }
                    break;

                case 7: // Chớp (Strobe)
                    bool strobeState = (tick % 2.0) < 0.3;
                    c = strobeState ? baseColor : Color.FromRgb(80, 80, 80);
                    break;

                case 8: // Chuyển sắc (Gradient Shift)
                    double gradHue = (tick * 15 + deviceIndex * 360.0 / totalDevices) % 360;
                    c = GetRainbowColor(gradHue, 1.0);
                    break;

                case 9: // Lấp lánh (Glitter)
                    double glitterVal = Math.Sin(tick * 3.5 + deviceIndex * 23.3) * Math.Cos(tick * 2.2 - deviceIndex * 13.7);
                    double glitterIntensity = (glitterVal > 0.35) ? (glitterVal - 0.35) / 0.65 : 0;
                    c = Color.FromRgb(
                        (byte)(80 + (baseColor.R - 80) * glitterIntensity),
                        (byte)(80 + (baseColor.G - 80) * glitterIntensity),
                        (byte)(80 + (baseColor.B - 80) * glitterIntensity)
                    );
                    break;

                case 10: // Sao băng (Meteor)
                    double meteorTick = tick * 4.0;
                    double meteorHead = meteorTick % (totalDevices + 15);
                    double distToHead = meteorHead - deviceIndex;
                    double meteorIntensity = 0;
                    if (distToHead >= 0 && distToHead < 10)
                    {
                        meteorIntensity = Math.Pow(1.0 - (distToHead / 10.0), 1.5);
                    }
                    c = Color.FromRgb(
                        (byte)(80 + (baseColor.R - 80) * meteorIntensity),
                        (byte)(80 + (baseColor.G - 80) * meteorIntensity),
                        (byte)(80 + (baseColor.B - 80) * meteorIntensity)
                    );
                    break;

                case 11: // Xung màu (Pulse Gradient)
                    double pulseIntensity = 0.1 + 0.9 * Math.Pow(Math.Abs(Math.Cos(tick * 3.14159)), 4.0);
                    double pulseHue = (tick * 12 + deviceIndex * 360.0 / totalDevices) % 360;
                    c = GetRainbowColor(pulseHue, pulseIntensity);
                    break;

                case 12: // Tĩnh (Color)
                    c = baseColor;
                    break;

                default:
                    c = GetRainbowColor((tick * 10) % 360, 0.8);
                    break;
            }

            // Nếu màu quá tối hoặc ở chế độ tắt, trả về mặc định màu xám
            if (effectIndex != 12 && c.R < 20 && c.G < 20 && c.B < 20)
                circle.Fill = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            else if (effectIndex == 12 && baseColor.R == 0 && baseColor.G == 0 && baseColor.B == 0)
                circle.Fill = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            else
                circle.Fill = new SolidColorBrush(c);
        }

        private void ApplyMotorEffect(Ellipse circle, int effectIndex, int threshold, double tick, int deviceIndex, int totalDevices)
        {
            double fillPercent = 0;

            switch (effectIndex)
            {
                case 13: // Fixed Level (Đặt mức cố định)
                    fillPercent = threshold;
                    break;

                case 14: // Global Breathing (Tăng giảm đồng loạt)
                    {
                        double wave = (Math.Sin(tick * 0.785398) + 1.0) / 2.0;
                        fillPercent = wave * threshold;
                        break;
                    }

                case 15: // Wave Sweep (Sóng lan truyền)
                    {
                        double phase = (tick * 0.785398) - (deviceIndex * 1.0);
                        double wave = (Math.Sin(phase) + 1.0) / 2.0;
                        fillPercent = wave * threshold;
                        break;
                    }

                case 16: // Alternate Wave (Xen kẽ đối xứng)
                    {
                        double phase = tick * 0.785398;
                        if (deviceIndex % 2 != 0)
                        {
                            phase += Math.PI;
                        }
                        double wave = (Math.Sin(phase) + 1.0) / 2.0;
                        fillPercent = wave * threshold;
                        break;
                    }

                case 17: // Left To Right Sweep (Quét trái sang phải)
                    {
                        int pos = ((int)(tick / 2.0)) % totalDevices;
                        fillPercent = (deviceIndex == pos) ? threshold : 0;
                        break;
                    }

                case 18: // Center Out Sweep (Quét giữa ra ngoài)
                    {
                        int center = totalDevices / 2;
                        int distance = Math.Abs(deviceIndex - center);
                        double phase = (tick * 0.785398) - (distance * 1.2);
                        double wave = (Math.Sin(phase) + 1.0) / 2.0;
                        fillPercent = wave * threshold;
                        break;
                    }

                case 19: // Edge To Center Sweep (Quét ngoài vào giữa)
                    {
                        int center = totalDevices / 2;
                        int distance = Math.Abs(deviceIndex - center);
                        double phase = (tick * 0.785398) + (distance * 1.2);
                        double wave = (Math.Sin(phase) + 1.0) / 2.0;
                        fillPercent = wave * threshold;
                        break;
                    }

                case 20: // Peak Runner (Đỉnh nhọn chạy)
                    {
                        int pos = ((int)(tick / 2.0)) % totalDevices;
                        int distance = Math.Abs(deviceIndex - pos);
                        if (distance < 3)
                        {
                            double bright = 1.0 - ((double)distance / 3.0);
                            fillPercent = bright * threshold;
                        }
                        else
                        {
                            fillPercent = 0;
                        }
                        break;
                    }

                case 21: // Fountain Fan (Xòe quạt)
                    {
                        int center = totalDevices / 2;
                        int distance = Math.Abs(deviceIndex - center);
                        double baseVal = (Math.Sin(tick * 0.785398) + 1.0) / 2.0;
                        double scale = 1.0 - ((double)distance / (double)(center + 1));
                        fillPercent = baseVal * scale * threshold;
                        break;
                    }

                default:
                    fillPercent = threshold;
                    break;
            }

            // Tạo hiệu ứng mực nước / lực bơm dâng lên bằng LinearGradientBrush từ dưới lên trên
            double percent = fillPercent / 100.0;
            if (percent < 0) percent = 0; if (percent > 1) percent = 1;

            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 1); // Cạnh dưới (y = 1, offset = 0.0)
            brush.EndPoint = new Point(0, 0);   // Cạnh trên (y = 0, offset = 1.0)

            brush.GradientStops.Add(new GradientStop(Colors.LightSkyBlue, percent));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(80, 80, 80), Math.Min(1.0, percent + 0.001)));
            circle.Fill = brush;
        }
        private Color GetRainbowColor(double h, double v)
        {
            double phase = h % 360;
            if (phase < 0) phase += 360;

            int hi = Convert.ToInt32(Math.Floor(phase / 60.0)) % 6;
            double f = (phase / 60.0) - Math.Floor(phase / 60.0);

            byte vByte = Convert.ToByte(v * 255);
            byte pByte = 0;
            byte qByte = Convert.ToByte(v * 255 * (1.0 - f));
            byte tByte = Convert.ToByte(v * 255 * f);

            if (hi == 0) return Color.FromRgb(vByte, tByte, pByte);
            else if (hi == 1) return Color.FromRgb(qByte, vByte, pByte);
            else if (hi == 2) return Color.FromRgb(pByte, vByte, tByte);
            else if (hi == 3) return Color.FromRgb(pByte, qByte, vByte);
            else if (hi == 4) return Color.FromRgb(tByte, pByte, vByte);
            else return Color.FromRgb(vByte, pByte, qByte);
        }
        private void BtnAlignHorizontal_Click(object sender, RoutedEventArgs e) => ArrangeDevices("Horizontal");
        private void BtnAlignVertical_Click(object sender, RoutedEventArgs e) => ArrangeDevices("Vertical");
        private void BtnAlignSquare_Click(object sender, RoutedEventArgs e) => ArrangeDevices("Square");
        private void BtnAlignCircle_Click(object sender, RoutedEventArgs e) => ArrangeDevices("Circle");
    }
}