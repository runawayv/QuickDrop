using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace QuickDrop.Services;

public static class QrService
{
    public static BitmapImage Generate(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(
            8,
            new byte[] { 20, 22, 28, 255 },
            new byte[] { 255, 255, 255, 255 });

        var image = new BitmapImage();
        using var ms = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}