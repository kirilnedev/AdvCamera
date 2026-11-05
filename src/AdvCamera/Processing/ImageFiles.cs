using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace AdvCamera.Processing;

public static class ImageFiles
{
    /// <summary>Saves a BGRA frame as PNG or JPEG. Works with any (Unicode) path.</summary>
    public static void Save(Mat bgraFrame, string path, bool jpeg)
    {
        using var bgr = new Mat();
        Cv2.CvtColor(bgraFrame, bgr, ColorConversionCodes.BGRA2BGR);

        byte[] data;
        if (jpeg)
            Cv2.ImEncode(".jpg", bgr, out data, new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
        else
            Cv2.ImEncode(".png", bgr, out data, new ImageEncodingParam(ImwriteFlags.PngCompression, 3));
        File.WriteAllBytes(path, data);
    }

    /// <summary>Copies a BGRA frame into a frozen WPF bitmap (e.g. for the clipboard).</summary>
    public static BitmapSource ToBitmapSource(Mat bgraFrame)
    {
        int stride = (int)bgraFrame.Step();
        var bmp = BitmapSource.Create(bgraFrame.Width, bgraFrame.Height, 96, 96, PixelFormats.Bgr32, null,
            bgraFrame.Data, stride * bgraFrame.Height, stride);
        bmp.Freeze();
        return bmp;
    }
}
