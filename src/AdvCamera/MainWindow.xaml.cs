using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AdvCamera.Camera;
using AdvCamera.Processing;
using CvMat = OpenCvSharp.Mat;

namespace AdvCamera;

public partial class MainWindow : Window
{
    private static readonly (string Name, Color Color)[] OverlayColors =
    [
        ("Green", Color.FromRgb(0x30, 0xFF, 0x30)),
        ("Red", Color.FromRgb(0xFF, 0x30, 0x30)),
        ("Yellow", Color.FromRgb(0xFF, 0xE0, 0x20)),
        ("Cyan", Color.FromRgb(0x20, 0xE0, 0xFF)),
        ("Magenta", Color.FromRgb(0xFF, 0x40, 0xFF)),
        ("White", Colors.White),
        ("Black", Colors.Black),
    ];

    private readonly AppSettings _settings;
    private readonly CaptureEngine _engine = new();
    private readonly ObservableCollection<HardwareControlViewModel> _hardwareControls = [];
    private readonly DispatcherTimer _statusTimer;

    private DsCamera? _dsCamera;
    private CameraDevice? _currentDevice;
    private bool _suppressSelectionEvents;
    private int _openGeneration;

    private WriteableBitmap? _bitmap;
    private long _lastFrameId;

    private bool _frozen;
    private CvMat? _frozenFrame;

    private bool _recording;
    private DateTime _recordingStart;

    private double _zoom = 1;
    private bool _panning;
    private Point _panStartMouse;
    private Point _panStartOffset;

    private bool _fullscreen;
    private WindowState _preFullscreenState;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        DataContext = _settings;
        _settings.PropertyChanged += Settings_PropertyChanged;
        _engine.UpdateParams(_settings.ToParams());

        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;

        foreach (var (name, color) in OverlayColors)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal };
            item.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0) });
            item.Children.Add(new TextBlock { Text = name });
            OverlayColorCombo.Items.Add(new ComboBoxItem { Content = item });
        }
        _settings.OverlayColorIndex = Math.Clamp(_settings.OverlayColorIndex, 0, OverlayColors.Length - 1);
        OverlayColorCombo.SetBinding(Selector.SelectedIndexProperty, new System.Windows.Data.Binding(nameof(AppSettings.OverlayColorIndex)));

        HardwareList.ItemsSource = _hardwareControls;

        _engine.Opened += info => Dispatcher.BeginInvoke(() => OnCameraOpened(info));
        _engine.Failed += msg => Dispatcher.BeginInvoke(() => OnCameraFailed(msg));
        _engine.Stopped += () => Dispatcher.BeginInvoke(OnCameraStopped);
        _engine.RecordingFailed += msg => Dispatcher.BeginInvoke(() => { SetRecordingUi(false); ShowStatus(msg); });

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statusTimer.Tick += StatusTimer_Tick;

        ApplyPanelVisibility();
    }

    // =====================================================================
    // Window lifetime
    // =====================================================================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dark title bar (Windows 10 20H1+ / Windows 11).
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnRendering;
        _statusTimer.Start();
        RefreshCameraList(selectSaved: true);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _statusTimer.Stop();

        if (_settings.PanelVisible && PanelColumn.ActualWidth > 0)
            _settings.PanelWidth = PanelColumn.ActualWidth;
        if (!_fullscreen)
        {
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            }
        }
        _settings.Save();

        _engine.Dispose();
        _dsCamera?.Dispose();
        _frozenFrame?.Dispose();
    }

    // =====================================================================
    // Settings
    // =====================================================================

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _engine.UpdateParams(_settings.ToParams());

        switch (e.PropertyName)
        {
            case nameof(AppSettings.Crosshair):
            case nameof(AppSettings.Grid):
            case nameof(AppSettings.GridSpacing):
            case nameof(AppSettings.OverlayColorIndex):
                RedrawOverlay();
                break;
            case nameof(AppSettings.PanelVisible):
                ApplyPanelVisibility();
                break;
            case nameof(AppSettings.RotationIndex):
            case nameof(AppSettings.FlipH):
            case nameof(AppSettings.FlipV):
                ShowStatus(DescribeOrientation());
                break;
        }
    }

    private string DescribeOrientation()
    {
        var parts = new List<string> { $"Rotation {_settings.RotationIndex * 90}°" };
        if (_settings.FlipH) parts.Add("mirrored X");
        if (_settings.FlipV) parts.Add("mirrored Y");
        return string.Join(", ", parts);
    }

    private void ApplyPanelVisibility()
    {
        if (_settings.PanelVisible)
        {
            PanelColumn.MinWidth = 240;
            PanelColumn.MaxWidth = 700;
            PanelColumn.Width = new GridLength(Math.Clamp(_settings.PanelWidth, 240, 700));
            SidePanel.Visibility = Visibility.Visible;
            PanelSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (PanelColumn.ActualWidth > 0) _settings.PanelWidth = PanelColumn.ActualWidth;
            PanelColumn.MinWidth = 0;
            PanelColumn.Width = new GridLength(0);
            SidePanel.Visibility = Visibility.Collapsed;
            PanelSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void PanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (PanelColumn.ActualWidth > 0) _settings.PanelWidth = PanelColumn.ActualWidth;
    }

    // =====================================================================
    // Camera selection
    // =====================================================================

    private void RefreshCameraList(bool selectSaved)
    {
        List<CameraDevice> devices;
        try
        {
            devices = DsCamera.EnumerateDevices();
        }
        catch (Exception ex)
        {
            devices = [];
            ShowStatus("Could not list cameras: " + ex.Message);
        }

        string? wanted = selectSaved ? _settings.DevicePath : _currentDevice?.DevicePath;

        _suppressSelectionEvents = true;
        CameraCombo.ItemsSource = devices;
        var match = devices.FirstOrDefault(d => d.DevicePath == wanted) ?? devices.FirstOrDefault();
        CameraCombo.SelectedItem = match;
        _suppressSelectionEvents = false;

        if (match == null)
        {
            _ = CloseCameraAsync();
            CenterMessage.Text = "No camera found.\nConnect a camera and click the refresh button next to the camera list.";
            CenterMessage.Visibility = Visibility.Visible;
            return;
        }

        // Re-open only if the device actually changed (or nothing is open yet).
        if (_currentDevice == null || match.DevicePath != _currentDevice.DevicePath || match.Index != _currentDevice.Index)
            _ = OpenDeviceAsync(match);
    }

    private void RefreshCameras_Click(object sender, RoutedEventArgs e) => RefreshCameraList(selectSaved: false);

    private void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents) return;
        if (CameraCombo.SelectedItem is CameraDevice device)
            _ = OpenDeviceAsync(device);
    }

    private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || _currentDevice == null) return;
        if (FormatCombo.SelectedItem is not CameraFormat format) return;

        _settings.FormatWidth = format.Width;
        _settings.FormatHeight = format.Height;
        _settings.FormatFourcc = format.Fourcc;
        _ = StartCaptureAsync(_currentDevice, format);
    }

    private async Task CloseCameraAsync()
    {
        _openGeneration++;
        StopRecordingIfActive();
        await _engine.StopAsync();
        _dsCamera?.Dispose();
        _dsCamera = null;
        _currentDevice = null;
        _hardwareControls.Clear();
        FormatCombo.ItemsSource = null;
    }

    private async Task OpenDeviceAsync(CameraDevice device)
    {
        int generation = ++_openGeneration;
        StopRecordingIfActive();
        CenterMessage.Text = $"Opening {device.Name}…";
        CenterMessage.Visibility = Visibility.Visible;

        await _engine.StopAsync();
        if (generation != _openGeneration) return;

        _dsCamera?.Dispose();
        _dsCamera = DsCamera.Open(device);
        _currentDevice = device;
        Title = $"AdvCamera — {device.Name}";

        bool sameAsSaved = device.DevicePath == _settings.DevicePath;
        _settings.DevicePath = device.DevicePath;

        // Formats
        var formats = new List<CameraFormat> { CameraFormat.Default };
        try { formats.AddRange(_dsCamera?.GetFormats() ?? []); } catch { /* ignore */ }

        CameraFormat selected = CameraFormat.Default;
        if (sameAsSaved && _settings.FormatWidth > 0)
        {
            selected = formats.FirstOrDefault(f => f.Width == _settings.FormatWidth && f.Height == _settings.FormatHeight && f.Fourcc == _settings.FormatFourcc)
                       ?? formats.FirstOrDefault(f => f.Width == _settings.FormatWidth && f.Height == _settings.FormatHeight)
                       ?? CameraFormat.Default;
        }
        else
        {
            _settings.FormatWidth = 0;
            _settings.FormatHeight = 0;
            _settings.FormatFourcc = null;
        }

        _suppressSelectionEvents = true;
        FormatCombo.ItemsSource = formats;
        FormatCombo.SelectedItem = selected;
        _suppressSelectionEvents = false;

        // Hardware controls
        _hardwareControls.Clear();
        try
        {
            foreach (var control in _dsCamera?.GetControls() ?? [])
                _hardwareControls.Add(new HardwareControlViewModel(control));
        }
        catch { /* ignore */ }
        NoHardwareText.Visibility = _hardwareControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HardwareDefaultsButton.Visibility = _hardwareControls.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        await StartCaptureAsync(device, selected, generation);
    }

    private async Task StartCaptureAsync(CameraDevice device, CameraFormat format, int? generation = null)
    {
        int gen = generation ?? ++_openGeneration;
        StopRecordingIfActive();
        await _engine.StopAsync();
        if (gen != _openGeneration) return;

        CenterMessage.Text = $"Opening {device.Name}…";
        CenterMessage.Visibility = _bitmap == null ? Visibility.Visible : Visibility.Collapsed;
        ActualFormatText.Text = "";
        _engine.Start(device.Index, format.Width > 0 ? format : null);
    }

    private void OnCameraOpened(CaptureInfo info)
    {
        string fps = info.Fps > 0 ? $" @ {info.Fps:0.#} fps" : "";
        string fourcc = string.IsNullOrEmpty(info.Fourcc) ? "" : $" {info.Fourcc}";
        ActualFormatText.Text = $"Camera delivers {info.Width} × {info.Height}{fourcc}{fps}";
        CenterMessage.Visibility = Visibility.Collapsed;
        ShowStatus($"{_currentDevice?.Name}: {info.Width} × {info.Height}{fourcc}");
    }

    private void OnCameraFailed(string message)
    {
        CenterMessage.Text = message;
        CenterMessage.Visibility = Visibility.Visible;
        ShowStatus(message);
    }

    private void OnCameraStopped()
    {
        if (_recording) SetRecordingUi(false);
    }

    private void DriverSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_dsCamera == null || _currentDevice == null)
        {
            ShowStatus("No camera is open.");
            return;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!_dsCamera.ShowPropertyPages(hwnd, _currentDevice.Name))
            ShowStatus("This camera's driver has no settings dialog.");
        foreach (var c in _hardwareControls) c.Refresh();
    }

    private void HardwareDefaults_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _hardwareControls) c.RestoreDefault();
        ShowStatus("Camera hardware controls restored to defaults.");
    }

    // =====================================================================
    // Frame display
    // =====================================================================

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_frozen) return;
        _engine.TryReadLatest(ref _lastFrameId, ShowFrame);
    }

    private void ShowFrame(CvMat frame)
    {
        int w = frame.Width, h = frame.Height;
        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
        {
            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
            Preview.Source = _bitmap;
            OutputSizeText.Text = $"{w} × {h}";
            UpdateScalingMode();
        }
        int stride = (int)frame.Step();
        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), frame.Data, stride * h, stride);
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        double fps = _engine.MeasuredFps;
        FpsText.Text = fps > 0 ? $"{fps:0.0} fps" : "";

        if (_recording)
        {
            var t = DateTime.Now - _recordingStart;
            RecText.Text = $"REC {(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }

        // Keep read-outs of auto-controlled hardware values (e.g. auto exposure) current.
        foreach (var c in _hardwareControls)
            if (c.IsAuto) c.Refresh();
    }

    // =====================================================================
    // Orientation / adjustments buttons
    // =====================================================================

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => _settings.RotationIndex--;
    private void RotateRight_Click(object sender, RoutedEventArgs e) => _settings.RotationIndex++;
    private void ResetOrientation_Click(object sender, RoutedEventArgs e) => _settings.ResetOrientation();
    private void ResetImage_Click(object sender, RoutedEventArgs e) => _settings.ResetImage();

    // =====================================================================
    // Freeze / snapshot / copy / record
    // =====================================================================

    private void Freeze_Click(object sender, RoutedEventArgs e) => SetFrozen(FreezeButton.IsChecked == true);

    private void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        FreezeButton.IsChecked = frozen;
        FrozenBadge.Visibility = frozen ? Visibility.Visible : Visibility.Collapsed;
        _frozenFrame?.Dispose();
        _frozenFrame = frozen ? _engine.CloneLatest() : null;
        if (frozen && _frozenFrame != null) ShowFrame(_frozenFrame);
    }

    /// <summary>The frame the user is looking at (frozen frame or latest live frame). Caller disposes.</summary>
    private CvMat? GetCurrentFrame() => _frozen ? _frozenFrame?.Clone() : _engine.CloneLatest();

    private void Snapshot_Click(object sender, RoutedEventArgs e) => TakeSnapshot();

    private void TakeSnapshot()
    {
        using var frame = GetCurrentFrame();
        if (frame == null)
        {
            ShowStatus("No image to save.");
            return;
        }
        try
        {
            Directory.CreateDirectory(_settings.CaptureFolder);
            bool jpeg = _settings.SnapshotFormatIndex == 1;
            string path = Path.Combine(_settings.CaptureFolder, $"IMG_{DateTime.Now:yyyyMMdd_HHmmss_fff}{(jpeg ? ".jpg" : ".png")}");
            ImageFiles.Save(frame, path, jpeg);
            Flash();
            ShowStatus($"Saved {path}");
        }
        catch (Exception ex)
        {
            ShowStatus("Snapshot failed: " + ex.Message);
        }
    }

    private void Flash()
    {
        var anim = new DoubleAnimation(0.55, 0, TimeSpan.FromMilliseconds(300));
        FlashOverlay.BeginAnimation(OpacityProperty, anim);
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();

    private void CopyToClipboard()
    {
        using var frame = GetCurrentFrame();
        if (frame == null) return;
        try
        {
            Clipboard.SetImage(ImageFiles.ToBitmapSource(frame));
            ShowStatus("Image copied to clipboard.");
        }
        catch (Exception ex)
        {
            ShowStatus("Copy failed: " + ex.Message);
        }
    }

    private void Record_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    private void ToggleRecording()
    {
        if (_recording)
        {
            _engine.StopRecording();
            SetRecordingUi(false);
            ShowStatus("Recording stopped.");
            return;
        }

        if (_currentDevice == null || _engine.MeasuredFps <= 0)
        {
            SetRecordingUi(false);
            ShowStatus("The camera is not running.");
            return;
        }
        try
        {
            Directory.CreateDirectory(_settings.CaptureFolder);
            string path = Path.Combine(_settings.CaptureFolder, $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.avi");
            _engine.StartRecording(path);
            _recordingStart = DateTime.Now;
            SetRecordingUi(true);
            ShowStatus($"Recording to {path}");
        }
        catch (Exception ex)
        {
            SetRecordingUi(false);
            ShowStatus("Could not start recording: " + ex.Message);
        }
    }

    private void StopRecordingIfActive()
    {
        if (!_recording) return;
        _engine.StopRecording();
        SetRecordingUi(false);
    }

    private void SetRecordingUi(bool recording)
    {
        _recording = recording;
        RecordButton.IsChecked = recording;
        RecordLabel.Text = recording ? "Stop" : "Record";
        RecBadge.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        RecText.Text = "REC 00:00";
    }

    // =====================================================================
    // Capture folder
    // =====================================================================

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to save snapshots and videos" };
        if (Directory.Exists(_settings.CaptureFolder)) dialog.InitialDirectory = _settings.CaptureFolder;
        if (dialog.ShowDialog(this) == true) _settings.CaptureFolder = dialog.FolderName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settings.CaptureFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.CaptureFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus("Could not open folder: " + ex.Message);
        }
    }

    // =====================================================================
    // Zoom & pan
    // =====================================================================

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(Viewport), e.Delta > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    private void ZoomAt(Point p, double factor)
    {
        double newZoom = Math.Clamp(_zoom * factor, 1, 40);
        factor = newZoom / _zoom;
        if (Math.Abs(factor - 1) < 1e-9) return;

        ZoomTranslate.X = p.X - (p.X - ZoomTranslate.X) * factor;
        ZoomTranslate.Y = p.Y - (p.Y - ZoomTranslate.Y) * factor;
        _zoom = newZoom;
        ZoomScale.ScaleX = ZoomScale.ScaleY = _zoom;
        ClampPan();
        UpdateZoomUi();
    }

    private void ZoomCenter(double factor) =>
        ZoomAt(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), factor);

    private void ResetZoom()
    {
        _zoom = 1;
        ZoomScale.ScaleX = ZoomScale.ScaleY = 1;
        ZoomTranslate.X = ZoomTranslate.Y = 0;
        UpdateZoomUi();
    }

    private void ClampPan()
    {
        double w = Viewport.ActualWidth, h = Viewport.ActualHeight;
        ZoomTranslate.X = Math.Clamp(ZoomTranslate.X, w * (1 - _zoom), 0);
        ZoomTranslate.Y = Math.Clamp(ZoomTranslate.Y, h * (1 - _zoom), 0);
    }

    private void UpdateZoomUi()
    {
        ZoomBadge.Visibility = _zoom > 1.001 ? Visibility.Visible : Visibility.Collapsed;
        ZoomBadgeText.Text = $"Zoom {_zoom * 100:0}%";
        Viewport.Cursor = _zoom > 1.001 ? Cursors.SizeAll : null;
        UpdateScalingMode();
    }

    /// <summary>Show crisp pixels when zoomed far in, smooth scaling otherwise.</summary>
    private void UpdateScalingMode()
    {
        double pixelScale = _bitmap != null && Preview.ActualWidth > 0 ? Preview.ActualWidth / _bitmap.PixelWidth * _zoom : _zoom;
        RenderOptions.SetBitmapScalingMode(Preview, pixelScale >= 3 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => ResetZoom();

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetZoom();
            return;
        }
        if (_zoom <= 1.001) return;
        _panning = true;
        _panStartMouse = e.GetPosition(Viewport);
        _panStartOffset = new Point(ZoomTranslate.X, ZoomTranslate.Y);
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(Viewport);
        ZoomTranslate.X = _panStartOffset.X + (p.X - _panStartMouse.X);
        ZoomTranslate.Y = _panStartOffset.Y + (p.Y - _panStartMouse.Y);
        ClampPan();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClampPan();
        RedrawOverlay();
        UpdateScalingMode();
    }

    // =====================================================================
    // Overlay
    // =====================================================================

    private void RedrawOverlay()
    {
        Overlay.Children.Clear();
        if (!_settings.Crosshair && !_settings.Grid) return;

        double w = Viewport.ActualWidth, h = Viewport.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double cx = Math.Round(w / 2) + 0.5, cy = Math.Round(h / 2) + 0.5;
        var color = OverlayColors[Math.Clamp(_settings.OverlayColorIndex, 0, OverlayColors.Length - 1)].Color;

        if (_settings.Grid)
        {
            double s = Math.Max(5, _settings.GridSpacing);
            var grid = new GeometryGroup();
            for (double x = cx % s; x < w; x += s) grid.Children.Add(new LineGeometry(new Point(x, 0), new Point(x, h)));
            for (double y = cy % s; y < h; y += s) grid.Children.Add(new LineGeometry(new Point(0, y), new Point(w, y)));
            grid.Freeze();
            Overlay.Children.Add(new System.Windows.Shapes.Path
            {
                Data = grid,
                Stroke = new SolidColorBrush(color) { Opacity = 0.4 },
                StrokeThickness = 1,
                SnapsToDevicePixels = true,
            });
        }

        if (_settings.Crosshair)
        {
            double r = Math.Min(w, h) * 0.06;
            var cross = new GeometryGroup();
            cross.Children.Add(new LineGeometry(new Point(0, cy), new Point(w, cy)));
            cross.Children.Add(new LineGeometry(new Point(cx, 0), new Point(cx, h)));
            cross.Children.Add(new EllipseGeometry(new Point(cx, cy), r, r));
            cross.Freeze();
            Overlay.Children.Add(new System.Windows.Shapes.Path
            {
                Data = cross,
                Stroke = new SolidColorBrush(color) { Opacity = 0.9 },
                StrokeThickness = 1,
                SnapsToDevicePixels = true,
            });
        }
    }

    // =====================================================================
    // Full screen
    // =====================================================================

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _preFullscreenState = WindowState;
            WindowStyle = WindowStyle.None;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; // re-maximize so it covers the taskbar
            WindowState = WindowState.Maximized;
            ToolbarHost.Visibility = Visibility.Collapsed;
            StatusBarHost.Visibility = Visibility.Collapsed;
            _fullscreen = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _preFullscreenState;
            ToolbarHost.Visibility = Visibility.Visible;
            StatusBarHost.Visibility = Visibility.Visible;
            _fullscreen = false;
        }
    }

    // =====================================================================
    // Keyboard
    // =====================================================================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.R when ctrl: ToggleRecording(); break;
            case Key.R: _settings.RotationIndex += shift ? -1 : 1; break;
            case Key.H: _settings.FlipH = !_settings.FlipH; break;
            case Key.V when !ctrl: _settings.FlipV = !_settings.FlipV; break;
            case Key.C when ctrl: CopyToClipboard(); break;
            case Key.C: _settings.Crosshair = !_settings.Crosshair; break;
            case Key.G: _settings.Grid = !_settings.Grid; break;
            case Key.S:
            case Key.F12: TakeSnapshot(); break;
            case Key.Space: SetFrozen(!_frozen); break;
            case Key.P: _settings.PanelVisible = !_settings.PanelVisible; break;
            case Key.F11: ToggleFullscreen(); break;
            case Key.Escape when _fullscreen: ToggleFullscreen(); break;
            case Key.D0:
            case Key.NumPad0: ResetZoom(); break;
            case Key.OemPlus:
            case Key.Add: ZoomCenter(1.25); break;
            case Key.OemMinus:
            case Key.Subtract: ZoomCenter(1 / 1.25); break;
            default: return;
        }
        e.Handled = true;
    }

    // =====================================================================

    private void ShowStatus(string text) => StatusText.Text = text;
}
