using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AdvCamera.Camera;

/// <summary>A video capture device. <see cref="Index"/> matches OpenCV's DirectShow index.</summary>
public sealed record CameraDevice(int Index, string Name, string DevicePath)
{
    public override string ToString() => Name;
}

/// <summary>A resolution / pixel format the camera offers. Width 0 means "camera default".</summary>
public sealed record CameraFormat(int Width, int Height, string Fourcc, double Fps, bool IsFourcc)
{
    public static readonly CameraFormat Default = new(0, 0, "", 0, false);

    public override string ToString() => Width == 0
        ? "Camera default"
        : $"{Width} × {Height}   {Fourcc}   {Fps:0.#} fps";
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
/// A DirectShow handle to a camera, used for enumerating formats, reading/writing
/// hardware controls and showing the driver settings dialog. Frames are grabbed
/// separately by OpenCV (see <see cref="CaptureEngine"/>).
/// </summary>
public sealed class DsCamera : IDisposable
{
    private object? _filter;

    private DsCamera(object filter) => _filter = filter;

    // ---------- Enumeration ----------

    public static List<CameraDevice> EnumerateDevices()
    {
        var result = new List<CameraDevice>();
        var monikers = GetMonikers();
        try
        {
            for (int i = 0; i < monikers.Count; i++)
            {
                string name = ReadProperty(monikers[i], "FriendlyName") ?? $"Camera {i + 1}";
                string path = ReadProperty(monikers[i], "DevicePath") ?? name;
                result.Add(new CameraDevice(i, name, path));
            }
        }
        finally
        {
            monikers.ForEach(m => Marshal.ReleaseComObject(m));
        }
        return result;
    }

    public static DsCamera? Open(CameraDevice device)
    {
        var monikers = GetMonikers();
        try
        {
            if (device.Index >= monikers.Count) return null;
            var iid = typeof(IBaseFilter).GUID;
            monikers[device.Index].BindToObject(null!, null, ref iid, out object filter);
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

    // ---------- Formats ----------

    public List<CameraFormat> GetFormats()
    {
        var formats = new Dictionary<(int, int, string), CameraFormat>();
        var config = FindStreamConfig();
        if (config == null) return [];

        try
        {
            if (config.GetNumberOfCapabilities(out int count, out int size) != 0) return [];
            IntPtr caps = Marshal.AllocCoTaskMem(Math.Max(size, 512));
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (config.GetStreamCaps(i, out IntPtr pmt, caps) != 0 || pmt == IntPtr.Zero) continue;
                    try
                    {
                        var mt = Marshal.PtrToStructure<AMMediaType>(pmt);
                        if (mt.majortype != DsGuids.MEDIATYPE_Video || mt.pbFormat == IntPtr.Zero) continue;

                        long avgTime; int w, h;
                        if (mt.formattype == DsGuids.FORMAT_VideoInfo && mt.cbFormat >= 88)
                        {
                            avgTime = Marshal.ReadInt64(mt.pbFormat, 40);
                            w = Marshal.ReadInt32(mt.pbFormat, 52);
                            h = Marshal.ReadInt32(mt.pbFormat, 56);
                        }
                        else if (mt.formattype == DsGuids.FORMAT_VideoInfo2 && mt.cbFormat >= 112)
                        {
                            avgTime = Marshal.ReadInt64(mt.pbFormat, 40);
                            w = Marshal.ReadInt32(mt.pbFormat, 76);
                            h = Marshal.ReadInt32(mt.pbFormat, 80);
                        }
                        else continue;

                        h = Math.Abs(h);
                        if (w <= 0 || h <= 0) continue;
                        double fps = avgTime > 0 ? Math.Round(1e7 / avgTime, 1) : 0;
                        var (fourcc, isFourcc) = SubtypeName(mt.subtype);
                        var key = (w, h, fourcc);
                        if (!formats.TryGetValue(key, out var existing) || existing.Fps < fps)
                            formats[key] = new CameraFormat(w, h, fourcc, fps, isFourcc);
                    }
                    finally
                    {
                        NativeMethods.FreeMediaType(pmt);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(caps);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(config);
        }

        return formats.Values
            .OrderByDescending(f => f.Width * f.Height)
            .ThenBy(f => f.Fourcc == "MJPG" ? 0 : 1)
            .ThenBy(f => f.Fourcc)
            .ToList();
    }

    private IAMStreamConfig? FindStreamConfig()
    {
        if (_filter is not IBaseFilter filter || filter.EnumPins(out var enumPins) != 0 || enumPins == null)
            return null;

        IAMStreamConfig? fallback = null;
        try
        {
            var one = new IPin[1];
            while (enumPins.Next(1, one, IntPtr.Zero) == 0 && one[0] != null)
            {
                var pin = one[0];
                if (pin.QueryDirection(out int dir) == 0 && dir == 1 && pin is IAMStreamConfig config)
                {
                    if (GetPinCategory(pin) == DsGuids.PIN_CATEGORY_CAPTURE)
                    {
                        if (fallback != null) Marshal.ReleaseComObject(fallback);
                        return config;
                    }
                    if (fallback == null)
                    {
                        fallback = config;
                        continue;
                    }
                }
                Marshal.ReleaseComObject(pin);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumPins);
        }
        return fallback;
    }

    private static Guid GetPinCategory(IPin pin)
    {
        if (pin is not IKsPropertySet ks) return Guid.Empty;
        var set = DsGuids.AMPROPSETID_Pin;
        IntPtr buffer = Marshal.AllocCoTaskMem(16);
        try
        {
            if (ks.Get(ref set, 0, IntPtr.Zero, 0, buffer, 16, out int returned) == 0 && returned == 16)
                return Marshal.PtrToStructure<Guid>(buffer);
        }
        catch
        {
            // ignore
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
        return Guid.Empty;
    }

    private static (string Name, bool IsFourcc) SubtypeName(Guid subtype)
    {
        if (subtype.ToString().EndsWith("-0000-0010-8000-00aa00389b71", StringComparison.OrdinalIgnoreCase))
        {
            var b = subtype.ToByteArray();
            var chars = b.Take(4).Select(x => x >= 32 && x < 127 ? (char)x : '?').ToArray();
            return (new string(chars).Trim(), true);
        }
        if (subtype == DsGuids.MEDIASUBTYPE_RGB24) return ("RGB24", false);
        if (subtype == DsGuids.MEDIASUBTYPE_RGB32) return ("RGB32", false);
        if (subtype == DsGuids.MEDIASUBTYPE_ARGB32) return ("ARGB32", false);
        if (subtype == DsGuids.MEDIASUBTYPE_RGB555) return ("RGB555", false);
        if (subtype == DsGuids.MEDIASUBTYPE_RGB565) return ("RGB565", false);
        return ("Other", false);
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
