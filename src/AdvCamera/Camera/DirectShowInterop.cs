using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AdvCamera.Camera;

// Minimal DirectShow COM interop: just enough to enumerate video devices, list their
// formats, read/write UVC controls and show the driver's property pages.

internal static class DsGuids
{
    public static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    public static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    public static readonly Guid MEDIATYPE_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid FORMAT_VideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");
    public static readonly Guid FORMAT_VideoInfo2 = new("f72a76A0-eb0a-11d0-ace4-0000c0cc16ba");
    public static readonly Guid AMPROPSETID_Pin = new("9b00f101-1567-11d1-b3f1-00aa003761c5");
    public static readonly Guid PIN_CATEGORY_CAPTURE = new("fb6c4281-0353-11d1-905f-0000c0cc16ba");

    public static readonly Guid MEDIASUBTYPE_RGB24 = new("e436eb7d-524f-11ce-9f53-0020af0ba770");
    public static readonly Guid MEDIASUBTYPE_RGB32 = new("e436eb7e-524f-11ce-9f53-0020af0ba770");
    public static readonly Guid MEDIASUBTYPE_RGB555 = new("e436eb7c-524f-11ce-9f53-0020af0ba770");
    public static readonly Guid MEDIASUBTYPE_RGB565 = new("e436eb7b-524f-11ce-9f53-0020af0ba770");
    public static readonly Guid MEDIASUBTYPE_ARGB32 = new("773c9ac0-3274-11d0-b724-00aa006c1a01");
}

[ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICreateDevEnum
{
    [PreserveSig]
    int CreateClassEnumerator([In] ref Guid clsidDeviceClass, out IEnumMoniker? enumMoniker, int flags);
}

[ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyBag
{
    [PreserveSig]
    int Read([MarshalAs(UnmanagedType.LPWStr)] string propName, [MarshalAs(UnmanagedType.Struct)] out object? value, IntPtr errorLog);

    [PreserveSig]
    int Write([MarshalAs(UnmanagedType.LPWStr)] string propName, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
}

[ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IBaseFilter
{
    // IPersist
    [PreserveSig] int GetClassID(out Guid classId);
    // IMediaFilter
    [PreserveSig] int Stop();
    [PreserveSig] int Pause();
    [PreserveSig] int Run(long start);
    [PreserveSig] int GetState(int msTimeout, out int state);
    [PreserveSig] int SetSyncSource(IntPtr clock);
    [PreserveSig] int GetSyncSource(out IntPtr clock);
    // IBaseFilter
    [PreserveSig] int EnumPins(out IEnumPins? enumPins);
}

[ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumPins
{
    [PreserveSig]
    int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IPin[] pins, IntPtr fetched);
}

[ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPin
{
    [PreserveSig] int Connect(IntPtr receivePin, IntPtr mediaType);
    [PreserveSig] int ReceiveConnection(IntPtr connector, IntPtr mediaType);
    [PreserveSig] int Disconnect();
    [PreserveSig] int ConnectedTo(out IntPtr pin);
    [PreserveSig] int ConnectionMediaType(IntPtr mediaType);
    [PreserveSig] int QueryPinInfo(IntPtr info);
    [PreserveSig] int QueryDirection(out int direction); // 0 = input, 1 = output
}

[ComImport, Guid("31EFAC30-515C-11d0-A9AA-00AA0061BE93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsPropertySet
{
    [PreserveSig]
    int Set([In] ref Guid propSet, int propId, IntPtr instanceData, int instanceLength, IntPtr propData, int dataLength);

    [PreserveSig]
    int Get([In] ref Guid propSet, int propId, IntPtr instanceData, int instanceLength, IntPtr propData, int dataLength, out int bytesReturned);
}

[ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAMStreamConfig
{
    [PreserveSig] int SetFormat(IntPtr mediaType);
    [PreserveSig] int GetFormat(out IntPtr mediaType);
    [PreserveSig] int GetNumberOfCapabilities(out int count, out int size);
    [PreserveSig] int GetStreamCaps(int index, out IntPtr mediaType, IntPtr streamConfigCaps);
}

[ComImport, Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAMVideoProcAmp
{
    [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int defaultValue, out int flags);
    [PreserveSig] int Set(int property, int value, int flags);
    [PreserveSig] int Get(int property, out int value, out int flags);
}

[ComImport, Guid("C6E13370-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAMCameraControl
{
    [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int defaultValue, out int flags);
    [PreserveSig] int Set(int property, int value, int flags);
    [PreserveSig] int Get(int property, out int value, out int flags);
}

[ComImport, Guid("B196B28B-BAB4-101A-B69C-00AA00341D07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpecifyPropertyPages
{
    [PreserveSig] int GetPages(out CAUUID pages);
}

[StructLayout(LayoutKind.Sequential)]
internal struct CAUUID
{
    public int cElems;
    public IntPtr pElems;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AMMediaType
{
    public Guid majortype;
    public Guid subtype;
    public int bFixedSizeSamples;
    public int bTemporalCompression;
    public int lSampleSize;
    public Guid formattype;
    public IntPtr pUnk;
    public int cbFormat;
    public IntPtr pbFormat;
}

internal static class NativeMethods
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int OleCreatePropertyFrame(IntPtr hwndOwner, int x, int y, string caption,
        int objectCount, [In] ref IntPtr objects, int pageCount, IntPtr pageClsids, int lcid, int reserved, IntPtr reserved2);

    public static void FreeMediaType(IntPtr pmt)
    {
        if (pmt == IntPtr.Zero) return;
        var mt = Marshal.PtrToStructure<AMMediaType>(pmt);
        if (mt.cbFormat != 0 && mt.pbFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.pbFormat);
        if (mt.pUnk != IntPtr.Zero) Marshal.Release(mt.pUnk);
        Marshal.FreeCoTaskMem(pmt);
    }
}
