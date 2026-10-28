using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DMXScenarioDesigner
{
    public partial class TimelineListPanel : UserControl
    {
        public event EventHandler<double> OnScrollChanged;
        private ObservableCollection<TimelineTrack> _tracks;

        public TimelineListPanel() { InitializeComponent(); }

        public void SetData(ObservableCollection<TimelineTrack> tracks)
        {
            _tracks = tracks;
            TracksItemsControl.ItemsSource = _tracks;
        }

        public void SyncVerticalScroll(double offset)
        {
            ListScroll.ScrollToVerticalOffset(offset);
        }

        private void ListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            OnScrollChanged?.Invoke(this, e.VerticalOffset);
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as Button).DataContext as TimelineTrack;
            if (track != null && !track.IsPlaceholder) _tracks.Remove(track);
        }
    }
}