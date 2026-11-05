using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace AdvCamera.Processing;

public static class ImageFiles
{
    /// <summary>Saves a BGR frame as PNG or JPEG. Works with any (Unicode) path.</summary>
    public static void Save(Mat frame, string path, bool jpeg)
    {
        byte[] data;
        if (jpeg)
            Cv2.ImEncode(".jpg", frame, out data, new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
        else
            Cv2.ImEncode(".png", frame, out data, new ImageEncodingParam(ImwriteFlags.PngCompression, 3));
        File.WriteAllBytes(path, data);
    }

    /// <summary>Copies a BGR frame into a frozen WPF bitmap (e.g. for the clipboard).</summary>
    public static BitmapSource ToBitmapSource(Mat frame)
    {
        int stride = (int)frame.Step();
        var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null,
            frame.Data, stride * frame.Height, stride);
        bmp.Freeze();
        return bmp;
    }
}
