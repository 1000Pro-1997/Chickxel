using System.Drawing;
using System.Text.Json;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.Core.Pixelate;

namespace Gpixel.Core.AiDraw;

/// <summary>
/// 생성한 그림을 픽셀 아트로: 프레임을 같은 크기로 맞춰 가로로 잇고(프레임마다 한 칸), 한 원본으로 함께 변환한다(같은 격자 찾기·공유 팔레트).
/// </summary>
public static class AiDrawConversion
{
    /// <summary>프레임 그림 긴 변 상한(넘으면 정수 배로 평균 내 줄임 — 생성 그림은 1000px 남짓이고 픽셀 칸이 커서 줄여도 격자가 남는다).</summary>
    public const int MaxFrameSide = 640;
    /// <summary>
    /// 스프라이트 시트 긴 변 상한(넘으면 정수 배로 평균 내 줄임). 생성 시트는 2000px 남짓(표본 2172×724)이라 웬만하면 원래 크기 그대로 쓴다: 예전 상한 1600은
    /// 이런 시트를 ×½로 줄여 칸도 반이 되고(마법사 꼬마 시트 7px → 3.5px) 칸 경계 지역 보정(칸 <see cref="LocalGrid.MinCell"/>px 이상)이 꺼지며
    /// 외곽선 두께 재기도 거칠어져, 원본 보기의 칸 선이 덩어리를 비껴갔다(기본 변환 점수 57.4 → 원래 크기 66.5). 원래 크기는 한 번 돌리는 데 ×½의 약 1.6배.
    /// </summary>
    public const int MaxSheetSide = 4096;

    /// <summary>긴 변이 <paramref name="maxSide"/> 이하가 되는 가장 작은 정수 배로 칸 평균(알파 가중) 축소. 이미 작으면 그대로.</summary>
    public static PixelImage Downscale(PixelImage img, int maxSide)
    {
        int f = (int)Math.Ceiling(Math.Max(img.Width, img.Height) / (double)Math.Max(1, maxSide));
        if (f <= 1) return img;
        int w = Math.Max(1, img.Width / f), h = Math.Max(1, img.Height / f);
        var dst = new PixelImage(w, h);
        var src = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                long r = 0, g = 0, b = 0, a = 0;
                for (int yy = 0; yy < f; yy++)
                {
                    int row = (y * f + yy) * img.Width + x * f;
                    for (int xx = 0; xx < f; xx++)
                    {
                        var c = src[row + xx];
                        r += c.R * c.A;
                        g += c.G * c.A;
                        b += c.B * c.A;
                        a += c.A;
                    }
                }
                dst.Pixels[y * w + x] = a == 0 ? Color32.Transparent
                    : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / (f * f)));
            }
        return dst;
    }

    /// <summary>
    /// 프레임을 가장 큰 가로·세로로 맞춰(가운데, 남는 곳은 그 프레임 왼쪽 위 픽셀 색 — 투명 배경이면 투명) 가로로 잇는다. 프레임이 하나면 그대로.
    /// </summary>
    public static PixelImage Combine(IReadOnlyList<PixelImage> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames", nameof(frames));
        if (frames.Count == 1) return frames[0];
        int w = frames.Max(f => f.Width), h = frames.Max(f => f.Height);
        var strip = new PixelImage(w * frames.Count, h);
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            var fill = f.Get(0, 0);
            if (fill.A < 128) fill = Color32.Transparent;
            strip.FillRect(new Rectangle(i * w, 0, w, h), fill);
            strip.Blit(f, i * w + (w - f.Width) / 2, (h - f.Height) / 2);
        }
        return strip;
    }

    /// <summary>
    /// 변환 설정: <paramref name="baseline"/>에 프레임 나누기(여럿이면 가로 <paramref name="frames"/>칸, 하나면 나누지 않음 — 공통 캔버스, 공유 팔레트),
    /// 맞추기(<paramref name="keepOffsets"/> = 칸 안 원래 자리 — 점프처럼 높이가 바뀌는 동작, 아니면 아래 가운데), 배경 지우기,
    /// 결과 크기(0 = 찾은 격자 그대로, 아니면 그 크기 안에 맞춤)를 넣는다. 외곽선 1px 맞추기 등 나머지는 그대로.
    /// </summary>
    public static PixelateSettings Settings(PixelateSettings baseline, int frames, int sizeTarget, bool removeBackground, bool keepOffsets)
    {
        var s = baseline with
        {
            Background = baseline.Background with { Mode = removeBackground ? BackgroundMode.Auto : BackgroundMode.None },
            Frames = frames > 1
                ? baseline.Frames with { Mode = FrameSplitMode.Grid, Columns = frames, Rows = 1, CommonCanvas = true, Align = keepOffsets ? FrameAlign.KeepOffsets : FrameAlign.BottomCenter }
                : baseline.Frames with { Mode = FrameSplitMode.Single },
            Palette = baseline.Palette with { Shared = true },
        };
        return sizeTarget > 0
            ? s with { Size = s.Size with { Mode = OutputSizeMode.FixedSize, Width = sizeTarget, Height = sizeTarget, KeepAspect = true } }
            : s with { Size = s.Size with { Mode = OutputSizeMode.FromScale } };
    }

    /// <summary>
    /// 스프라이트 시트(한 장에 프레임 <paramref name="frames"/>개)를 나눌 설정과 준비 결과: 먼저 자동 나누기(빈 열·행 기준, 기대 프레임 수 = <paramref name="frames"/> —
    /// 맞닿은 스프라이트는 가장 얇은 세로 줄에서 가름, 바닥 가운데 맞추기 — <paramref name="keepOffsets"/>면 칸 안 원래 자리)로 해 보고, 나온 프레임 수가
    /// <paramref name="frames"/>와 다르면 시트 배치(<paramref name="layout"/>)대로 격자로 다시 나눈다(칸 선을 넘은 손끝·발끝은 덩어리째 제 스프라이트에).
    /// 공통 캔버스·공유 팔레트·배경 지우기·결과 크기는 <paramref name="settings"/>(<see cref="Settings"/>로 만든 것) 그대로.
    /// </summary>
    public static (PixelateSource Source, PixelateSettings Settings) PrepareSheet(PixelImage sheet, PixelateSettings settings, int frames, AiDrawSheetLayout layout, bool keepOffsets)
    {
        var align = keepOffsets ? FrameAlign.KeepOffsets : FrameAlign.BottomCenter;
        var auto = settings with { Frames = settings.Frames with { Mode = FrameSplitMode.Auto, ExpectedFrames = frames, CommonCanvas = true, Align = align } };
        var src = PixelateSource.Prepare(sheet, auto);
        if (src.Split.Frames.Count == frames || frames <= 1) return (src, auto);
        var grid = settings with
        {
            Frames = settings.Frames with { Mode = FrameSplitMode.Grid, Columns = Math.Max(1, layout.Columns), Rows = Math.Max(1, layout.Rows), CommonCanvas = true, Align = align },
        };
        return (PixelateSource.Prepare(sheet, grid), grid);
    }

    /// <summary>
    /// 생성한 그림(<paramref name="source"/> — 시트면 <see cref="Downscale"/>한 한 장, 아니면 <see cref="Combine"/>한 띠)을 변환한다: 시트(<paramref name="sheet"/>)면
    /// <see cref="PrepareSheet"/>(배치 = <see cref="AiDrawSheetLayout.For"/>), 아니면 <paramref name="settings"/> 그대로 준비해 <see cref="Run(PixelateSource, PixelateSettings, bool, PixelateOptimizeOptions, Action{PixelateOptimizeProgress}?, CancellationToken)"/>.
    /// 결과가 실패했는지(<see cref="PixelateResult.Failed"/>)는 부르는 쪽이 보고 넣지 않는다.
    /// </summary>
    public static (PixelateResult Result, PixelateSettings Settings) Convert(PixelImage source, PixelateSettings settings, int frames, bool sheet, bool keepOffsets,
        bool optimize, PixelateOptimizeOptions options, Action<PixelateOptimizeProgress>? onProgress = null, CancellationToken ct = default)
    {
        var (src, split) = sheet
            ? PrepareSheet(source, settings, frames, AiDrawSheetLayout.For(frames), keepOffsets)
            : (PixelateSource.Prepare(source, settings), settings);
        return Run(src, split, optimize, options, onProgress, ct);
    }

    /// <summary>
    /// 변환한다: <paramref name="optimize"/>면 자동 최적화(<paramref name="options"/> — 정리 창의 자동 최적화 항목·예산, 진행은 <paramref name="onProgress"/>)의 1위
    /// (실패한 결과는 1위가 되지 않음 — 모두 실패면 시작 결과), 아니면 설정 그대로. 스프라이트를 못 찾으면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public static (PixelateResult Result, PixelateSettings Settings) Run(PixelImage source, PixelateSettings settings, bool optimize,
        PixelateOptimizeOptions options, CancellationToken ct = default)
        => Run(PixelateSource.Prepare(source, settings), settings, optimize, options, null, ct);

    /// <inheritdoc cref="Run(PixelImage, PixelateSettings, bool, PixelateOptimizeOptions, CancellationToken)"/>
    public static (PixelateResult Result, PixelateSettings Settings) Run(PixelateSource src, PixelateSettings settings, bool optimize,
        PixelateOptimizeOptions options, Action<PixelateOptimizeProgress>? onProgress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (src.Split.Frames.Count == 0) throw new InvalidOperationException("no sprite found in the generated image");
        if (!optimize)
        {
            var r = Pixelator.Run(src, settings, ct);
            return (r, settings);
        }
        var best = PixelateOptimizer.Run(src, settings, options, onProgress, ct).Best;
        return (best.Result, best.Settings);
    }
}

/// <summary>
/// 생성·변환 기록 하나(채팅의 "변환 설정 보기"로 다시 연다): 이름, 프레임 수, 쓴 변환 설정(JSON), 동작, 프레임마다 보낸 프롬프트, 만든 때.
/// 원본 그림(이은 그림)은 같은 폴더의 <see cref="AiDrawJobStore.SourceFile"/>.
/// </summary>
public sealed class AiDrawJob
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Frames { get; set; } = 1;
    public string? Action { get; set; }
    public string Settings { get; set; } = "";
    public List<string> Prompts { get; set; } = new();
    public DateTime CreatedUtc { get; set; }
    public int FrameDurationMs { get; set; } = 100;

    public PixelateSettings ReadSettings() => PixelatePresets.ParseSettings(Settings) ?? new PixelateSettings();
}

/// <summary>
/// 생성 기록 보관소(<c>&lt;설정 폴더&gt;/Gpixel/AiDraw/&lt;id&gt;/</c>): <c>job.json</c>, 변환한 원본 <c>source.png</c>, 생성한 그림(스프라이트 시트 <c>sheet.png</c> 또는 프레임 <c>frame-01.png</c>…).
/// 새로 넣을 때 오래된 기록부터 지워 <see cref="MaxJobs"/>개까지 둔다.
/// </summary>
public sealed class AiDrawJobStore(string root)
{
    public const int MaxJobs = 40;
    public const string JobFile = "job.json", SourceFile = "source.png";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; } = root;

    public string DirectoryOf(string id)
        => ChatHistoryStore.IsValidName(id) ? Path.Combine(Root, id) : throw new ArgumentException($"Invalid job id: {id}", nameof(id));

    /// <summary>한 장에 그린 스프라이트 시트(애니메이션을 한 번에 생성했을 때).</summary>
    public string SheetPath(string id) => Path.Combine(DirectoryOf(id), "sheet.png");

    /// <summary>새 기록 폴더를 만들고 id를 돌려준다(프레임을 먼저 저장해 두려고 — <see cref="Save"/> 전에 생성 중 그림을 넣음).</summary>
    public string Create()
    {
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(DirectoryOf(id));
        return id;
    }

    public string FramePath(string id, int index) => Path.Combine(DirectoryOf(id), $"frame-{index + 1:00}.png");

    /// <summary>기록이 남아 있는지(지워졌으면 "변환 설정 보기"를 보이지 않음).</summary>
    public bool Exists(string id) => ChatHistoryStore.IsValidName(id) && File.Exists(Path.Combine(Root, id, JobFile));

    /// <summary>기록과 이은 원본을 쓰고 오래된 기록을 지운다.</summary>
    public void Save(AiDrawJob job, PixelImage source)
    {
        var dir = DirectoryOf(job.Id);
        Directory.CreateDirectory(dir);
        PngIO.Save(source, Path.Combine(dir, SourceFile));
        File.WriteAllText(Path.Combine(dir, JobFile), JsonSerializer.Serialize(job, Json));
        Prune(MaxJobs, job.Id);
    }

    /// <summary>기록과 원본(없거나 깨졌으면 null).</summary>
    public (AiDrawJob Job, PixelImage Source)? Load(string id)
    {
        try
        {
            var dir = DirectoryOf(id);
            var job = JsonSerializer.Deserialize<AiDrawJob>(File.ReadAllText(Path.Combine(dir, JobFile)));
            if (job is null) return null;
            return (job, PngIO.Load(Path.Combine(dir, SourceFile)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>기록 폴더가 <paramref name="max"/>개를 넘으면 이름(만든 때) 순으로 오래된 것부터 지운다(<paramref name="keepId"/>는 남김).</summary>
    public void Prune(int max, string? keepId = null)
    {
        if (!Directory.Exists(Root)) return;
        var dirs = Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>()
            .Where(ChatHistoryStore.IsValidName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        int extra = dirs.Count - max;
        foreach (var name in dirs)
        {
            if (extra <= 0) break;
            if (name == keepId) continue;
            try
            {
                Directory.Delete(Path.Combine(Root, name), recursive: true);
                extra--;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
