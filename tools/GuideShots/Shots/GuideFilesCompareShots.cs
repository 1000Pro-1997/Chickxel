using System.Diagnostics;
using System.Drawing;
using Chickxel.App;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>파일 장(files-*) 비교 그림: 같은 입력을 설정만 바꿔 실제 앱 처리로 만든 결과를 한 장에 나란히(왼쪽 = 끔, 오른쪽 = 켬).</summary>
internal static class GuideFilesCompareShots
{
    /// <summary>나란히 놓을 때 한 픽셀을 몇 배로 키울지.</summary>
    private const int Zoom = 8;

    /// <summary>
    /// 영상 가져오기의 부드럽게 끔/켬: 픽셀 그림(예시 늑대귀 모험가)을 3.5배로 녹화한 무손실 영상을 실제 ffmpeg 가져오기(<see cref="FfmpegTool.Decode"/>)로
    /// 긴 변 98px(원래 그림 크기)에 맞춰 뽑는다. 끄면 최근접 픽셀(원래 색만), 켜면 면적 평균(경계에 섞인 색).
    /// </summary>
    [Shot("files-open-media-compare")]
    public static void VideoSmooth(ShotKit k)
    {
        var ffmpeg = FfmpegTool.Locate((string?)null) ?? throw new InvalidOperationException("ffmpeg not found");
        var art = PngIO.Load(ShotKit.SamplePng);

        // 녹화 화면: 게임 배경색 위에 그림을 3.5배 최근접으로 키운 한 장(화면 녹화처럼 정수배가 아님).
        const double scale = 3.5;
        int rw = (int)Math.Round(art.Width * scale), rh = (int)Math.Round(art.Height * scale);
        var screen = new PixelImage(rw, rh);
        screen.Clear(new Color32(58, 68, 102, 255));
        var scaled = new PixelImage(rw, rh);
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
                scaled[x, y] = art.Get((int)(x / scale), (int)(y / scale));
        screen.Composite(scaled);
        var still = Path.Combine(k.WorkDir, "recording.png");
        PngIO.Save(screen, still);
        var video = Path.Combine(k.WorkDir, "recording.mkv");
        Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-loop", "1", "-i", still, "-t", "1", "-r", "10",
            "-c:v", "ffv1", "-pix_fmt", "bgr0", video);

        var info = FfmpegTool.Probe(ffmpeg, video) ?? throw new InvalidOperationException("ffmpeg could not read the test video");
        PixelImage Import(bool smooth) => FfmpegTool.Decode(ffmpeg, video, info,
            new VideoImportOptions { Scale = VideoScaleMode.FitLongSide, LongSide = art.Height, MaxFrames = 1, Smooth = smooth },
            null, CancellationToken.None).Images[0];
        // 차이가 잘 보이게 머리 부분만 잘라 키운다(두 결과 모두 같은 곳).
        var head = new Rectangle(2, 2, 45, 42);
        var off = Import(false).Crop(head);
        var on = Import(true).Crop(head);

        const int pad = 8, gap = 24;
        int w = off.Width * Zoom, h = off.Height * Zoom;
        var sheet = new PixelImage(pad * 2 + gap + w * 2, pad * 2 + h);
        Paste(sheet, off, pad, pad);
        Paste(sheet, on, pad + w + gap, pad);
        var file = Path.Combine(ShotKit.OutDir, "files-open-media-compare.png");
        Directory.CreateDirectory(ShotKit.OutDir);
        PngIO.Save(sheet, file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }

    /// <summary><paramref name="image"/>를 <see cref="Zoom"/>배 최근접으로 키워 (<paramref name="dx"/>, <paramref name="dy"/>)에 놓는다.</summary>
    private static void Paste(PixelImage sheet, PixelImage image, int dx, int dy)
    {
        for (int y = 0; y < image.Height * Zoom; y++)
            for (int x = 0; x < image.Width * Zoom; x++)
                sheet[dx + x, dy + y] = image[x / Zoom, y / Zoom];
    }

    private static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + err);
    }
}
