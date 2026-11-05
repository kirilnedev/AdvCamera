using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AdvCamera.Camera;

// Minimal DirectShow COM interop: just enough to find a video device, read/write its
// UVC controls and show the driver's property pages. (Video capture itself uses Media Foundation.)

internal static class DsGuids
{
    public static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    public static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
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
    // Only used as the interface id to bind the device; no methods are called.
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

internal static class NativeMethods
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int OleCreatePropertyFrame(IntPtr hwndOwner, int x, int y, string caption,
        int objectCount, [In] ref IntPtr objects, int pageCount, IntPtr pageClsids, int lcid, int reserved, IntPtr reserved2);
}
