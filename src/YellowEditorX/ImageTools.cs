using System.IO;
using System.Windows.Media.Imaging;

namespace YellowEditorX;

static class ImageTools
{
    /// <summary>Loads an image without keeping the file open, so it can be replaced while shown.</summary>
    public static BitmapSource? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return Decode(File.ReadAllBytes(path)); }
        catch { return null; }
    }

    static BitmapSource Decode(byte[] data)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(data);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    static readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly Dictionary<string, BitmapSource> webCache = new();

    /// <summary>The official image of a print (its image URL), kept in memory while the editor runs.</summary>
    public static async Task<BitmapSource?> LoadUrl(string url)
    {
        if (webCache.TryGetValue(url, out var hit)) return hit;
        try
        {
            var img = Decode(await http.GetByteArrayAsync(url));
            webCache[url] = img;
            return img;
        }
        catch { return null; }
    }

    /// <summary>Writes the image as it is (the card maker's PNG keeps its size).</summary>
    public static void SavePng(BitmapSource src, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
