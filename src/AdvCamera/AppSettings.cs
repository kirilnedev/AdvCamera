using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AdvCamera.Processing;

namespace AdvCamera;

/// <summary>
/// All user-adjustable settings. The UI binds to this object directly and it is
/// saved to %APPDATA%\AdvCamera\settings.json on exit.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    // ---- Orientation ----
    private int _rotationIndex;
    /// <summary>0 = 0°, 1 = 90° CW, 2 = 180°, 3 = 270° CW (90° CCW).</summary>
    public int RotationIndex { get => _rotationIndex; set => Set(ref _rotationIndex, ((value % 4) + 4) % 4); }

    private bool _flipH;
    public bool FlipH { get => _flipH; set => Set(ref _flipH, value); }

    private bool _flipV;
    public bool FlipV { get => _flipV; set => Set(ref _flipV, value); }

    // ---- Image adjustments (software, applied to every frame) ----
    private double _brightness;
    public double Brightness { get => _brightness; set => Set(ref _brightness, value); }

    private double _contrast;
    public double Contrast { get => _contrast; set => Set(ref _contrast, value); }

    private double _gamma = 1.0;
    public double Gamma { get => _gamma; set => Set(ref _gamma, value); }

    private double _saturation = 100;
    public double Saturation { get => _saturation; set => Set(ref _saturation, value); }

    private double _sharpness;
    public double Sharpness { get => _sharpness; set => Set(ref _sharpness, value); }

    private double _temperature;
    public double Temperature { get => _temperature; set => Set(ref _temperature, value); }

    private double _tint;
    public double Tint { get => _tint; set => Set(ref _tint, value); }

    private double _denoise;
    public double Denoise { get => _denoise; set => Set(ref _denoise, value); }

    private bool _grayscale;
    public bool Grayscale { get => _grayscale; set => Set(ref _grayscale, value); }

    private bool _invert;
    public bool Invert { get => _invert; set => Set(ref _invert, value); }

    // ---- Overlay ----
    private bool _crosshair;
    public bool Crosshair { get => _crosshair; set => Set(ref _crosshair, value); }

    private bool _grid;
    public bool Grid { get => _grid; set => Set(ref _grid, value); }

    private double _gridSpacing = 60;
    public double GridSpacing { get => _gridSpacing; set => Set(ref _gridSpacing, value); }

    private int _overlayColorIndex;
    public int OverlayColorIndex { get => _overlayColorIndex; set => Set(ref _overlayColorIndex, value); }

    // ---- Capture ----
    private string _captureFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AdvCamera");
    public string CaptureFolder { get => _captureFolder; set => Set(ref _captureFolder, value); }

    private int _snapshotFormatIndex;
    /// <summary>0 = PNG, 1 = JPEG.</summary>
    public int SnapshotFormatIndex { get => _snapshotFormatIndex; set => Set(ref _snapshotFormatIndex, value); }

    // ---- UI / window ----
    private bool _panelVisible = true;
    public bool PanelVisible { get => _panelVisible; set => Set(ref _panelVisible, value); }

    public double PanelWidth { get; set; } = 330;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }

    // ---- Last used camera ----
    public string? DevicePath { get; set; }
    public int FormatWidth { get; set; }
    public int FormatHeight { get; set; }
    public string? FormatFourcc { get; set; }

    public ProcessingParams ToParams() => new(
        RotationIndex, FlipH, FlipV,
        Brightness, Contrast, Gamma, Saturation, Sharpness,
        Temperature, Tint, Denoise, Grayscale, Invert);

    public void ResetOrientation()
    {
        RotationIndex = 0;
        FlipH = false;
        FlipV = false;
    }

    public void ResetImage()
    {
        Brightness = 0;
        Contrast = 0;
        Gamma = 1.0;
        Saturation = 100;
        Sharpness = 0;
        Temperature = 0;
        Tint = 0;
        Denoise = 0;
        Grayscale = false;
        Invert = false;
    }

    // ---- Persistence ----
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdvCamera", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or incompatible file: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Not critical.
        }
    }
}
