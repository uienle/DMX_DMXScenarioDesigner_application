using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMXScenarioDesigner
{
    public partial class DeviceTreePanel : UserControl
    {
        // Sự kiện gửi yêu cầu đổi tên sang MainWindow
        public class RenameEventArgs : EventArgs { public string GroupId { get; set; } public string NewName { get; set; } }
        public event EventHandler<RenameEventArgs> OnGroupRenamed;

        private string _editingGroupId; // Lưu ID của nhóm đang được đổi tên
        public event EventHandler<List<PlacedDeviceData>> OnSelectionChangedFromTree;
        private bool _isSyncing = false;

        // Class giả lập giao diện Cây (Tree)
        public class TreeListItem
        {
            public bool IsGroupHeader { get; set; }
            public string GroupId { get; set; }
            public string DisplayText { get; set; }
            public PlacedDeviceData Device { get; set; }
            public Thickness Margin { get; set; }
            public FontWeight FontWeight { get; set; }
            public Brush TextColor { get; set; }
        }

        public DeviceTreePanel() { InitializeComponent(); }

        public void UpdateList(List<PlacedDeviceData> devices)
        {
            _isSyncing = true;

            // Lưu lại các thiết bị đang được chọn
            var oldSelectedDevices = DeviceListBox.SelectedItems.Cast<TreeListItem>()
                                        .Where(t => !t.IsGroupHeader)
                                        .Select(t => t.Device).ToList();

            var displayList = new List<TreeListItem>();

            // 1. XỬ LÝ CÁC THIẾT BỊ ĐÃ GỘP NHÓM
            var grouped = devices.Where(d => !string.IsNullOrEmpty(d.GroupId))
                                 .GroupBy(d => new { d.GroupId, d.GroupName })
                                 .OrderBy(g => g.Key.GroupName);

            foreach (var group in grouped)
            {
                // Thêm Thư mục cha (Header)
                displayList.Add(new TreeListItem
                {
                    IsGroupHeader = true,
                    GroupId = group.Key.GroupId,
                    DisplayText = group.Key.GroupName,
                    Margin = new Thickness(0, 5, 0, 2),
                    FontWeight = FontWeights.Bold,
                    TextColor = Brushes.Black
                });

                // Thêm các thiết bị con (Thụt lề vào trong)
                var sortedChildren = group.OrderBy(d => GetDeviceTypePriority(d.DeviceName)).ThenBy(d => d.StartChannel);
                foreach (var child in sortedChildren)
                {
                    displayList.Add(new TreeListItem
                    {
                        IsGroupHeader = false,
                        Device = child,
                        DisplayText = "   " + child.DisplayText, // Thụt lề
                        Margin = new Thickness(15, 0, 0, 0),
                        FontWeight = FontWeights.Normal,
                        TextColor = Brushes.DarkSlateGray
                    });
                }
            }

            // 2. XỬ LÝ CÁC THIẾT BỊ ĐỘC LẬP (Không nhóm)
            var ungrouped = devices.Where(d => string.IsNullOrEmpty(d.GroupId))
                                   .OrderBy(d => GetDeviceTypePriority(d.DeviceName)).ThenBy(d => d.StartChannel);

            foreach (var child in ungrouped)
            {
                displayList.Add(new TreeListItem
                {
                    IsGroupHeader = false,
                    Device = child,
                    DisplayText =  child.DisplayText,
                    Margin = new Thickness(0, 2, 0, 2),
                    FontWeight = FontWeights.Normal,
                    TextColor = Brushes.Black
                });
            }

            // Gán data mới để UI tự Refresh
            DeviceListBox.ItemsSource = displayList;

            // Phục hồi lại trạng thái bôi đen
            foreach (var item in displayList)
            {
                if (!item.IsGroupHeader && oldSelectedDevices.Any(o => o.UIElement == item.Device.UIElement))
                {
                    DeviceListBox.SelectedItems.Add(item);
                }
            }
            _isSyncing = false;
        }

        private int GetDeviceTypePriority(string deviceName)
        {
            if (deviceName == "LED RGB") return 1;
            if (deviceName == "MOTOR") return 2;
            if (deviceName == "Kênh đơn") return 3;
            return 4;
        }

        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing) return;

            var selectedItems = DeviceListBox.SelectedItems.Cast<TreeListItem>().ToList();
            var selectedDevices = new List<PlacedDeviceData>();

            foreach (var item in selectedItems)
            {
                if (item.IsGroupHeader)
                {
                    // Nếu click vào Thư mục cha -> Chọn toàn bộ thiết bị con của nó
                    var children = ((List<TreeListItem>)DeviceListBox.ItemsSource)
                                    .Where(t => !t.IsGroupHeader && t.Device.GroupId == item.GroupId)
                                    .Select(t => t.Device);
                    selectedDevices.AddRange(children);
                }
                else
                {
                    selectedDevices.Add(item.Device);
                }
            }

            // Xóa trùng lặp và gửi sang Canvas
            selectedDevices = selectedDevices.Distinct().ToList();
            OnSelectionChangedFromTree?.Invoke(this, selectedDevices);
        }

        public void SyncSelectionFromCanvas(List<PlacedDeviceData> selectedDevices)
        {
            _isSyncing = true;
            DeviceListBox.SelectedItems.Clear();

            if (DeviceListBox.ItemsSource != null)
            {
                var allItems = DeviceListBox.ItemsSource.Cast<TreeListItem>().ToList();
                foreach (var dev in selectedDevices)
                {
                    var itemToSelect = allItems.FirstOrDefault(t => !t.IsGroupHeader && t.Device.UIElement == dev.UIElement);
                    if (itemToSelect != null) DeviceListBox.SelectedItems.Add(itemToSelect);
                }
            }
            _isSyncing = false;
        }
        // --- LOGIC ĐỔI TÊN NHÓM ---
        private void DeviceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DeviceListBox.SelectedItem is TreeListItem selectedItem && selectedItem.IsGroupHeader)
            {
                _editingGroupId = selectedItem.GroupId;
                // Bỏ icon thư mục "📂 " để lấy tên gốc
                //RenameTextBox.Text = selectedItem.DisplayText.Replace("📂 ", "");
                RenameOverlay.Visibility = Visibility.Visible;
                RenameTextBox.Focus();
                RenameTextBox.SelectAll();
            }
        }

        private void ConfirmRename_Click(object sender, RoutedEventArgs e)
        {
            string newName = RenameTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(newName) && !string.IsNullOrEmpty(_editingGroupId))
            {
                // Gửi sự kiện yêu cầu đổi tên
                OnGroupRenamed?.Invoke(this, new RenameEventArgs { GroupId = _editingGroupId, NewName = newName });
            }
            RenameOverlay.Visibility = Visibility.Collapsed;
        }

        private void CancelRename_Click(object sender, RoutedEventArgs e) => RenameOverlay.Visibility = Visibility.Collapsed;

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ConfirmRename_Click(null, null);
            else if (e.Key == Key.Escape) CancelRename_Click(null, null);
        }
    }
}