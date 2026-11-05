using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using AdvCamera.Processing;
using OpenCvSharp;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace AdvCamera.Camera;

public sealed record CaptureInfo(int Width, int Height, string Subtype, double Fps);

/// <summary>
/// Captures frames through Windows Media Foundation (the same camera stack as the Windows
/// Camera app), runs them through <see cref="FrameProcessor"/>, optionally records them, and
/// keeps the latest processed frame (BGRA) available for the UI.
/// </summary>
/// <remarks>
/// Media Foundation decodes the camera's MJPEG stream itself and offers the result as NV12
/// formats, so high resolutions (4K) run at full frame rate even over USB 2.0.
/// Events are raised on background threads.
/// </remarks>
public sealed class CaptureEngine : IDisposable
{
    private static readonly HashSet<string> CompressedSubtypes = ["MJPG", "H264", "H265", "HEVC"];

    private readonly object _frameLock = new();
    private readonly Mat _latest = new();
    private long _frameId;

    private readonly FrameProcessor _processor = new();
    private volatile ProcessingParams _params = ProcessingParams.Default;

    private MediaCapture? _capture;
    private MediaFrameSource? _source;
    private MediaFrameReader? _reader;
    private int _busy;

    private byte[]? _buffer;
    private GCHandle _bufferHandle;
    private readonly Mat _bgra = new();

    private readonly object _recordLock = new();
    private VideoWriter? _writer;
    private Size _writerSize;
    private string? _pendingRecordPath;
    private readonly Mat _recordBgr = new();
    private readonly Mat _recordResized = new();

    private double _measuredFps;
    private double _nominalFps;
    private int _fpsCount;
    private readonly Stopwatch _fpsWatch = new();

    /// <summary>The camera stopped working (e.g. it was unplugged).</summary>
    public event Action<string>? Failed;
    public event Action<string>? RecordingFailed;

    public double MeasuredFps => Volatile.Read(ref _measuredFps);

    public void UpdateParams(ProcessingParams p) => _params = p;

    // ---------------------------------------------------------------- devices

    public static async Task<List<CameraDevice>> ListDevicesAsync()
    {
        var all = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return all.Select((d, i) => new CameraDevice(i, d.Name, d.Id)).ToList();
    }

    /// <summary>Opens the camera (exclusively) and returns the formats it offers.</summary>
    public async Task<List<CameraFormat>> OpenAsync(CameraDevice device)
    {
        await CloseAsync().ConfigureAwait(false);

        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.DevicePath,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
        }
        catch (UnauthorizedAccessException)
        {
            capture.Dispose();
            throw new InvalidOperationException(
                "Windows is blocking access to the camera. Allow it in Settings → Privacy → Camera (\"Let desktop apps access your camera\").");
        }
        catch (Exception ex)
        {
            capture.Dispose();
            throw new InvalidOperationException(
                "Could not open the camera. It may be in use by another application.\n(" + ex.Message + ")");
        }

        capture.Failed += OnCaptureFailed;
        _capture = capture;
        _source = capture.FrameSources.Values
            .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
            .OrderBy(s => s.Info.MediaStreamType switch
            {
                MediaStreamType.VideoRecord => 0,
                MediaStreamType.VideoPreview => 1,
                _ => 2,
            })
            .FirstOrDefault();

        if (_source == null)
        {
            await CloseAsync().ConfigureAwait(false);
            throw new InvalidOperationException("This camera has no color video stream.");
        }

        return BuildFormatList(_source);
    }

    private static List<CameraFormat> BuildFormatList(MediaFrameSource source) =>
        source.SupportedFormats
            .Where(f => f.VideoFormat is { Width: > 0, Height: > 0 } && !CompressedSubtypes.Contains(f.Subtype.ToUpperInvariant()))
            .Select(f => new CameraFormat(
                (int)f.VideoFormat.Width,
                (int)f.VideoFormat.Height,
                f.Subtype.ToUpperInvariant(),
                FrameRate(f),
                f))
            .GroupBy(f => (f.Width, f.Height, f.Subtype))
            .Select(g => g.MaxBy(f => f.Fps)!)
            .OrderByDescending(f => f.Width * f.Height)
            .ThenBy(f => f.Subtype == "NV12" ? 0 : 1)
            .ThenBy(f => f.Subtype)
            .ToList();

    private static double FrameRate(MediaFrameFormat f) =>
        f.FrameRate is { Denominator: > 0 } r ? Math.Round((double)r.Numerator / r.Denominator, 1) : 0;

    // ---------------------------------------------------------------- streaming

    /// <summary>Starts streaming in the given format (null = keep the camera's current format).</summary>
    public async Task<CaptureInfo> StartAsync(CameraFormat? format)
    {
        await StopAsync().ConfigureAwait(false);
        var capture = _capture ?? throw new InvalidOperationException("No camera is open.");
        var source = _source!;

        if (format?.Native is MediaFrameFormat wanted)
        {
            await source.SetFormatAsync(wanted);
        }
        else if (source.CurrentFormat is { } current && CompressedSubtypes.Contains(current.Subtype.ToUpperInvariant()))
        {
            // The camera defaults to a compressed format we can't read: use the decoded equivalent.
            var alternative = BuildFormatList(source)
                .FirstOrDefault(f => f.Width == current.VideoFormat.Width && f.Height == current.VideoFormat.Height)
                ?? BuildFormatList(source).FirstOrDefault();
            if (alternative?.Native is MediaFrameFormat alt) await source.SetFormatAsync(alt);
        }

        var active = source.CurrentFormat;
        _nominalFps = FrameRate(active);

        // NV12 output is cheapest; fall back to BGRA if the pipeline can't produce it.
        MediaFrameReaderStartStatus status = MediaFrameReaderStartStatus.UnknownFailure;
        foreach (var outputSubtype in new[] { MediaEncodingSubtypes.Nv12, MediaEncodingSubtypes.Bgra8 })
        {
            var reader = await capture.CreateFrameReaderAsync(source, outputSubtype);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrameArrived;
            status = await reader.StartAsync();
            if (status == MediaFrameReaderStartStatus.Success)
            {
                _reader = reader;
                break;
            }
            reader.FrameArrived -= OnFrameArrived;
            reader.Dispose();
        }

        if (_reader == null)
            throw new InvalidOperationException($"The camera could not start streaming ({status}).");

        _fpsCount = 0;
        _fpsWatch.Restart();
        return new CaptureInfo((int)active.VideoFormat.Width, (int)active.VideoFormat.Height, active.Subtype.ToUpperInvariant(), _nominalFps);
    }

    public async Task StopAsync()
    {
        StopRecording();

        var reader = _reader;
        _reader = null;
        if (reader != null)
        {
            reader.FrameArrived -= OnFrameArrived;
            try { await reader.StopAsync(); } catch { /* camera may already be gone */ }

            // Let a frame that is being processed finish.
            var wait = Stopwatch.StartNew();
            while (Volatile.Read(ref _busy) == 1 && wait.ElapsedMilliseconds < 2000)
                await Task.Delay(5).ConfigureAwait(false);
            reader.Dispose();
        }
        Volatile.Write(ref _measuredFps, 0);
    }

    public async Task CloseAsync()
    {
        await StopAsync().ConfigureAwait(false);
        var capture = _capture;
        _capture = null;
        _source = null;
        if (capture != null)
        {
            capture.Failed -= OnCaptureFailed;
            try { capture.Dispose(); } catch { /* ignore */ }
        }
    }

    private void OnCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs e) =>
        Failed?.Invoke("The camera stopped working (was it disconnected?).\n" + e.Message);

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Realtime mode: if we are still busy with the previous frame, skip this one.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap == null || !ConvertToBgra(bitmap)) return;

            CountFrame();
            var output = _processor.Process(_bgra, _params);
            WriteRecording(output);

            lock (_frameLock)
            {
                output.CopyTo(_latest);
                _frameId++;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Frame error: " + ex);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Copies the bitmap into <see cref="_bgra"/>, converting from NV12 / YUY2 if needed.</summary>
    private bool ConvertToBgra(SoftwareBitmap bitmap)
    {
        var pixelFormat = bitmap.BitmapPixelFormat;
        if (pixelFormat is not (BitmapPixelFormat.Nv12 or BitmapPixelFormat.Bgra8 or BitmapPixelFormat.Yuy2))
        {
            using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
            return ConvertToBgra(converted);
        }

        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        int stride, uvStart = 0, uvStride = 0, size;
        using (var locked = bitmap.LockBuffer(BitmapBufferAccessMode.Read))
        {
            var y = locked.GetPlaneDescription(0);
            stride = y.Stride;
            size = y.StartIndex + stride * h;
            if (pixelFormat == BitmapPixelFormat.Nv12)
            {
                var uv = locked.GetPlaneDescription(1);
                uvStart = uv.StartIndex;
                uvStride = uv.Stride;
                size = Math.Max(size, uvStart + uvStride * (h / 2));
            }
        }

        if (_buffer == null || _buffer.Length != size)
        {
            if (_bufferHandle.IsAllocated) _bufferHandle.Free();
            _buffer = new byte[size];
            _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        }
        bitmap.CopyToBuffer(_buffer.AsBuffer());
        IntPtr ptr = _bufferHandle.AddrOfPinnedObject();

        switch (pixelFormat)
        {
            case BitmapPixelFormat.Nv12:
            {
                using var yPlane = Mat.FromPixelData(h, w, MatType.CV_8UC1, ptr, stride);
                using var uvPlane = Mat.FromPixelData(h / 2, w / 2, MatType.CV_8UC2, ptr + uvStart, uvStride);
                Cv2.CvtColorTwoPlane(yPlane, uvPlane, _bgra, ColorConversionCodes.YUV2BGRA_NV12);
                break;
            }
            case BitmapPixelFormat.Yuy2:
            {
                using var yuy2 = Mat.FromPixelData(h, w, MatType.CV_8UC2, ptr, stride);
                Cv2.CvtColor(yuy2, _bgra, ColorConversionCodes.YUV2BGRA_YUY2);
                break;
            }
            default:
            {
                using var bgra = Mat.FromPixelData(h, w, MatType.CV_8UC4, ptr, stride);
                bgra.CopyTo(_bgra);
                break;
            }
        }
        return true;
    }

    private void CountFrame()
    {
        _fpsCount++;
        long ms = _fpsWatch.ElapsedMilliseconds;
        if (ms >= 1000)
        {
            Volatile.Write(ref _measuredFps, _fpsCount * 1000.0 / ms);
            _fpsCount = 0;
            _fpsWatch.Restart();
        }
    }

    // ---------------------------------------------------------------- frames for the UI

    /// <summary>Calls <paramref name="consumer"/> with the newest frame (BGRA) if it differs from <paramref name="lastId"/>.</summary>
    public bool TryReadLatest(ref long lastId, Action<Mat> consumer)
    {
        lock (_frameLock)
        {
            if (_frameId == lastId || _latest.Empty()) return false;
            lastId = _frameId;
            consumer(_latest);
            return true;
        }
    }

    /// <summary>A copy of the newest frame (BGRA), or null.</summary>
    public Mat? CloneLatest()
    {
        lock (_frameLock)
            return _latest.Empty() ? null : _latest.Clone();
    }

    // ---------------------------------------------------------------- recording

    public void StartRecording(string path)
    {
        lock (_recordLock) _pendingRecordPath = path;
    }

    public void StopRecording()
    {
        lock (_recordLock)
        {
            _pendingRecordPath = null;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void WriteRecording(Mat bgraFrame)
    {
        lock (_recordLock)
        {
            if (_pendingRecordPath != null)
            {
                string path = _pendingRecordPath;
                _pendingRecordPath = null;
                _writer?.Dispose();
                double fps = MeasuredFps > 1 ? Math.Round(MeasuredFps, 1) : _nominalFps > 1 ? _nominalFps : 30;
                _writerSize = new Size(bgraFrame.Width, bgraFrame.Height);
                _writer = new VideoWriter(path, FourCC.MJPG, fps, _writerSize);
                if (!_writer.IsOpened())
                {
                    _writer.Dispose();
                    _writer = null;
                    RecordingFailed?.Invoke("Could not create the video file.");
                    return;
                }
            }
            if (_writer == null) return;

            Cv2.CvtColor(bgraFrame, _recordBgr, ColorConversionCodes.BGRA2BGR);
            if (_recordBgr.Width == _writerSize.Width && _recordBgr.Height == _writerSize.Height)
            {
                _writer.Write(_recordBgr);
            }
            else
            {
                // Orientation changed mid-recording: keep the file's frame size.
                Cv2.Resize(_recordBgr, _recordResized, _writerSize);
                _writer.Write(_recordResized);
            }
        }
    }

    public void Dispose()
    {
        try { Task.Run(CloseAsync).Wait(3000); } catch { /* shutting down */ }
        lock (_frameLock) _latest.Dispose();
        _processor.Dispose();
        _bgra.Dispose();
        _recordBgr.Dispose();
        _recordResized.Dispose();
        if (_bufferHandle.IsAllocated) _bufferHandle.Free();
    }
}
