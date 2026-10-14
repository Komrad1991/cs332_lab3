using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace FloodFillAvalonia;

public sealed class PatternImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    private PatternImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static async Task<PatternImage> LoadAsync(Stream source)
    {
        await using var copy = new MemoryStream();
        await source.CopyToAsync(copy);
        copy.Position = 0;
        using var decoded = WriteableBitmap.Decode(copy);
        var normalized = new WriteableBitmap(
            decoded.PixelSize,
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Opaque);

        using (var targetBuffer = normalized.Lock())
        {
            decoded.CopyPixels(targetBuffer);
        }

        using var locked = normalized.Lock();
        var length = locked.RowBytes * locked.Size.Height;
        var pixels = new byte[length];
        Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
        return new PatternImage(locked.Size.Width, locked.Size.Height, pixels);
    }
}
