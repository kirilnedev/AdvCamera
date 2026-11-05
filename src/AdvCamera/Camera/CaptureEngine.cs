using System.Diagnostics;
using AdvCamera.Processing;
using OpenCvSharp;

namespace AdvCamera.Camera;

public sealed record CaptureInfo(int Width, int Height, string Fourcc, double Fps);

/// <summary>
/// Grabs frames from a camera on a background thread (OpenCV / DirectShow backend),
/// runs them through <see cref="FrameProcessor"/>, optionally records them, and keeps
/// the latest processed frame available for the UI.
/// Events are raised on the capture thread.
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    private readonly object _frameLock = new();
    private readonly Mat _latest = new();
    private long _frameId;

    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile ProcessingParams _params = ProcessingParams.Default;
    private string? _pendingRecordPath;
    private volatile bool _stopRecordRequested;
    private double _measuredFps;

    public event Action<CaptureInfo>? Opened;
    public event Action<string>? Failed;
    public event Action? Stopped;
    public event Action<string>? RecordingFailed;

    public double MeasuredFps => Volatile.Read(ref _measuredFps);

    public void UpdateParams(ProcessingParams p) => _params = p;

    public void Start(int deviceIndex, CameraFormat? format)
    {
        Stop();
        _stopRequested = false;
        _stopRecordRequested = false;
        Volatile.Write(ref _measuredFps, 0);
        _thread = new Thread(() => Run(deviceIndex, format)) { IsBackground = true, Name = "Camera capture" };
        _thread.Start();
    }

    public void Stop()
    {
        var t = _thread;
        if (t == null) return;
        _stopRequested = true;
        t.Join(5000);
        _thread = null;
    }

    public Task StopAsync() => Task.Run(Stop);

    public void StartRecording(string path) => Volatile.Write(ref _pendingRecordPath, path);

    public void StopRecording() => _stopRecordRequested = true;

    /// <summary>Calls <paramref name="consumer"/> with the newest frame if it differs from <paramref name="lastId"/>.</summary>
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

    public Mat? CloneLatest()
    {
        lock (_frameLock)
            return _latest.Empty() ? null : _latest.Clone();
    }

    private void Run(int deviceIndex, CameraFormat? format)
    {
        VideoCapture? cap = null;
        VideoWriter? writer = null;
        var writerSize = new Size();
        using var frame = new Mat();
        using var bgr = new Mat();
        using var recBuffer = new Mat();
        using var processor = new FrameProcessor();

        try
        {
            cap = new VideoCapture();
            if (!cap.Open(deviceIndex, VideoCaptureAPIs.DSHOW) || !cap.IsOpened())
            {
                Failed?.Invoke("Could not open the camera. It may be in use by another application.");
                return;
            }

            if (format is { Width: > 0 })
            {
                if (format.IsFourcc && format.Fourcc.Length == 4)
                    cap.Set(VideoCaptureProperties.FourCC,
                        VideoWriter.FourCC(format.Fourcc[0], format.Fourcc[1], format.Fourcc[2], format.Fourcc[3]));
                cap.Set(VideoCaptureProperties.FrameWidth, format.Width);
                cap.Set(VideoCaptureProperties.FrameHeight, format.Height);
                if (format.Fps > 0) cap.Set(VideoCaptureProperties.Fps, format.Fps);
            }

            var info = new CaptureInfo(
                (int)cap.Get(VideoCaptureProperties.FrameWidth),
                (int)cap.Get(VideoCaptureProperties.FrameHeight),
                FourccToString((int)cap.Get(VideoCaptureProperties.FourCC)),
                cap.Get(VideoCaptureProperties.Fps));
            Opened?.Invoke(info);

            var sinceLastFrame = Stopwatch.StartNew();
            var fpsWatch = Stopwatch.StartNew();
            int fpsCount = 0;

            while (!_stopRequested)
            {
                if (!cap.Read(frame) || frame.Empty())
                {
                    if (sinceLastFrame.Elapsed.TotalSeconds > 5)
                    {
                        Failed?.Invoke("The camera stopped sending images (was it disconnected?).");
                        break;
                    }
                    Thread.Sleep(15);
                    continue;
                }
                sinceLastFrame.Restart();

                fpsCount++;
                if (fpsWatch.ElapsedMilliseconds >= 1000)
                {
                    Volatile.Write(ref _measuredFps, fpsCount * 1000.0 / fpsWatch.ElapsedMilliseconds);
                    fpsCount = 0;
                    fpsWatch.Restart();
                }

                Mat src = frame;
                if (frame.Channels() == 1) { Cv2.CvtColor(frame, bgr, ColorConversionCodes.GRAY2BGR); src = bgr; }
                else if (frame.Channels() == 4) { Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR); src = bgr; }

                Mat output = processor.Process(src, _params);

                // ---- Recording ----
                if (_stopRecordRequested)
                {
                    _stopRecordRequested = false;
                    writer?.Dispose();
                    writer = null;
                }
                var recordPath = Interlocked.Exchange(ref _pendingRecordPath, null);
                if (recordPath != null)
                {
                    writer?.Dispose();
                    double fps = MeasuredFps > 1 ? Math.Round(MeasuredFps, 1) : info.Fps > 1 ? info.Fps : 30;
                    writerSize = new Size(output.Width, output.Height);
                    writer = new VideoWriter(recordPath, FourCC.MJPG, fps, writerSize);
                    if (!writer.IsOpened())
                    {
                        writer.Dispose();
                        writer = null;
                        RecordingFailed?.Invoke("Could not create the video file.");
                    }
                }
                if (writer != null)
                {
                    if (output.Width == writerSize.Width && output.Height == writerSize.Height)
                        writer.Write(output);
                    else
                    {
                        // Orientation changed mid-recording: keep the file's frame size.
                        Cv2.Resize(output, recBuffer, writerSize);
                        writer.Write(recBuffer);
                    }
                }

                lock (_frameLock)
                {
                    output.CopyTo(_latest);
                    _frameId++;
                }
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke("Camera error: " + ex.Message);
        }
        finally
        {
            writer?.Dispose();
            cap?.Release();
            cap?.Dispose();
            Volatile.Write(ref _measuredFps, 0);
            Stopped?.Invoke();
        }
    }

    private static string FourccToString(int fourcc)
    {
        if (fourcc <= 0) return "";
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            int c = (fourcc >> (8 * i)) & 0xFF;
            chars[i] = c >= 32 && c < 127 ? (char)c : ' ';
        }
        return new string(chars).Trim();
    }

    public void Dispose()
    {
        Stop();
        _latest.Dispose();
    }
}
