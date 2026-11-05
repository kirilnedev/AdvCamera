using OpenCvSharp;

namespace AdvCamera.Processing;

/// <summary>Immutable snapshot of the processing settings, handed to the capture thread.</summary>
public sealed record ProcessingParams(
    int RotationIndex, bool FlipH, bool FlipV,
    double Brightness, double Contrast, double Gamma, double Saturation, double Sharpness,
    double Temperature, double Tint, double Denoise, bool Grayscale, bool Invert)
{
    public static readonly ProcessingParams Default = new(0, false, false, 0, 0, 1, 100, 0, 0, 0, 0, false, false);
}

/// <summary>
/// Applies orientation and image adjustments to camera frames (BGRA, 8-bit).
/// Reuses its internal buffers between frames; not thread-safe (one per capture thread).
/// </summary>
public sealed class FrameProcessor : IDisposable
{
    private readonly Mat _rotated = new();
    private readonly Mat _flipped = new();
    private readonly Mat _accumulator = new();
    private readonly Mat _denoised = new();
    private readonly Mat _lutOut = new();
    private readonly Mat _gray = new();
    private readonly Mat _gray3 = new();
    private readonly Mat _saturated = new();
    private readonly Mat _blurred = new();
    private readonly Mat _sharpened = new();
    private readonly Mat _lut = new(1, 256, MatType.CV_8UC4);

    private (double, double, double, double, double, bool)? _lutKey;
    private bool _lutIsIdentity = true;

    /// <summary>Returns the processed frame. The result may be <paramref name="src"/> itself or an internal buffer.</summary>
    public Mat Process(Mat src, ProcessingParams p)
    {
        Mat cur = src;

        // 1. Orientation: rotate first, then mirror along the *screen* axes.
        switch (p.RotationIndex)
        {
            case 1: Cv2.Rotate(cur, _rotated, RotateFlags.Rotate90Clockwise); cur = _rotated; break;
            case 2: Cv2.Rotate(cur, _rotated, RotateFlags.Rotate180); cur = _rotated; break;
            case 3: Cv2.Rotate(cur, _rotated, RotateFlags.Rotate90Counterclockwise); cur = _rotated; break;
        }

        if (p.FlipH || p.FlipV)
        {
            var mode = p.FlipH && p.FlipV ? FlipMode.XY : p.FlipH ? FlipMode.Y : FlipMode.X;
            Cv2.Flip(cur, _flipped, mode);
            cur = _flipped;
        }

        // 2. Temporal noise reduction (running average of frames).
        if (p.Denoise > 0)
        {
            double alpha = 1.0 - Math.Clamp(p.Denoise, 0, 95) / 100.0;
            if (_accumulator.Empty() || _accumulator.Width != cur.Width || _accumulator.Height != cur.Height)
                cur.ConvertTo(_accumulator, MatType.CV_32FC4);
            else
                Cv2.AccumulateWeighted(cur, _accumulator, alpha, null!);
            _accumulator.ConvertTo(_denoised, MatType.CV_8UC4);
            cur = _denoised;
        }
        else if (!_accumulator.Empty())
        {
            _accumulator.Release();
        }

        // 3. Brightness / contrast / gamma / white balance / invert in one lookup table.
        EnsureLut(p);
        if (!_lutIsIdentity)
        {
            Cv2.LUT(cur, _lut, _lutOut);
            cur = _lutOut;
        }

        // 4. Saturation / grayscale.
        if (p.Grayscale || Math.Abs(p.Saturation - 100) > 0.5)
        {
            Cv2.CvtColor(cur, _gray, ColorConversionCodes.BGRA2GRAY);
            Cv2.CvtColor(_gray, _gray3, ColorConversionCodes.GRAY2BGRA);
            if (p.Grayscale)
            {
                cur = _gray3;
            }
            else
            {
                double s = p.Saturation / 100.0;
                Cv2.AddWeighted(cur, s, _gray3, 1.0 - s, 0, _saturated);
                cur = _saturated;
            }
        }

        // 5. Sharpening (unsharp mask).
        if (p.Sharpness > 0.5)
        {
            double amount = p.Sharpness / 100.0 * 1.5;
            double sigma = Math.Max(1.0, cur.Width / 1000.0);
            Cv2.GaussianBlur(cur, _blurred, new Size(0, 0), sigma);
            Cv2.AddWeighted(cur, 1 + amount, _blurred, -amount, 0, _sharpened);
            cur = _sharpened;
        }

        return cur;
    }

    private void EnsureLut(ProcessingParams p)
    {
        var key = (p.Brightness, p.Contrast, p.Gamma, p.Temperature, p.Tint, p.Invert);
        if (_lutKey == key) return;
        _lutKey = key;

        double c = p.Contrast / 100.0;
        double contrast = c >= 0 ? 1 + 3 * c : 1 + c;      // 0 .. 4
        double brightness = p.Brightness / 100.0 * 0.5;     // -0.5 .. +0.5
        double invGamma = 1.0 / Math.Max(0.05, p.Gamma);

        // Channel gains (B, G, R): temperature shifts blue <-> red/yellow, tint shifts green <-> magenta.
        double[] gains =
        [
            1 - p.Temperature * 0.004,
            1 - p.Tint * 0.004,
            1 + p.Temperature * 0.004,
        ];

        var table = new Vec4b[256];
        bool identity = true;
        Span<byte> v = stackalloc byte[3];
        for (int i = 0; i < 256; i++)
        {
            for (int ch = 0; ch < 3; ch++)
            {
                double x = i / 255.0 * gains[ch];
                x = Math.Pow(Math.Clamp(x, 0, 1), invGamma);
                x = (x - 0.5) * contrast + 0.5 + brightness;
                if (p.Invert) x = 1 - x;
                v[ch] = (byte)Math.Clamp(Math.Round(x * 255), 0, 255);
                if (v[ch] != i) identity = false;
            }
            table[i] = new Vec4b(v[0], v[1], v[2], 255);
        }

        _lut.SetArray(table);
        _lutIsIdentity = identity;
    }

    public void Dispose()
    {
        foreach (var m in new[] { _rotated, _flipped, _accumulator, _denoised, _lutOut, _gray, _gray3, _saturated, _blurred, _sharpened, _lut })
            m.Dispose();
    }
}
