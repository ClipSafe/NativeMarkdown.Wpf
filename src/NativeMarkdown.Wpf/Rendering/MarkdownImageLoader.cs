using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Resolves Markdown image targets to WPF images. Local files are decoded once and frozen so the
/// file handle is released; remote images download asynchronously so typing is never blocked.
/// </summary>
internal sealed class MarkdownImageLoader
{
    private const long LargeImageThreshold = 1_500_000;
    private const int LargeImageDecodeWidth = 1600;

    private readonly MarkdownRenderOptions _options;

    public MarkdownImageLoader(MarkdownRenderOptions options)
    {
        _options = options;
    }

    public Image? Create(string? url)
    {
        var source = Load(url);
        if (source is null)
            return null;

        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform
        };

        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private ImageSource? Load(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return LoadDataUri(url);

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (absolute.IsFile)
                return _options.RenderLocalImages ? LoadLocalFile(absolute.LocalPath) : null;

            if (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                return _options.LoadRemoteImages ? LoadRemote(absolute) : null;

            return null;
        }

        if (!_options.RenderLocalImages || string.IsNullOrEmpty(_options.BasePath))
            return null;

        var path = Uri.UnescapeDataString(url);
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_options.BasePath, path);

        return LoadLocalFile(path);
    }

    private static ImageSource? LoadLocalFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var fullPath = Path.GetFullPath(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(fullPath);

            if (new FileInfo(fullPath).Length > LargeImageThreshold)
                bitmap.DecodePixelWidth = LargeImageDecodeWidth;

            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadRemote(Uri uri)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadDataUri(string url)
    {
        try
        {
            var comma = url.IndexOf(',');
            if (comma < 0)
                return null;

            var metadata = url[5..comma];
            if (!metadata.Contains("base64", StringComparison.OrdinalIgnoreCase))
                return null;

            var bytes = Convert.FromBase64String(url[(comma + 1)..]);
            using var stream = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
