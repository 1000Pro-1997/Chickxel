using System.Security.Cryptography;
using System.Text;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>게시 작성자의 프로필 그림. 커뮤니티 사이트의 HTTPS 이미지에만 접근하고 작은 PNG로 로컬에 보관한다.</summary>
public static class AuthorAvatarView
{
    private const int ImageWidth = 48;
    private const int MaxBytes = 2 << 20;
    private const int MaxCachedBytes = 128 << 10;
    private const int MaxFiles = 128;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private static readonly SemaphoreSlim Downloads = new(4);
    // UI 스레드에서만 접근한다. 같은 행들을 다시 그릴 때 이미 디코딩한 작은 그림을 재사용한다.
    private static readonly Dictionary<Uri, Task<Bitmap?>> Pending = [];
    private static readonly Dictionary<Uri, (Bitmap Image, LinkedListNode<Uri> Node)> Memory = [];
    private static readonly LinkedList<Uri> Recent = [];

    /// <summary>그림이 없거나 받지 못하면 SVG 기반 사람 아이콘을 남기는 둥근 칸.</summary>
    public static Control Create(AssetAuthor? author, double size)
    {
        var host = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new IconView(VectorIcon.CircleUser, AppTheme.DimTextBrush)
            {
                Width = Math.Round(size * 0.72), Height = Math.Round(size * 0.72),
            },
        };
        Show(host, author);
        return host;
    }

    private static async void Show(Border host, AssetAuthor? author)
    {
        if (await LoadAsync(author) is not { } bitmap) return;
        host.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill, Width = host.Width, Height = host.Height };
    }

    /// <summary>효과 타일처럼 직접 그리는 화면도 같은 작은 그림을 읽는다. 주소가 없거나 허용되지 않으면 null.</summary>
    public static async Task<Bitmap?> LoadAsync(AssetAuthor? author)
    {
        if (TrustedUrl(author?.AvatarUrl) is not { } uri) return null;
        if (Memory.TryGetValue(uri, out var cached))
        {
            Recent.Remove(cached.Node);
            Recent.AddFirst(cached.Node);
            return cached.Image;
        }
        if (!Pending.TryGetValue(uri, out var load)) Pending[uri] = load = LoadCoreAsync(uri);
        try
        {
            if (await load is not { } bitmap) return null;
            if (!Memory.ContainsKey(uri))
            {
                Memory.Add(uri, (bitmap, Recent.AddFirst(uri)));
                if (Memory.Count > MaxFiles)
                {
                    // 화면이 아직 참조할 수 있어 제거한 Bitmap을 여기서 Dispose하지 않는다.
                    var oldest = Recent.Last!;
                    Recent.RemoveLast();
                    Memory.Remove(oldest.Value);
                }
            }
            return bitmap;
        }
        finally { if (Pending.TryGetValue(uri, out var current) && current == load) Pending.Remove(uri); }
    }

    private static Uri? TrustedUrl(string? url)
    {
        if (url is null || !AssetManifest.IsHttpsUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var site = AppCommunity.Client.BaseUrl;
        return uri.Scheme == site.Scheme && uri.Host.Equals(site.Host, StringComparison.OrdinalIgnoreCase)
               && uri.Port == site.Port && uri.UserInfo.Length == 0 ? uri : null;
    }

    private static string CachePath(Uri uri)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
        return Path.Combine(Platform.AppFolder, "AuthorAvatars", hash + ".png");
    }

    private static async Task<Bitmap?> LoadCoreAsync(Uri uri)
    {
        string path = CachePath(uri);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length is > 0 and <= MaxCachedBytes)
            {
                using var file = File.OpenRead(path);
                var cached = Bitmap.DecodeToWidth(file, ImageWidth, BitmapInterpolationMode.HighQuality);
                if (cached.PixelSize.Height is > 0 and <= ImageWidth * 4) return cached;
                cached.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException) { }

        await Downloads.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return null;
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + length > MaxBytes) return null;
                bytes.Write(buffer, 0, length);
            }
            bytes.Position = 0;
            var bitmap = Bitmap.DecodeToWidth(bytes, ImageWidth, BitmapInterpolationMode.HighQuality);
            if (bitmap.PixelSize.Height is < 1 or > ImageWidth * 4)
            {
                bitmap.Dispose();
                return null;
            }
            try
            {
                using var png = new MemoryStream();
                bitmap.Save(png);
                if (png.Length <= MaxCachedBytes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temporary, png.GetBuffer().AsSpan(0, (int)png.Length));
                        File.Move(temporary, path, overwrite: true);
                        Prune(Path.GetDirectoryName(path)!, path);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            { /* 그림은 보이되 캐시는 저장하지 못함. */ }
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
        finally { Downloads.Release(); }
    }

    private static void Prune(string directory, string current)
    {
        var files = new DirectoryInfo(directory).EnumerateFiles("*.png")
            .Where(file => file.FullName != current).OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        foreach (var file in files.Skip(MaxFiles - 1)) file.Delete();
    }
}
