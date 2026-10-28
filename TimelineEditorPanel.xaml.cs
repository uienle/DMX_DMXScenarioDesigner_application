using DMXScenarioDesigner.Services;
using NAudio.Dsp;
using NAudio.Midi;
using NAudio.Wave;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DMXScenarioDesigner
{
    public class ZoomConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is double timeValue && values[1] is double msToPx)
            {
                return timeValue * msToPx;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class TimelineEditorPanel : UserControl
    {
        public event EventHandler<double> OnScrollChanged;
        public event EventHandler<List<TimelineEffectItem>> OnActiveEffectsChanged;
        public event EventHandler<TimelineEffectItem> OnEffectSelected;
        public event EventHandler<bool> OnPlayStateChanged;

        private ObservableCollection<TimelineTrack> _tracks;
        private const double HeaderHeight = 65;
        private AudioService _audioService = new AudioService();
        private bool _audioLoaded = false;

        private ObservableCollection<TimelineAudioItem> _audioTracks = new ObservableCollection<TimelineAudioItem>();
        private List<float> _waveformData = new List<float>();
        private List<double> _beatMarkers = new List<double>();
        private List<TimelineSection> _sectionMarkers = new List<TimelineSection>();
        private TimelineAudioItem _selectedAudio;
        private Border _selectedAudioBorder;
        private double _currentBpm = 0;
        private double _internalEstimatedBpm = 120.0;
        private double _internalFirstBeatMs = 0.0;

        // =================================================================================
        // --- DEPENDENCY PROPERTY CHO ZOOM (MsToPx) ---
        // =================================================================================
        public static readonly DependencyProperty MsToPxProperty =
            DependencyProperty.Register("MsToPx", typeof(double), typeof(TimelineEditorPanel),
            new PropertyMetadata(0.026, OnMsToPxChanged));

        public double MsToPx
        {
            get { return (double)GetValue(MsToPxProperty); }
            set { SetValue(MsToPxProperty, value); }
        }

        private static void OnMsToPxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = d as TimelineEditorPanel;
            if (panel != null)
            {
                panel.UpdateTimelineWidth();
                panel.UpdateGridBackground();
                panel.UpdatePlayhead();
                panel.UpdateStopMarkerPosition();
                panel.DrawWaveform();
                panel.DrawSectionMarkers();

                // Dùng Timer để chống lag khi lăn chuột liên tục
                panel._zoomDebounceTimer.Stop();
                panel._zoomDebounceTimer.Start();
            }
        }

        private void DrawSectionMarkers()
        {
            if (BeatCanvas == null) return;
            BeatCanvas.Children.Clear();
            foreach (var section in _sectionMarkers)
            {
                double x = section.StartTimeMs * MsToPx;

                // Vẽ đường line section
                Line line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = TimelineGrid.ActualHeight,
                    Stroke = Brushes.Red,
                    StrokeThickness = 1,
                    Opacity = 0.35
                };
                BeatCanvas.Children.Add(line);
            }
        }

        private void DrawWaveform()
        {
            if (WaveformCanvas == null) return;
            WaveformCanvas.Children.Clear();

            if (_waveformData == null || _waveformData.Count == 0) return;

            double totalHeight = TimelineGrid.ActualHeight;
            double centerY = totalHeight / 2;
            double waveformHeight = totalHeight * 0.45;
            double totalDuration = _audioService.Duration.TotalMilliseconds;
            double pxPerSample = (totalDuration * MsToPx) / _waveformData.Count;

            for (int i = 0; i < _waveformData.Count; i++)
            {
                double x = i * pxPerSample;
                double amplitude = _waveformData[i] * waveformHeight;

                Line line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = centerY - amplitude,
                    Y2 = centerY + amplitude,
                    Stroke = Brushes.LightGreen,
                    StrokeThickness = 1,
                    Opacity = 0.75
                };
                WaveformCanvas.Children.Add(line);
            }
        }

        private bool _isPlaying = false;
        public bool IsPlaying => _isPlaying;
        private double _currentTime = 0;
        public double CurrentTime => _currentTime;
        private DateTime _lastTick;

        // Vạch dừng Playhead (Stop Marker)
        private Grid _stopMarker;
        private double _stopTimeMs = 20000; // Mặc định 20 giây khi chưa có nhạc
        private bool _isDraggingStopMarker = false;
        private bool _hasStoppedAtStopMarker = false;

        private bool _isMovingEffect = false;
        private Point _startMovePoint;
        private TimelineEffectItem _movingEffect;
        private double _originalStartTime;
        private TimelineTrack _originalTrack;

        private TimelineEffectItem _selectedEffect;
        private Border _selectedBorder;

        private HistoryManager _historyManager;
        private bool _isDraggingPlayhead = false;

        private DispatcherTimer _zoomDebounceTimer;

        public TimelineEditorPanel()
        {
            InitializeComponent();

            BtnBeatDectect.Content = "Hiện đánh dấu";
            BtnBeatDectect.ToolTip = "Đánh dấu các đoạn của bài nhạc (section)";

            _zoomDebounceTimer = new DispatcherTimer();
            _zoomDebounceTimer.Interval = TimeSpan.FromMilliseconds(30);
            _zoomDebounceTimer.Tick += (s, e) => {
                _zoomDebounceTimer.Stop();
                DrawRuler();
            };

            UpdateTimelineWidth();
            DrawRuler();
            UpdateGridBackground();
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _historyManager = new HistoryManager();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeStopMarker();
            UpdateTimelineWidth();
            DrawRuler();
        }

        private void EditorScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTimelineWidth();
            DrawRuler();
        }

        public void SetData(ObservableCollection<TimelineTrack> tracks)
        {
            _tracks = tracks;
            EditorTracksItemsControl.ItemsSource = _tracks;
            AudioItemsControl.ItemsSource = _audioTracks;
            SaveStateToHistory();
            UpdateTimelineWidth();
        }

        public void SyncVerticalScroll(double offset) { EditorScroll.ScrollToVerticalOffset(offset); }

        // =================================================================================
        // --- LOGIC TÍNH TOÁN BƯỚC NHẢY (GRID STEP) DỰA TRÊN ZOOM ---
        // =================================================================================
        private double GetCurrentStepMs()
        {
            double idealStepMs = 20.0 / MsToPx;

            if (_currentBpm > 0)
            {
                double beatMs = 60000.0 / _currentBpm;
                double[] beatMultipliers = { 0.125, 0.25, 0.5, 1, 2, 4, 8, 16 };

                double bestStep = beatMs;
                double minDiff = double.MaxValue;

                foreach (double mult in beatMultipliers)
                {
                    double step = beatMs * mult;
                    double diff = Math.Abs(idealStepMs - step);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        bestStep = step;
                    }
                }
                return bestStep;
            }
            else
            {
                double[] allowedSteps = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 6000 };

                double bestStep = allowedSteps[0];
                double minDiff = Math.Abs(idealStepMs - bestStep);

                foreach (double step in allowedSteps)
                {
                    double diff = Math.Abs(idealStepMs - step);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        bestStep = step;
                    }
                }
                return bestStep;
            }
        }

        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;

                double zoomFactor = e.Delta > 0 ? 1.15 : 1 / 1.15;
                double minMsToPx = 20.0 / 6000.0;
                double maxMsToPx = 20.0 / 10.0;

                double newMsToPx = Math.Max(minMsToPx, Math.Min(maxMsToPx, MsToPx * zoomFactor));

                if (newMsToPx == MsToPx) return;

                // Tính toán vị trí chuột hiện tại trên TimelineGrid
                Point mousePosInGrid = e.GetPosition(TimelineGrid);
                double timeAtMouse = mousePosInGrid.X / MsToPx;

                // Lấy vị trí cuộn trên màn hình
                double mousePosInViewport = e.GetPosition(EditorScroll).X;

                MsToPx = newMsToPx;

                // Chờ giao diện render kích thước mới rồi mới cuộn để đảm bảo không bị vượt giới hạn (Clamp)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    double newMouseX = timeAtMouse * MsToPx;
                    double scrollOffset = newMouseX - mousePosInViewport;
                    EditorScroll.ScrollToHorizontalOffset(scrollOffset);
                }), DispatcherPriority.Render);
            }
        }

        private void UpdateGridBackground()
        {
            double stepMs = GetCurrentStepMs();
            double cellWidth = stepMs * MsToPx;
            if (cellWidth < 2) cellWidth = 2;

            var rect = new Rectangle { Width = cellWidth, Height = 40, Stroke = Brushes.LightGray, StrokeThickness = 0.5 };
            var brush = new VisualBrush
            {
                Visual = rect,
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, cellWidth, 40),
                ViewportUnits = BrushMappingMode.Absolute
            };
            TimelineGrid.Background = brush;
        }

        // =================================================================================
        // --- VẼ THƯỚC ĐO (CULLING OPTIMIZATION - 0 LAG) ---
        // =================================================================================
        private void DrawRuler()
        {
            if (RulerCanvas == null || EditorScroll == null) return;
            RulerCanvas.Children.Clear();

            double stepMs = GetCurrentStepMs();
            double majorStepMs = _currentBpm > 0 ? stepMs * 4 : stepMs * 10;

            // TỐI ƯU: Chỉ vẽ trong vùng đang hiển thị trên màn hình
            double offset = EditorScroll.HorizontalOffset;
            double viewport = EditorScroll.ViewportWidth;
            if (double.IsNaN(viewport) || viewport <= 0) viewport = 1000;

            double startMs = offset / MsToPx;
            double endMs = (offset + viewport) / MsToPx;

            // Làm tròn xuống mốc lớn gần nhất để không bị mất chữ khi cuộn
            double startI = Math.Floor(startMs / majorStepMs) * majorStepMs;
            double endI = Math.Ceiling(endMs / majorStepMs) * majorStepMs;

            startI = Math.Max(0, startI);

            for (double i = startI; i <= endI; i += stepMs)
            {
                double x = i * MsToPx;
                Line line = new Line { X1 = x, X2 = x, Y1 = 18, Y2 = 25, Stroke = Brushes.Gray, StrokeThickness = 0.5 };

                // check if major step
                bool isMajor = Math.Abs(i % majorStepMs) < 0.1 || Math.Abs((i % majorStepMs) - majorStepMs) < 0.1;

                if (isMajor)
                {
                    line.Y1 = 10; line.Stroke = Brushes.Black;
                    TimeSpan ts = TimeSpan.FromMilliseconds(i);
                    string timeStr = string.Format("{0:D2}m:{1:D2}s:{2:D2}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);

                    TextBlock txt = new TextBlock
                    {
                        Text = timeStr,
                        Foreground = Brushes.Black,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold
                    };
                    Canvas.SetLeft(txt, x + 3);
                    Canvas.SetTop(txt, 0);
                    Panel.SetZIndex(txt, 10);
                    RulerCanvas.Children.Add(txt);
                }
                RulerCanvas.Children.Add(line);
            }
        }

        private void EditorScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            OnScrollChanged?.Invoke(this, e.VerticalOffset);
            Canvas.SetLeft(RulerCanvas, -e.HorizontalOffset);

            // Vẽ lại thước đo khi cuộn ngang (rất nhẹ vì đã dùng Culling)
            if (e.HorizontalChange != 0)
            {
                DrawRuler();
            }
        }

        private void UpdateTimelineWidth()
        {
            if (EditorScroll == null || TimelineGrid == null || RulerCanvas == null) return;

            double maxTimeMs = 0;
            if (_tracks != null)
            {
                foreach (var track in _tracks)
                {
                    foreach (var effect in track.Effects)
                    {
                        double endTime = effect.StartTime + effect.Duration;
                        if (endTime > maxTimeMs) maxTimeMs = endTime;
                    }
                }
            }

            if (_audioTracks != null)
            {
                foreach (var audio in _audioTracks)
                {
                    double endTime = audio.StartTime + audio.Duration;
                    if (endTime > maxTimeMs) maxTimeMs = endTime;
                }
            }

            if (_currentTime > maxTimeMs)
            {
                maxTimeMs = _currentTime;
            }

            double requiredWidth = (maxTimeMs + 10000) * MsToPx;
            double viewportWidth = EditorScroll.ViewportWidth;
            if (double.IsNaN(viewportWidth) || viewportWidth <= 0) viewportWidth = 800;

            double finalWidth = Math.Max(viewportWidth, requiredWidth);

            TimelineGrid.Width = finalWidth;
            RulerCanvas.Width = finalWidth;
        }

        // --- PLAYBACK & TIMING ---
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (!_isPlaying) return;

            var now = DateTime.Now;
            double elapsed = (now - _lastTick).TotalMilliseconds;
            _lastTick = now;

            if (_audioLoaded)
            {
                double audioTime = _audioService.CurrentTime.TotalMilliseconds;
                double diff = audioTime - _currentTime;

                if (Math.Abs(diff) > 150)
                {
                    // Lệch quá nhiều (ví dụ tua nhanh hoặc giật lag lớn), nhảy thẳng tới thời gian nhạc
                    _currentTime = audioTime;
                }
                else
                {
                    // Tăng mượt mà dựa trên thời gian thực trôi qua, tự điều chỉnh cực nhỏ để bám sát nhạc
                    // Nếu thời gian nhạc chạy nhanh hơn, tăng nhẹ tốc độ playhead lên 2%.
                    // Nếu thời gian nhạc chạy chậm hơn, giảm nhẹ tốc độ playhead đi 2%.
                    // Giải pháp này loại bỏ hoàn toàn hiện tượng răng cưa/giật hình do bước đệm của NAudio.
                    double speedAdjustment = 1.0;
                    if (diff > 20) speedAdjustment = 1.02;      // Chạy nhanh hơn 2% để đuổi kịp nhạc
                    else if (diff < -20) speedAdjustment = 0.98; // Chạy chậm hơn 2% để chờ nhạc

                    _currentTime += elapsed * speedAdjustment;
                }
            }
            else
            {
                _currentTime += elapsed;
                if (_currentTime > 600000) _currentTime = 0;
            }

            // Kiểm tra chạm vạch dừng Playhead
            if (_currentTime >= _stopTimeMs && !_hasStoppedAtStopMarker)
            {
                _currentTime = _stopTimeMs; // Đặt playhead chính xác tại vạch dừng
                _hasStoppedAtStopMarker = true;
                PausePlayback();
                UpdatePlayhead();
                UpdateTimingDisplay();
                return;
            }

            UpdatePlayhead();
            UpdateTimingDisplay();
            CheckActiveEffects();
        }

        private void UpdatePlayhead()
        {
            if (Playhead != null)
            {
                double playheadX = _currentTime * MsToPx;
                Canvas.SetLeft(Playhead, playheadX);

                if (_isPlaying)
                {
                    double offset = EditorScroll.HorizontalOffset;
                    double viewportWidth = EditorScroll.ViewportWidth;
                    if (double.IsNaN(viewportWidth) || viewportWidth <= 0) viewportWidth = 800;

                    // Cập nhật lại width của Timeline nếu đang chạm tới phần đuôi chưa mở rộng của Grid
                    if (playheadX > TimelineGrid.Width - (viewportWidth / 2))
                    {
                        UpdateTimelineWidth();
                    }

                    // Nếu Playhead chạm tới 4/5 màn hình thì cuộn để Playhead giữ tại đó, tạo cảm giác di chuyển mượt mà
                    double threshold = offset + viewportWidth * 0.8;
                    if (playheadX > threshold)
                    {
                        // Cuộn timeline sao cho Playhead luôn nằm ở mức 80% của viewport
                        EditorScroll.ScrollToHorizontalOffset(playheadX - viewportWidth * 0.8);
                    }
                    // Nếu lỡ seek ngược hoặc Playhead nằm ngoài bên trái màn hình
                    else if (playheadX < offset)
                    {
                        EditorScroll.ScrollToHorizontalOffset(Math.Max(0, playheadX - 50));
                    }
                }
            }
        }

        private void UpdateTimingDisplay()
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(_currentTime);
            CurrentTimeText.Text = string.Format("{0:D2}m:{1:D2}s:{2:D2}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
        }

        private void CheckActiveEffects()
        {
            if (_tracks == null) return;
            List<TimelineEffectItem> activeEffects = new List<TimelineEffectItem>();

            foreach (var track in _tracks)
            {
                foreach (var effect in track.Effects)
                {
                    if (_currentTime >= effect.StartTime && _currentTime <= (effect.StartTime + effect.Duration))
                    {
                        activeEffects.Add(effect);
                    }
                }
            }
            OnActiveEffectsChanged?.Invoke(this, activeEffects);
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = !_isPlaying;
            OnPlayStateChanged?.Invoke(this, _isPlaying);

            if (_isPlaying)
            {
                // Cập nhật trạng thái dừng khi bắt đầu phát nhạc
                if (_currentTime < _stopTimeMs)
                {
                    _hasStoppedAtStopMarker = false;
                }
                else
                {
                    _hasStoppedAtStopMarker = true; // Đang chạy sau vạch dừng, không dừng lại nữa
                }

                if (_audioLoaded)
                {
                    _audioService.Seek(TimeSpan.FromMilliseconds(_currentTime));
                    _audioService.Play();
                }

                _lastTick = DateTime.Now;
                BtnPlayPause.Content = "⏸";
            }
            else
            {
                if (_audioLoaded)
                {
                    _audioService.Pause();
                }

                BtnPlayPause.Content = "▶";
                CheckActiveEffects();
            }
        }

        private void BtnReplay_Click(object sender, RoutedEventArgs e)
        {
            _currentTime = 0;
            _hasStoppedAtStopMarker = false; // Reset lại trạng thái để dừng khi chạm mốc tiếp theo

            if (_audioLoaded)
            {
                _audioService.Seek(TimeSpan.Zero);
            }

            UpdatePlayhead();
            UpdateTimingDisplay();
            CheckActiveEffects();

            if (_isPlaying)
            {
                _lastTick = DateTime.Now;
            }
        }

        private async void BtnAddMusic_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Audio Files|*.mp3;*.wav";

            if (openFileDialog.ShowDialog() == true)
            {
                BtnAddMusic.IsEnabled = false;
                BtnBeatDectect.IsEnabled = false;

                // Tạo cửa sổ thông báo "Vui lòng chờ" với phong cách màu xám trắng sáng đồng bộ với hệ thống
                Window pleaseWait = new Window
                {
                    Title = "Đang tải nhạc",
                    Width = 240,
                    Height = 80,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                    ShowInTaskbar = false
                };

                Grid grid = new Grid();
                TextBlock txtTitle = new TextBlock
                {
                    Text = "Vui lòng chờ...",
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                grid.Children.Add(txtTitle);

                pleaseWait.Content = grid;
                pleaseWait.Show();

                // Đợi 100ms để WPF hoàn tất vẽ giao diện "Vui lòng chờ"
                await Task.Delay(100);

                try
                {
                    string filePath = openFileDialog.FileName;

                    // Load audio reader (có thể tốn 50-100ms)
                    _audioService.Load(filePath);
                    _audioLoaded = true;

                    // Vạch dừng nằm cách điểm kết thúc bài nhạc 2s
                    _stopTimeMs = Math.Max(0, _audioService.Duration.TotalMilliseconds - 2000);
                    _hasStoppedAtStopMarker = false;
                    UpdateStopMarkerPosition();

                    _audioTracks.Clear();

                    _audioTracks.Add(new TimelineAudioItem
                    {
                        StartTime = 0,
                        Duration = _audioService.Duration.TotalMilliseconds,
                        FilePath = filePath,
                        DisplayName = System.IO.Path.GetFileName(filePath)
                    });

                    // Clear old beats
                    _beatMarkers.Clear();
                    _sectionMarkers.Clear();
                    BeatCanvas.Children.Clear();
                    _currentBpm = 0;
                    UpdateGridBackground();
                    DrawRuler();

                    // 1. Lấy waveform data song song trong background (mượt mà, không đơ UI)
                    _waveformData = await Task.Run(() => DMXScenarioDesigner.Services.AudioAnalysisService.GenerateWaveformData(filePath));

                    // 2. Chạy audio analysis để lấy BPM và các Section Markers đồng thời (mượt mà, không đơ UI)
                    var result = await Task.Run(() => DMXScenarioDesigner.Services.AudioAnalysisService.AnalyzeAudio(filePath, _audioService.Duration.TotalMilliseconds, _waveformData));

                    _internalFirstBeatMs = result.FirstBeatMs;
                    _sectionMarkers = result.Sections;
                    _currentBpm = result.EstimatedBpm;
                    DMXScenarioDesigner.Services.AudioAnalysisService.CurrentBpm = _currentBpm; // Đồng bộ BPM ra toàn bộ hệ thống

                    // Vẽ waveform và cập nhật giao diện nhịp lưới
                    DrawWaveform();
                    UpdateGridBackground();
                    DrawRuler();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi tải nhạc: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // Chắc chắn đóng cửa sổ "Vui lòng chờ"
                    pleaseWait.Close();
                }

                BtnAddMusic.IsEnabled = true;
                BtnBeatDectect.IsEnabled = true;

                MessageBox.Show($"Đã thêm {System.IO.Path.GetFileName(openFileDialog.FileName)}. BPM: {_currentBpm:F1}");
            }
        }

        private void BtnBeatDectect_Click(object sender, RoutedEventArgs e)
        {
            if (!_audioLoaded) return;

            // Nút "Đánh dấu" giờ chỉ chuyên biệt dùng để hiện các vạch đánh dấu phân đoạn (section break)
            DrawSectionMarkers();

            MessageBox.Show($"Đã đánh dấu {_sectionMarkers.Count} vị trí");
        }

        // --- KÉO RÊ PLAYHEAD (SCRUBBING) & VẠCH DỪNG (STOP MARKER) ---
        private void TimelineGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (SnapPreview.Visibility == Visibility.Visible) return;

            Point mousePos = e.GetPosition(TimelineGrid);
            double stopMarkerX = _stopTimeMs * MsToPx;

            // Kiểm tra click theo tọa độ: nếu click trong phạm vi 15px quanh vạch dừng (thân vạch dừng có chiều rộng 24px)
            bool clickedStopMarker = Math.Abs(mousePos.X - stopMarkerX) <= 15;

            // Kiểm tra bổ trợ qua Visual Tree để đảm bảo độ chính xác tuyệt đối
            if (!clickedStopMarker)
            {
                DependencyObject clickedElement = e.OriginalSource as DependencyObject;
                while (clickedElement != null)
                {
                    if (clickedElement == _stopMarker)
                    {
                        clickedStopMarker = true;
                        break;
                    }
                    clickedElement = VisualTreeHelper.GetParent(clickedElement);
                }
            }

            if (clickedStopMarker)
            {
                _isDraggingStopMarker = true;
                if (sender is UIElement element) element.CaptureMouse();
                UpdateStopTimeFromMouse(mousePos);
                e.Handled = true;
                return;
            }

            if (e.OriginalSource is Rectangle || e.OriginalSource is Grid || e.OriginalSource is Canvas)
            {
                _isDraggingPlayhead = true;
                if (sender is UIElement element) element.CaptureMouse();
                UpdateTimeFromMouse(mousePos);
                SelectEffect(null, null);
                SelectAudio(null, null);
            }
        }

        private void TimelineGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingStopMarker)
            {
                UpdateStopTimeFromMouse(e.GetPosition(TimelineGrid));
                e.Handled = true;
            }
            else if (_isDraggingPlayhead)
            {
                UpdateTimeFromMouse(e.GetPosition(TimelineGrid));
            }
        }

        private void TimelineGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingStopMarker)
            {
                _isDraggingStopMarker = false;
                if (sender is UIElement element) element.ReleaseMouseCapture();
                e.Handled = true;
            }
            else if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                if (sender is UIElement element) element.ReleaseMouseCapture();
            }
        }

        private void UpdateTimeFromMouse(Point p)
        {
            double clickedTime = p.X / MsToPx;

            _currentTime = Math.Max(0, Math.Min(clickedTime, 600000));

            // Cập nhật trạng thái vạch dừng khi kéo Playhead
            _hasStoppedAtStopMarker = (_currentTime >= _stopTimeMs);

            if (_audioLoaded)
            {
                _audioService.Seek(TimeSpan.FromMilliseconds(_currentTime));
            }

            UpdatePlayhead();
            UpdateTimingDisplay();
            CheckActiveEffects();
        }

        private void UpdateStopTimeFromMouse(Point p)
        {
            double targetX = p.X;
            double newStopTimeMs = targetX / MsToPx;
            newStopTimeMs = Math.Max(0, Math.Min(newStopTimeMs, 600000));

            _stopTimeMs = newStopTimeMs;
            UpdateStopMarkerPosition();

            // Nếu Playhead hiện tại đang nằm trước vạch dừng mới, reset trạng thái dừng
            if (_currentTime < _stopTimeMs)
            {
                _hasStoppedAtStopMarker = false;
            }
        }

        // =================================================================================
        // --- LOGIC VẠCH DỪNG PLAYHEAD (STOP MARKER) ---
        // =================================================================================
        private void InitializeStopMarker()
        {
            if (_stopMarker != null) return;

            Canvas parentCanvas = Playhead?.Parent as Canvas;
            if (parentCanvas == null)
            {
                if (BeatCanvas != null) parentCanvas = BeatCanvas;
                else if (WaveformCanvas != null) parentCanvas = WaveformCanvas;
                else if (RulerCanvas != null) parentCanvas = RulerCanvas;
            }
            if (parentCanvas == null) return;

            // Tạo Container Grid cho vạch dừng
            _stopMarker = new Grid
            {
                Width = 24, // Rộng đủ để dễ click kéo thả
                Height = parentCanvas.ActualHeight > 0 ? parentCanvas.ActualHeight : 400,
                Background = Brushes.Transparent, // Nhận sự kiện click chuột
                Cursor = Cursors.SizeWE,
                ToolTip = "Vạch dừng Playhead (Giữ và kéo để di chuyển)",
                IsHitTestVisible = true
            };

            // Vẽ đường Line đứt đoạn
            Line line = new Line
            {
                X1 = 12,
                X2 = 12,
                Y1 = 0,
                Y2 = _stopMarker.Height,
                Stroke = new SolidColorBrush(Color.FromRgb(220, 53, 69)), // Màu đỏ nổi bật
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false // Bỏ qua hit test trên chính đường line để nhận ở Grid container
            };
            _stopMarker.Children.Add(line);

            // Handle bằng nút tròn có icon dừng 🛑 ở trên cùng
            Border handle = new Border
            {
                Width = 24,
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.SizeWE,
                IsHitTestVisible = true,
                Child = new TextBlock
                {
                    Text = "🛑",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White,
                    IsHitTestVisible = false
                }
            };
            _stopMarker.Children.Add(handle);

            // Đặt vị trí ban đầu
            Canvas.SetLeft(_stopMarker, _stopTimeMs * MsToPx - 12);
            Canvas.SetTop(_stopMarker, 0);
            Panel.SetZIndex(_stopMarker, 1000); // Luôn nổi lên trên cùng

            // Đăng ký sự kiện kéo thả chuột
            _stopMarker.MouseLeftButtonDown += StopMarker_MouseLeftButtonDown;
            _stopMarker.MouseMove += StopMarker_MouseMove;
            _stopMarker.MouseLeftButtonUp += StopMarker_MouseLeftButtonUp;

            // Cập nhật chiều cao vạch dừng khi Canvas thay đổi kích thước
            parentCanvas.SizeChanged += (s, e) =>
            {
                _stopMarker.Height = parentCanvas.ActualHeight;
                line.Y2 = parentCanvas.ActualHeight;
            };

            parentCanvas.Children.Add(_stopMarker);
        }

        private void StopMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingStopMarker = true;
            if (TimelineGrid != null)
            {
                TimelineGrid.CaptureMouse();
            }
            else
            {
                _stopMarker.CaptureMouse();
            }
            e.Handled = true;
        }

        private void StopMarker_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingStopMarker)
            {
                if (TimelineGrid != null)
                {
                    UpdateStopTimeFromMouse(e.GetPosition(TimelineGrid));
                }
                else
                {
                    Canvas parentCanvas = _stopMarker.Parent as Canvas;
                    if (parentCanvas != null)
                    {
                        Point pos = e.GetPosition(parentCanvas);
                        double targetX = pos.X;
                        double newStopTimeMs = targetX / MsToPx;
                        newStopTimeMs = Math.Max(0, Math.Min(newStopTimeMs, 600000));
                        _stopTimeMs = newStopTimeMs;
                        UpdateStopMarkerPosition();
                        if (_currentTime < _stopTimeMs)
                        {
                            _hasStoppedAtStopMarker = false;
                        }
                    }
                }
                e.Handled = true;
            }
        }

        private void StopMarker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingStopMarker)
            {
                _isDraggingStopMarker = false;
                if (TimelineGrid != null)
                {
                    TimelineGrid.ReleaseMouseCapture();
                }
                else
                {
                    _stopMarker.ReleaseMouseCapture();
                }
                e.Handled = true;
            }
        }

        public void UpdateStopMarkerPosition()
        {
            if (_stopMarker != null)
            {
                Canvas.SetLeft(_stopMarker, _stopTimeMs * MsToPx - 12);
            }
        }

        private void PausePlayback()
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            OnPlayStateChanged?.Invoke(this, _isPlaying);

            if (_audioLoaded)
            {
                _audioService.Pause();
            }

            BtnPlayPause.Content = "▶";
            CheckActiveEffects();
        }

        private void CleanupEmptyTracks()
        {
            if (_tracks == null) return;
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                var track = _tracks[i];
                if (!track.IsPlaceholder && track.Effects.Count == 0)
                {
                    _tracks.RemoveAt(i);
                }
            }
            int trackNum = 1;
            foreach (var track in _tracks)
            {
                if (!track.IsPlaceholder)
                {
                    track.TrackName = "Timeline " + trackNum;
                    trackNum++;
                }
            }
        }

        // --- UNDO / REDO LOGIC ---
        private void SaveStateToHistory()
        {
            if (_tracks == null) return;
            _historyManager.SaveState(_tracks);
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            BtnUndo.IsEnabled = _historyManager.CanUndo;
            BtnRedo.IsEnabled = _historyManager.CanRedo;
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_historyManager.CanUndo)
            {
                var previousState = _historyManager.Undo();
                RestoreState(previousState);
                UpdateUndoRedoButtons();
                SelectEffect(null, null);
                UpdateTimelineWidth();
                DrawRuler();
            }
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_historyManager.CanRedo)
            {
                var nextState = _historyManager.Redo();
                RestoreState(nextState);
                UpdateUndoRedoButtons();
                SelectEffect(null, null);
                UpdateTimelineWidth();
                DrawRuler();
            }
        }

        private void RestoreState(ObservableCollection<TimelineTrack> state)
        {
            if (state == null) return;
            _tracks.Clear();
            foreach (var track in state)
            {
                var newTrack = new TimelineTrack
                {
                    TrackName = track.TrackName,
                    IsPlaceholder = track.IsPlaceholder,
                    Effects = new ObservableCollection<TimelineEffectItem>()
                };
                foreach (var eff in track.Effects)
                {
                    newTrack.Effects.Add(new TimelineEffectItem
                    {
                        EffectName = eff.EffectName,
                        StartTime = eff.StartTime,
                        Duration = eff.Duration,
                        ConfigData = eff.ConfigData?.Clone()
                    });
                }
                _tracks.Add(newTrack);
            }
        }

        // --- HÀM HỖ TRỢ KIỂM TRA VA CHẠM ---
        private bool IsOverlapping(TimelineTrack track, TimelineEffectItem ignoreEffect, double start, double duration)
        {
            double end = start + duration;
            foreach (var otherEffect in track.Effects)
            {
                if (otherEffect == ignoreEffect) continue;
                double otherStart = otherEffect.StartTime;
                double otherEnd = otherEffect.StartTime + otherEffect.Duration;
                if (start < otherEnd && end > otherStart) return true;
            }
            return false;
        }

        private double GetValidMoveStartTime(TimelineTrack track, TimelineEffectItem movingEffect, double desiredStart)
        {
            double desiredEnd = desiredStart + movingEffect.Duration;
            double validStart = desiredStart;

            foreach (var other in track.Effects)
            {
                if (other == movingEffect) continue;
                double otherStart = other.StartTime;
                double otherEnd = other.StartTime + other.Duration;

                if (desiredStart < otherEnd && desiredEnd > otherStart)
                {
                    if (_originalStartTime < otherStart) validStart = otherStart - movingEffect.Duration;
                    else validStart = otherEnd;
                }
            }
            return Math.Max(0, validStart);
        }

        // --- DRAG & DROP TỪ NGOÀI VÀO ---
        public void ShowSnapPreview(Point p)
        {
            SnapPreview.Visibility = Visibility.Visible;
            double gridY = p.Y - HeaderHeight;
            double absoluteX = p.X + EditorScroll.HorizontalOffset;

            double snapMs = GetCurrentStepMs();
            double defaultWidth = 5000 * MsToPx; // 5 giây mặc định
            double snapX = Math.Round((absoluteX - defaultWidth / 2) / (snapMs * MsToPx)) * (snapMs * MsToPx);
            double snapY = Math.Floor(gridY / 40) * 40 + 5;

            int trackIndex = (int)(gridY / 40);
            if (trackIndex >= 0 && trackIndex < _tracks.Count)
            {
                var track = _tracks[trackIndex];
                double startTime = Math.Max(0, snapX) / MsToPx;

                if (IsOverlapping(track, null, startTime, 5000))
                {
                    SnapPreview.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x00, 0x00));
                }
                else
                {
                    SnapPreview.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x33, 0x33, 0x33));
                }
            }

            Canvas.SetLeft(SnapPreview, Math.Max(0, snapX));
            Canvas.SetTop(SnapPreview, snapY);
            SnapPreview.Width = defaultWidth;
        }

        public void HideSnapPreview() { SnapPreview.Visibility = Visibility.Collapsed; }

        public void HandleDrop(Point p, string effectName, object configData = null)
        {
            double gridY = p.Y - HeaderHeight;
            int trackIndex = (int)(gridY / 40);

            if (trackIndex >= 0 && trackIndex < _tracks.Count)
            {
                var track = _tracks[trackIndex];
                double absoluteX = p.X + EditorScroll.HorizontalOffset;

                double snapMs = GetCurrentStepMs();
                double defaultWidth = 5000 * MsToPx;
                double snappedX = Math.Round((absoluteX - defaultWidth / 2) / (snapMs * MsToPx)) * (snapMs * MsToPx);
                double startTime = Math.Max(0, snappedX) / MsToPx;

                if (IsOverlapping(track, null, startTime, 5000))
                {
                    MessageBox.Show("Không thể đặt hiệu ứng ở đây vì bị đè lên hiệu ứng khác!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                track.Effects.Add(new TimelineEffectItem
                {
                    EffectName = effectName,
                    StartTime = startTime,
                    Duration = 5000,
                    ConfigData = configData != null ? ((EffectConfigData)configData).Clone() : null
                });

                if (track.IsPlaceholder)
                {
                    track.IsPlaceholder = false;
                    track.TrackName = "Timeline " + _tracks.Count;
                    _tracks.Add(new TimelineTrack { TrackName = "Thêm Timeline...", IsPlaceholder = true });
                }

                SaveStateToHistory();
                UpdateTimelineWidth();
                DrawRuler();
            }
        }

        // --- CHỌN, XÓA KHỐI audio ---
        private void SelectAudio(TimelineAudioItem audio, Border border)
        {
            if (_selectedAudioBorder != null)
                _selectedAudioBorder.BorderThickness = new Thickness(0);

            _selectedAudio = audio;
            _selectedAudioBorder = border;

            if (border != null)
            {
                border.BorderBrush = Brushes.DeepSkyBlue;
                border.BorderThickness = new Thickness(2);
                BtnDelete.IsEnabled = true;

                // Clear effect selection when selecting audio
                if (_selectedBorder != null) _selectedBorder.Tag = null;
                _selectedEffect = null;
                _selectedBorder = null;
                OnEffectSelected?.Invoke(this, null);
            }
            else
            {
                if (_selectedEffect == null)
                {
                    BtnDelete.IsEnabled = false;
                }
            }
        }
        private void Audio_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border?.DataContext is TimelineAudioItem audio)
            {
                SelectAudio(audio, border);
                e.Handled = true;
            }
        }

        // --- CHỌN, XÓA VÀ DI CHUYỂN KHỐI HIỆU ỨNG ---
        public void Effect_Loaded(object sender, RoutedEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                border.Opacity = 0.75; // 75% Opacity để không che khuất Waveform đằng sau
            }
        }

        private void SelectEffect(TimelineEffectItem effect, Border border)
        {
            if (_selectedBorder != null) _selectedBorder.Tag = null;

            _selectedEffect = effect;
            _selectedBorder = border;

            if (_selectedBorder != null)
            {
                _selectedBorder.Tag = "Selected";
                this.Focus();
                BtnDelete.IsEnabled = true;

                // Clear audio selection when selecting an effect
                if (_selectedAudioBorder != null)
                    _selectedAudioBorder.BorderThickness = new Thickness(0);
                _selectedAudio = null;
                _selectedAudioBorder = null;
            }
            else
            {
                if (_selectedAudio == null)
                {
                    BtnDelete.IsEnabled = false;
                }
            }
            OnEffectSelected?.Invoke(this, _selectedEffect);
        }

        private void Effect_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            border.Opacity = 0.75;
            var effect = border.DataContext as TimelineEffectItem;
            if (effect != null)
            {
                SelectEffect(effect, border);

                _isMovingEffect = true;
                _movingEffect = effect;
                _startMovePoint = e.GetPosition(TimelineGrid);
                _originalStartTime = _movingEffect.StartTime;
                _originalTrack = FindTrackContainingEffect(_movingEffect);

                border.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Effect_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isMovingEffect || _movingEffect == null) return;

            Point currentPoint = e.GetPosition(TimelineGrid);

            double gridY = currentPoint.Y;
            int newTrackIndex = (int)(gridY / 40);
            TimelineTrack currentTrack = _originalTrack;

            if (newTrackIndex >= 0 && newTrackIndex < _tracks.Count && _originalTrack != null)
            {
                var newTrack = _tracks[newTrackIndex];

                if (newTrack != _originalTrack && !newTrack.IsPlaceholder)
                {
                    if (!IsOverlapping(newTrack, null, _movingEffect.StartTime, _movingEffect.Duration))
                    {
                        _originalTrack.Effects.Remove(_movingEffect);
                        newTrack.Effects.Add(_movingEffect);
                        _originalTrack = newTrack;
                        currentTrack = newTrack;
                    }
                }
            }

            double diffX = currentPoint.X - _startMovePoint.X;
            double desiredStartTime = _originalStartTime + (diffX / MsToPx);

            double snapMs = GetCurrentStepMs();
            double snappedDesiredStart = Math.Max(0, Math.Round(desiredStartTime / snapMs) * snapMs);

            double validStartTime = GetValidMoveStartTime(currentTrack, _movingEffect, snappedDesiredStart);
            _movingEffect.StartTime = validStartTime;

            UpdateTimelineWidth();

            e.Handled = true;
        }

        private void Effect_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMovingEffect)
            {
                _isMovingEffect = false;
                var border = sender as Border;
                if (border != null) border.ReleaseMouseCapture();

                if (_movingEffect != null && (_movingEffect.StartTime != _originalStartTime || _originalTrack != FindTrackContainingEffect(_movingEffect)))
                {
                    CleanupEmptyTracks();
                    SaveStateToHistory();
                    UpdateTimelineWidth();
                    DrawRuler();
                }

                _movingEffect = null;
                _originalTrack = null;
                e.Handled = true;
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e) { DeleteSelectedEffect(); }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedEffect();
            }
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnUndo_Click(null, null);
            }
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnRedo_Click(null, null);
            }
        }

        private void DeleteSelectedEffect()
        {
            if (_selectedEffect != null)
            {
                var track = FindTrackContainingEffect(_selectedEffect);
                if (track != null)
                {
                    track.Effects.Remove(_selectedEffect);
                    CleanupEmptyTracks();
                    SaveStateToHistory();
                    UpdateTimelineWidth();
                    DrawRuler();
                }
                SelectEffect(null, null);
            }
            if (_selectedAudio != null)
            {
                _audioTracks.Remove(_selectedAudio);
                _selectedAudio = null;
                _audioLoaded = false;
                _audioService.Stop();
                WaveformCanvas.Children.Clear();

                // Reset vạch dừng về mặc định 20s khi không có nhạc
                _stopTimeMs = 20000;
                _hasStoppedAtStopMarker = false;
                UpdateStopMarkerPosition();

                // Clear beats and bpm
                _beatMarkers.Clear();
                _sectionMarkers.Clear();
                BeatCanvas.Children.Clear();
                _currentBpm = 0;
                UpdateGridBackground();
                DrawRuler();

                SaveStateToHistory();
                return;
            }
        }

        public void RemoveEffectsWithoutDevices(List<PlacedDeviceData> survivingDevices)
        {
            if (_tracks == null) return;
            bool changed = false;

            foreach (var track in _tracks.ToList())
            {
                var effectsToRemove = track.Effects.Where(eff =>
                    eff.ConfigData != null &&
                    eff.ConfigData.TargetDevices != null &&
                    eff.ConfigData.TargetDevices.Count > 0 &&
                    !eff.ConfigData.TargetDevices.Any(d => survivingDevices.Contains(d))
                ).ToList();

                foreach (var eff in effectsToRemove)
                {
                    track.Effects.Remove(eff);
                    changed = true;
                }
            }

            if (changed)
            {
                CleanupEmptyTracks();
                SaveStateToHistory();
                UpdateTimelineWidth();
                DrawRuler();
            }
        }

        private TimelineTrack FindTrackContainingEffect(TimelineEffectItem effect)
        {
            if (_tracks == null || effect == null) return null;
            foreach (var track in _tracks)
            {
                if (track.Effects.Contains(effect)) return track;
            }
            return null;
        }

        // --- RESIZE 2 ĐẦU CỦA KHỐI HIỆU ỨNG ---
        private void ThumbRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            var effect = thumb?.DataContext as TimelineEffectItem;
            if (effect == null) return;

            TimelineTrack track = FindTrackContainingEffect(effect);
            if (track == null) return;

            double snapMs = GetCurrentStepMs();
            double newDuration = effect.Duration + (e.HorizontalChange / MsToPx);
            double snappedDuration = Math.Max(snapMs, Math.Round(newDuration / snapMs) * snapMs);

            if (!IsOverlapping(track, effect, effect.StartTime, snappedDuration))
            {
                effect.Duration = snappedDuration;
            }
            else
            {
                var nextEffect = track.Effects.Where(x => x != effect && x.StartTime >= effect.StartTime).OrderBy(x => x.StartTime).FirstOrDefault();
                if (nextEffect != null)
                {
                    double maxDuration = nextEffect.StartTime - effect.StartTime;
                    effect.Duration = Math.Max(snapMs, Math.Round(maxDuration / snapMs) * snapMs);
                }
            }
            UpdateTimelineWidth();
            e.Handled = true;
        }

        private void ThumbLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            var effect = thumb?.DataContext as TimelineEffectItem;
            if (effect == null) return;

            TimelineTrack track = FindTrackContainingEffect(effect);
            if (track == null) return;

            double snapMs = GetCurrentStepMs();
            double changeMs = e.HorizontalChange / MsToPx;
            double newStart = effect.StartTime + changeMs;
            double newDuration = effect.Duration - changeMs;

            if (newDuration >= snapMs && newStart >= 0)
            {
                double snappedStart = Math.Round(newStart / snapMs) * snapMs;
                double snappedDuration = Math.Round(newDuration / snapMs) * snapMs;

                if (!IsOverlapping(track, effect, snappedStart, snappedDuration))
                {
                    effect.StartTime = snappedStart;
                    effect.Duration = snappedDuration;
                }
                else
                {
                    var prevEffect = track.Effects.Where(x => x != effect && x.StartTime <= effect.StartTime).OrderByDescending(x => x.StartTime).FirstOrDefault();
                    if (prevEffect != null)
                    {
                        double minStart = prevEffect.StartTime + prevEffect.Duration;
                        double maxChange = effect.StartTime - minStart;
                        effect.StartTime = Math.Round(minStart / snapMs) * snapMs;
                        effect.Duration = Math.Round((effect.Duration + maxChange) / snapMs) * snapMs;
                    }
                }
            }
            e.Handled = true;
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SaveStateToHistory();
        }
    }

    // --- CLASS QUẢN LÝ UNDO / REDO ---
    public class HistoryManager
    {
        private Stack<ObservableCollection<TimelineTrack>> _undoStack = new Stack<ObservableCollection<TimelineTrack>>();
        private Stack<ObservableCollection<TimelineTrack>> _redoStack = new Stack<ObservableCollection<TimelineTrack>>();

        public bool CanUndo => _undoStack.Count > 1;
        public bool CanRedo => _redoStack.Count > 0;

        public void SaveState(ObservableCollection<TimelineTrack> currentTracks)
        {
            _redoStack.Clear();
            _undoStack.Push(DeepCopy(currentTracks));
        }

        public ObservableCollection<TimelineTrack> Undo()
        {
            if (CanUndo)
            {
                var currentState = _undoStack.Pop();
                _redoStack.Push(currentState);
                return DeepCopy(_undoStack.Peek());
            }
            return null;
        }

        public ObservableCollection<TimelineTrack> Redo()
        {
            if (CanRedo)
            {
                var nextState = _redoStack.Pop();
                _undoStack.Push(nextState);
                return DeepCopy(nextState);
            }
            return null;
        }

        private ObservableCollection<TimelineTrack> DeepCopy(ObservableCollection<TimelineTrack> source)
        {
            var copy = new ObservableCollection<TimelineTrack>();
            foreach (var track in source)
            {
                var newTrack = new TimelineTrack
                {
                    TrackName = track.TrackName,
                    IsPlaceholder = track.IsPlaceholder,
                    Effects = new ObservableCollection<TimelineEffectItem>()
                };
                foreach (var eff in track.Effects)
                {
                    newTrack.Effects.Add(new TimelineEffectItem
                    {
                        EffectName = eff.EffectName,
                        StartTime = eff.StartTime,
                        Duration = eff.Duration,
                        ConfigData = eff.ConfigData?.Clone()
                    });
                }
                copy.Add(newTrack);
            }
            return copy;
        }
    }
}
