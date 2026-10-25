using System.Diagnostics;
using System.Runtime.InteropServices;
using Gpixel.App.Ai;
using Gpixel.App.Localization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// ffmpeg 실행(영상·AVIF·HEIC 열기): 찾기(설정 경로 → PATH → 흔한 설치 위치), 정보 읽기, RGBA 원시 프레임을 표준 출력으로 받아 프레임 만들기(진행·취소),
/// 파일 목록 썸네일. 인자 만들기·출력 읽기는 <see cref="Ffmpeg"/>(Core). 앱에 넣어 배포하지 않는다(자동 내려받기 없음).
/// </summary>
public static class FfmpegTool
{
    public const string DownloadUrl = "https://ffmpeg.org/download.html";

    /// <summary>ffmpeg 실행 파일. 못 찾으면 null.</summary>
    public static string? Locate(AppSettings settings) => Locate(settings.FfmpegPath);

    /// <summary>ffmpeg 실행 파일(<paramref name="overridePath"/> = 설정에서 직접 정한 경로 — 없으면 PATH·흔한 설치 위치). 못 찾으면 null.</summary>
    public static string? Locate(string? overridePath) => AiCli.Locate("ffmpeg", overridePath, CommonLocations());

    private static IEnumerable<string> CommonLocations()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Ffmpeg.CommonLocations(OperatingSystem.IsWindows(), AiCli.Home, local,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            WingetPackages(local));
    }

    /// <summary>winget으로 설치한 FFmpeg 패키지 안의 빌드 폴더들(…\Packages\Gyan.FFmpeg_…\ffmpeg-8.x-full_build).</summary>
    private static IEnumerable<string> WingetPackages(string localAppData)
    {
        if (!OperatingSystem.IsWindows() || localAppData.Length == 0) return [];
        try
        {
            var root = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (!Directory.Exists(root)) return [];
            return Directory.GetDirectories(root, "*FFmpeg*").SelectMany(Directory.GetDirectories).OrderDescending(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Process Start(string ffmpeg, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new IOException(UiStrings.Ffmpeg_StartFailed);
    }

    private static void Kill(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    /// <summary>영상 정보(길이·크기·초당 프레임). 비디오 스트림이 없거나 읽을 수 없으면 null. ffmpeg를 띄울 수 없으면 예외.</summary>
    public static VideoInfo? Probe(string ffmpeg, string path, CancellationToken token = default)
    {
        using var p = Start(ffmpeg, Ffmpeg.ProbeArgs(path));
        var stderr = p.StandardError.ReadToEndAsync(token);
        _ = p.StandardOutput.ReadToEndAsync(token);
        using var reg = token.Register(() => Kill(p));
        if (!p.WaitForExit(TimeSpan.FromSeconds(20))) Kill(p);
        token.ThrowIfCancellationRequested();
        return Ffmpeg.ParseProbe(stderr.GetAwaiter().GetResult());
    }

    /// <summary>
    /// 설정대로 프레임을 뽑는다: ffmpeg 표준 출력의 RGBA 원시 프레임을 차례로 읽어 그림으로(진행 = 받은 프레임 수).
    /// 취소하면 ffmpeg를 끝내고 <see cref="OperationCanceledException"/>. 프레임을 하나도 받지 못하면 ffmpeg 오류 출력과 함께 <see cref="IOException"/>.
    /// </summary>
    public static DecodedAnimation Decode(string ffmpeg, string path, VideoInfo info, VideoImportOptions options,
        IProgress<int>? progress, CancellationToken token)
    {
        var (w, h) = VideoImportPlan.OutputSize(info.Best, options);
        int ms = info.IsStill ? Frame.DefaultDurationMs : VideoImportPlan.FrameDurationMs(options);
        using var p = Start(ffmpeg, Ffmpeg.DecodeArgs(path, info, options));
        var stderr = p.StandardError.ReadToEndAsync(CancellationToken.None);
        using var reg = token.Register(() => Kill(p));
        var images = new List<PixelImage>();
        var buffer = new byte[w * h * 4];
        var output = p.StandardOutput.BaseStream;
        try
        {
            while (output.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length)
            {
                token.ThrowIfCancellationRequested();
                var pixels = new Color32[w * h];
                var src = MemoryMarshal.Cast<byte, uint>(buffer);
                var dst = MemoryMarshal.Cast<Color32, uint>(pixels.AsSpan());
                // RGBA 바이트(리틀 엔디언 uint = ABGR) → Color32(ARGB): R과 B를 맞바꾼다.
                for (int i = 0; i < src.Length; i++)
                {
                    uint v = src[i];
                    dst[i] = (v & 0xFF00FF00u) | ((v & 0xFFu) << 16) | ((v >> 16) & 0xFFu);
                }
                images.Add(new PixelImage(w, h, pixels));
                progress?.Report(images.Count);
            }
        }
        catch (IOException) when (token.IsCancellationRequested)
        {
        }
        token.ThrowIfCancellationRequested();
        p.WaitForExit();
        if (images.Count == 0)
        {
            var err = stderr.GetAwaiter().GetResult().Trim();
            throw new IOException(err.Length > 0 ? err : UiStrings.Ffmpeg_NoFrames);
        }
        return new DecodedAnimation(w, h, images, Enumerable.Repeat(ms, images.Count).ToList());
    }

    /// <summary>파일 목록 썸네일용 첫 프레임(긴 변 <paramref name="size"/>px 이하). 실패하면 null.</summary>
    public static PixelImage? FirstFrame(string ffmpeg, string path, int size, CancellationToken token)
    {
        try
        {
            using var p = Start(ffmpeg,
            [
                "-hide_banner", "-nostdin", "-loglevel", "error", "-i", path, "-an", "-sn", "-dn", "-frames:v", "1",
                "-vf", $"scale={size}:{size}:force_original_aspect_ratio=decrease:flags=area", "-f", "image2pipe", "-c:v", "png", "-",
            ]);
            _ = p.StandardError.ReadToEndAsync(token);
            using var reg = token.Register(() => Kill(p));
            using var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, token);
            if (!copy.Wait(TimeSpan.FromSeconds(10)))
            {
                Kill(p);
                return null;
            }
            p.WaitForExit();
            if (ms.Length == 0) return null;
            ms.Position = 0;
            return PngIO.Load(ms);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or AggregateException
                                       or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>ffmpeg가 없을 때: 내려받는 곳과 설정 위치를 알리고, 원하면 내려받기 쪽을 연다.</summary>
    public static void ShowMissing(Visual? owner)
    {
        if (Ui.Ask(owner, UiStrings.Ffmpeg_Missing, UiStrings.Ffmpeg_MissingTitle, MessageButtons.OKCancel, MessageIcon.Info,
                UiStrings.Ffmpeg_OpenDownload, UiStrings.S_Ui_CloseOnly_11312F9A81) == DialogResult.OK)
            Updater.OpenUrl(owner, DownloadUrl);
    }
}
