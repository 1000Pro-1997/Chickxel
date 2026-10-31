using System.Globalization;
using System.Text.RegularExpressions;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>영상 안의 비디오 스트림 하나(ffmpeg 출력에서 읽음): 입력 스트림 번호, 보이는 크기(회전 적용), 초당 프레임, 코덱 이름, 알파 여부.</summary>
public sealed record VideoStreamInfo(int Index, int Width, int Height, double Fps, string Codec, bool Alpha);

/// <summary>영상 정보: 길이(초, 모르면 null — 정지 그림), 비디오 스트림들.</summary>
public sealed record VideoInfo(double? DurationSec, IReadOnlyList<VideoStreamInfo> Streams)
{
    /// <summary>뽑을 스트림: 가장 큰 것, 크기가 같으면 초당 프레임이 많은 것(움직이는 AVIF의 정지 그림 대신 재생 트랙).</summary>
    public VideoStreamInfo Best => Streams.OrderByDescending(s => (long)s.Width * s.Height).ThenByDescending(s => s.Fps).First();

    /// <summary>길이가 없는 한 장짜리(AVIF·HEIC 정지 그림).</summary>
    public bool IsStill => DurationSec is null or <= 0;
}

/// <summary>영상 크기 조절.</summary>
public enum VideoScaleMode
{
    /// <summary>원래 크기.</summary>
    Keep,
    /// <summary>긴 변을 <see cref="VideoImportOptions.LongSide"/>px로(작게도 크게도).</summary>
    FitLongSide,
}

/// <summary>영상 가져오기 설정: 구간(초), 뽑을 초당 프레임, 최대 프레임 수, 크기 조절.</summary>
public sealed record VideoImportOptions
{
    public double StartSec { get; init; }
    /// <summary>끝(초). null이면 영상 끝까지.</summary>
    public double? EndSec { get; init; }
    public double Fps { get; init; } = VideoImportPlan.DefaultFpsCap;
    public int MaxFrames { get; init; } = VideoImportPlan.DefaultMaxFrames;
    public VideoScaleMode Scale { get; init; } = VideoScaleMode.Keep;
    public int LongSide { get; init; } = VideoImportPlan.DefaultLongSide;
    /// <summary>크기를 바꿀 때 부드럽게(면적 평균) — 아니면 최근접(픽셀 그대로).</summary>
    public bool Smooth { get; init; } = true;
}

/// <summary>영상에서 뽑을 프레임 계산(대화상자 예상치·ffmpeg 인자·프레임 길이).</summary>
public static class VideoImportPlan
{
    public const int DefaultMaxFrames = 120;
    public const double DefaultFpsCap = 12;
    public const double MinFps = 0.1, MaxFps = 60;
    public const int MaxFramesLimit = 3000;
    public const int DefaultLongSide = 256;
    /// <summary>긴 변이 이보다 크면 기본으로 <see cref="DefaultLongSide"/>에 맞춘다.</summary>
    public const int KeepSizeLimit = 512;

    /// <summary>영상에 맞춘 기본 설정: 영상 초당 프레임(최대 12), 최대 120장, 긴 변이 512px를 넘으면 256px로 부드럽게.</summary>
    public static VideoImportOptions Defaults(VideoInfo info)
    {
        var s = info.Best;
        double fps = s.Fps > 0 ? Math.Min(s.Fps, DefaultFpsCap) : DefaultFpsCap;
        bool big = Math.Max(s.Width, s.Height) > KeepSizeLimit;
        return new VideoImportOptions
        {
            Fps = Math.Round(fps, 3),
            Scale = big ? VideoScaleMode.FitLongSide : VideoScaleMode.Keep,
            LongSide = DefaultLongSide,
            Smooth = true,
        };
    }

    /// <summary>뽑을 구간 길이(초). 길이를 모르면 null.</summary>
    public static double? SegmentSec(VideoInfo info, VideoImportOptions o)
    {
        if (info.DurationSec is not { } d || d <= 0) return null;
        double start = Math.Clamp(o.StartSec, 0, d);
        double end = Math.Clamp(o.EndSec ?? d, start, d);
        return end - start;
    }

    /// <summary>
    /// 나올 프레임 수 예상: 정지 그림이면 1, 아니면 구간 × 초당 프레임(반올림, ffmpeg fps 필터와 같음 — ±1 차이 날 수 있음)과 최대 프레임 수 중 작은 값.
    /// 길이를 모르면 최대 프레임 수.
    /// </summary>
    public static int EstimateFrames(VideoInfo info, VideoImportOptions o)
    {
        if (info.IsStill) return 1;
        int max = Math.Clamp(o.MaxFrames, 1, MaxFramesLimit);
        if (SegmentSec(info, o) is not { } len) return max;
        double fps = Math.Clamp(o.Fps, MinFps, MaxFps);
        return (int)Math.Clamp(Math.Round(len * fps, MidpointRounding.AwayFromZero), 1, max);
    }

    /// <summary>뽑은 프레임 크기(긴 변 맞춤이면 비율 유지, 반올림, 최소 1, 최대 <see cref="PixelImage.MaxSize"/>).</summary>
    public static (int Width, int Height) OutputSize(VideoStreamInfo s, VideoImportOptions o)
    {
        int w = s.Width, h = s.Height;
        if (o.Scale == VideoScaleMode.FitLongSide && w > 0 && h > 0)
        {
            double k = Math.Clamp(o.LongSide, 1, PixelImage.MaxSize) / (double)Math.Max(w, h);
            w = (int)Math.Round(w * k, MidpointRounding.AwayFromZero);
            h = (int)Math.Round(h * k, MidpointRounding.AwayFromZero);
        }
        return (Math.Clamp(w, 1, PixelImage.MaxSize), Math.Clamp(h, 1, PixelImage.MaxSize));
    }

    /// <summary>프레임 길이(ms) = 1000 / 초당 프레임(최소 10ms).</summary>
    public static int FrameDurationMs(VideoImportOptions o) => FrameDurations.FromFps(Math.Clamp(o.Fps, MinFps, MaxFps));

    /// <summary>예상 메모리(바이트): 프레임 수 × 가로 × 세로 × 4.</summary>
    public static long EstimateBytes(VideoInfo info, VideoImportOptions o)
    {
        var (w, h) = OutputSize(info.Best, o);
        return (long)EstimateFrames(info, o) * w * h * 4;
    }
}

/// <summary>ffmpeg 명령 인자 만들기와 출력 읽기(실행은 앱이 함).</summary>
public static class Ffmpeg
{
    /// <summary>정보 읽기: <c>ffmpeg -hide_banner -nostdin -i 파일</c>(출력 파일이 없어 종료 코드는 1 — 오류 출력에서 읽음).</summary>
    public static IReadOnlyList<string> ProbeArgs(string path) => ["-hide_banner", "-nostdin", "-i", path];

    private static readonly Regex DurationRx = new(@"Duration:\s*(?:(\d+):(\d+):(\d+(?:\.\d+)?)|N/A)", RegexOptions.CultureInvariant);
    private static readonly Regex StreamRx = new(@"^\s*Stream #\d+:(\d+)(?:\[[^\]]*\])?(?:\([^)]*\))?:\s*(\w+):\s*(\w+)(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex SizeRx = new(@"[ ,](\d+)x(\d+)(?=[ ,\[]|$)", RegexOptions.CultureInvariant);
    private static readonly Regex FpsRx = new(@"([\d.]+)(k?) fps", RegexOptions.CultureInvariant);
    private static readonly Regex TbrRx = new(@"([\d.]+)(k?) tbr", RegexOptions.CultureInvariant);
    private static readonly Regex AlphaRx = new(@"^\s*alpha_mode\s*:\s*1\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex RotationRx = new(@"rotation of (-?[\d.]+) degrees", RegexOptions.CultureInvariant);
    private static readonly Regex PixFmtRx = new(@"\b(yuva\w*|rgba\w*|argb|bgra|abgr|gbrap\w*|ya\d+\w*|pal8)\b", RegexOptions.CultureInvariant);

    /// <summary><c>ffmpeg -i</c>의 오류 출력에서 길이·비디오 스트림을 읽는다. 비디오 스트림이 없으면 null.</summary>
    public static VideoInfo? ParseProbe(string stderr)
    {
        double? duration = null;
        var streams = new List<VideoStreamInfo>();
        VideoStreamInfo? current = null;
        void Flush()
        {
            if (current is { Width: > 0, Height: > 0 }) streams.Add(current);
            current = null;
        }
        foreach (var raw in stderr.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (duration is null && DurationRx.Match(line) is { Success: true } dm && dm.Groups[1].Success)
            {
                duration = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                           + int.Parse(dm.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                           + double.Parse(dm.Groups[3].Value, CultureInfo.InvariantCulture);
                continue;
            }
            if (StreamRx.Match(line) is { Success: true } sm)
            {
                Flush();
                if (sm.Groups[2].Value != "Video") continue;
                var rest = sm.Groups[4].Value;
                var size = SizeRx.Match(rest);
                if (!size.Success) continue;
                // 표지 그림(attached pic)은 영상이 아니다.
                if (rest.Contains("(attached pic)", StringComparison.Ordinal)) continue;
                double fps = Rate(FpsRx.Match(rest)) ?? Rate(TbrRx.Match(rest)) ?? 0;
                current = new VideoStreamInfo(
                    int.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture),
                    fps, sm.Groups[3].Value, PixFmtRx.IsMatch(rest));
                continue;
            }
            if (current is null) continue;
            if (AlphaRx.IsMatch(line)) current = current with { Alpha = true };
            else if (RotationRx.Match(line) is { Success: true } rm
                     && double.TryParse(rm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg)
                     && Math.Abs(Math.Round(deg) % 180) == 90)
                current = current with { Width = current.Height, Height = current.Width };
        }
        Flush();
        return streams.Count > 0 ? new VideoInfo(duration, streams) : null;
    }

    private static double? Rate(Match m)
    {
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        return m.Groups[2].Value == "k" ? v * 1000 : v;
    }

    /// <summary>
    /// 프레임 뽑기 인자: 구간(입력 앞 -ss = 빠르고 정확한 탐색, -t), 고른 스트림, fps 필터(정지 그림은 없음), 크기 조절(최근접 또는 면적 평균),
    /// 최대 프레임 수, 표준 출력으로 RGBA 원시 프레임. 알파가 있는 VP8·VP9(WebM)는 알파를 읽는 libvpx 디코더를 고른다.
    /// </summary>
    public static IReadOnlyList<string> DecodeArgs(string path, VideoInfo info, VideoImportOptions o)
    {
        var s = info.Best;
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
        if (s.Alpha && s.Codec == "vp9") args.AddRange(["-c:v", "libvpx-vp9"]);
        else if (s.Alpha && s.Codec == "vp8") args.AddRange(["-c:v", "libvpx"]);
        double? segment = VideoImportPlan.SegmentSec(info, o);
        bool still = info.IsStill;
        if (!still && o.StartSec > 0) args.AddRange(["-ss", Num(o.StartSec)]);
        args.AddRange(["-i", path]);
        if (!still && o.EndSec is not null && segment is { } len) args.AddRange(["-t", Num(len)]);
        args.AddRange(["-map", "0:" + s.Index.ToString(CultureInfo.InvariantCulture), "-an", "-sn", "-dn"]);
        var filters = new List<string>();
        if (!still) filters.Add("fps=" + Num(Math.Clamp(o.Fps, VideoImportPlan.MinFps, VideoImportPlan.MaxFps)));
        var (w, h) = VideoImportPlan.OutputSize(s, o);
        if (w != s.Width || h != s.Height)
            filters.Add($"scale={w}:{h}:flags={(o.Smooth ? "area" : "neighbor")}");
        if (filters.Count > 0) args.AddRange(["-vf", string.Join(',', filters)]);
        int frames = still ? 1 : Math.Clamp(o.MaxFrames, 1, VideoImportPlan.MaxFramesLimit);
        args.AddRange(["-frames:v", frames.ToString(CultureInfo.InvariantCulture), "-f", "rawvideo", "-pix_fmt", "rgba", "-"]);
        return args;
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// ffmpeg를 찾아볼 흔한 설치 위치(PATH 다음에 살핌): Windows는 winget·Chocolatey·Scoop·Program Files·C:\ffmpeg, macOS·Linux는 Homebrew·/usr.
    /// <paramref name="wingetPackages"/> = winget 패키지 폴더 아래 FFmpeg 패키지 폴더들(앱이 찾아 넘김).
    /// </summary>
    public static IEnumerable<string> CommonLocations(bool windows, string home, string localAppData, string programFiles, string programData,
        IEnumerable<string> wingetPackages)
    {
        if (windows)
        {
            if (localAppData.Length > 0) yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            foreach (var pkg in wingetPackages) yield return Path.Combine(pkg, "bin", "ffmpeg.exe");
            if (programData.Length > 0) yield return Path.Combine(programData, "chocolatey", "bin", "ffmpeg.exe");
            if (home.Length > 0) yield return Path.Combine(home, "scoop", "shims", "ffmpeg.exe");
            if (programFiles.Length > 0) yield return Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe");
            yield return @"C:\ffmpeg\bin\ffmpeg.exe";
            yield break;
        }
        yield return "/opt/homebrew/bin/ffmpeg";
        yield return "/usr/local/bin/ffmpeg";
        yield return "/usr/bin/ffmpeg";
        yield return "/snap/bin/ffmpeg";
    }
}
