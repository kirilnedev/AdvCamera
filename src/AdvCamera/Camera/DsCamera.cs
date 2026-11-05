using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AdvCamera.Camera;

/// <summary>A video capture device. <see cref="DevicePath"/> is the Windows device id.</summary>
public sealed record CameraDevice(int Index, string Name, string DevicePath)
{
    public override string ToString() => Name;

    /// <summary>
    /// The part of a device path that identifies the physical device, the same for Media Foundation
    /// and DirectShow paths, e.g. <c>\\?\usb#vid_1d6b&amp;pid_0102&amp;mi_02#6&amp;13142298&amp;1&amp;0002</c>.
    /// </summary>
    public static string Key(string? devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return "";
        string p = devicePath.ToLowerInvariant();
        int i = p.IndexOf("#{", StringComparison.Ordinal);
        return i > 0 ? p[..i] : p;
    }
}

/// <summary>A resolution / pixel format the camera offers. Width 0 means "camera default".</summary>
public sealed record CameraFormat(int Width, int Height, string Subtype, double Fps, object? Native = null)
{
    public static readonly CameraFormat Default = new(0, 0, "", 0);

    public override string ToString()
    {
        if (Width == 0) return "Camera default";
        string text = $"{Width} × {Height}  @ {Fps:0.#} fps";
        return Subtype == "NV12" ? text : $"{text}  ({Subtype})";
    }
}

/// <summary>A UVC control (brightness, exposure, focus, …) exposed by the camera driver.</summary>
public sealed class HardwareControl
{
    private readonly object _iface;

    internal HardwareControl(object iface, int property, string name, int min, int max, int step, int def, bool canAuto)
    {
        _iface = iface;
        Property = property;
        Name = name;
        Min = min;
        Max = max;
        Step = Math.Max(1, step);
        Default = def;
        CanAuto = canAuto;
    }

    public int Property { get; }
    public string Name { get; }
    public int Min { get; }
    public int Max { get; }
    public int Step { get; }
    public int Default { get; }
    public bool CanAuto { get; }

    public bool TryGet(out int value, out bool auto)
    {
        int hr, flags;
        try
        {
            hr = _iface is IAMVideoProcAmp v
                ? v.Get(Property, out value, out flags)
                : ((IAMCameraControl)_iface).Get(Property, out value, out flags);
        }
        catch
        {
            value = 0; auto = false; return false;
        }
        auto = (flags & 1) != 0;
        return hr == 0;
    }

    public bool TrySet(int value, bool auto)
    {
        int flags = auto ? 1 : 2;
        try
        {
            return (_iface is IAMVideoProcAmp v
                ? v.Set(Property, value, flags)
                : ((IAMCameraControl)_iface).Set(Property, value, flags)) == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// A DirectShow handle to a camera, used only for its hardware controls (exposure, focus, …)
/// and the driver settings dialog. Video itself is captured through Media Foundation
/// (see <see cref="CaptureEngine"/>); this handle never streams.
/// </summary>
public sealed class DsCamera : IDisposable
{
    private object? _filter;

    private DsCamera(object filter) => _filter = filter;

    /// <summary>Opens the DirectShow device that matches the given (Media Foundation) device.</summary>
    public static DsCamera? Open(CameraDevice device)
    {
        var monikers = GetMonikers();
        try
        {
            string key = CameraDevice.Key(device.DevicePath);
            var match = monikers.FirstOrDefault(m => CameraDevice.Key(ReadProperty(m, "DevicePath")) == key)
                        ?? monikers.FirstOrDefault(m => ReadProperty(m, "FriendlyName") == device.Name);
            if (match == null) return null;

            var iid = typeof(IBaseFilter).GUID;
            match.BindToObject(null!, null, ref iid, out object filter);
            return new DsCamera(filter);
        }
        catch
        {
            return null;
        }
        finally
        {
            monikers.ForEach(m => Marshal.ReleaseComObject(m));
        }
    }

    private static List<IMoniker> GetMonikers()
    {
        var list = new List<IMoniker>();
        var type = Type.GetTypeFromCLSID(DsGuids.CLSID_SystemDeviceEnum, true)!;
        var devEnum = (ICreateDevEnum)Activator.CreateInstance(type)!;
        try
        {
            var category = DsGuids.CLSID_VideoInputDeviceCategory;
            if (devEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0) != 0 || enumMoniker == null)
                return list;
            var one = new IMoniker[1];
            while (enumMoniker.Next(1, one, IntPtr.Zero) == 0 && one[0] != null)
                list.Add(one[0]);
            Marshal.ReleaseComObject(enumMoniker);
        }
        finally
        {
            Marshal.ReleaseComObject(devEnum);
        }
        return list;
    }

    private static string? ReadProperty(IMoniker moniker, string name)
    {
        try
        {
            var iid = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(null!, null, ref iid, out object bagObj);
            var bag = (IPropertyBag)bagObj;
            try
            {
                return bag.Read(name, out object? value, IntPtr.Zero) == 0 ? value as string : null;
            }
            finally
            {
                Marshal.ReleaseComObject(bag);
            }
        }
        catch
        {
            return null;
        }
    }

    // ---------- Hardware controls ----------

    private static readonly string[] ProcAmpNames =
        ["Brightness", "Contrast", "Hue", "Saturation", "Sharpness", "Gamma", "Color enable", "White balance", "Backlight compensation", "Gain"];

    private static readonly string[] CameraControlNames =
        ["Pan", "Tilt", "Roll", "Zoom", "Exposure", "Iris", "Focus"];

    public List<HardwareControl> GetControls()
    {
        var list = new List<HardwareControl>();
        try
        {
            if (_filter is IAMCameraControl cc)
            {
                // Exposure and focus first: most relevant for a microscope.
                foreach (int p in new[] { 4, 6, 3, 5, 0, 1, 2 })
                    if (cc.GetRange(p, out int min, out int max, out int step, out int def, out int flags) == 0 && max > min)
                        list.Add(new HardwareControl(cc, p, CameraControlNames[p], min, max, step, def, (flags & 1) != 0));
            }
        }
        catch
        {
            // driver doesn't support it
        }
        try
        {
            if (_filter is IAMVideoProcAmp pa)
            {
                for (int p = 0; p < ProcAmpNames.Length; p++)
                    if (pa.GetRange(p, out int min, out int max, out int step, out int def, out int flags) == 0 && max > min)
                        list.Add(new HardwareControl(pa, p, ProcAmpNames[p], min, max, step, def, (flags & 1) != 0));
            }
        }
        catch
        {
            // driver doesn't support it
        }
        return list;
    }

    // ---------- Driver dialog ----------

    /// <summary>Shows the driver's own property sheet (modal). Returns false if the driver has none.</summary>
    public bool ShowPropertyPages(IntPtr owner, string caption)
    {
        if (_filter is not ISpecifyPropertyPages spp || spp.GetPages(out CAUUID pages) != 0) return false;
        try
        {
            if (pages.cElems == 0) return false;
            IntPtr unk = Marshal.GetIUnknownForObject(_filter);
            try
            {
                NativeMethods.OleCreatePropertyFrame(owner, 0, 0, caption, 1, ref unk, pages.cElems, pages.pElems, 0, 0, IntPtr.Zero);
            }
            finally
            {
                Marshal.Release(unk);
            }
            return true;
        }
        finally
        {
            if (pages.pElems != IntPtr.Zero) Marshal.FreeCoTaskMem(pages.pElems);
        }
    }

    public void Dispose()
    {
        if (_filter != null)
        {
            Marshal.ReleaseComObject(_filter);
            _filter = null;
        }
    }
}
