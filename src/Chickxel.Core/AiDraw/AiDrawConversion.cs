using System.Drawing;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.AiDraw;

/// <summary>
/// 생성한 그림을 픽셀 아트로: 프레임을 같은 크기로 맞춰 가로로 잇고(프레임마다 한 칸), 한 원본으로 대표 프리셋 설정 그대로(프레임 나누기만 프레임 수에 맞춰) 변환한다.
/// </summary>
public static class AiDrawConversion
{
    /// <summary>긴 변이 <paramref name="maxSide"/> 이하가 되는 가장 작은 정수 배로 칸 평균(알파 가중) 축소. 이미 작으면 그대로.</summary>
    public static PixelImage Downscale(PixelImage img, int maxSide)
        => Shrink(img, (int)Math.Ceiling(Math.Max(img.Width, img.Height) / (double)Math.Max(1, maxSide)));

    /// <summary>정수 배 <paramref name="f"/>로 칸 평균(알파 가중) 축소. 1 이하면 그대로.</summary>
    private static PixelImage Shrink(PixelImage img, int f)
    {
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
    /// 생성 그림은 원래 크기 그대로 잇는다(정리 창에서 같은 그림을 직접 돌린 결과와 같게). 이은 띠가 그림 크기 상한(<see cref="PixelImage.MaxSize"/>)을
    /// 넘을 때만 모든 프레임을 같은 정수 배로 줄인다.
    /// </summary>
    public static PixelImage Combine(IReadOnlyList<PixelImage> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames", nameof(frames));
        if (frames.Count == 1) return frames[0];
        int w = frames.Max(f => f.Width), h = frames.Max(f => f.Height);
        int shrink = (int)Math.Ceiling(w * (double)frames.Count / PixelImage.MaxSize);
        if (shrink > 1) return Combine([.. frames.Select(f => Shrink(f, shrink))]);
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
    /// 그리기 설정에서 켠 것만 대표 프리셋 시작 설정(<paramref name="preset"/>) 위에 덮어쓴다(둘 다 기본 끔 — 끄면 프리셋 그대로):
    /// 결과 크기 보정(<paramref name="sizeTarget"/> &gt; 0 — 그 정사각형 안에 비율 유지로 맞춤), 배경 지우기(<paramref name="removeBackground"/> — 자동으로 지움).
    /// </summary>
    public static PixelateSettings WithDrawOptions(PixelateSettings preset, int sizeTarget, bool removeBackground)
    {
        var s = removeBackground ? preset with { Background = preset.Background with { Mode = BackgroundMode.Auto } } : preset;
        return sizeTarget > 0
            ? s with { Size = s.Size with { Mode = OutputSizeMode.FixedSize, Width = sizeTarget, Height = sizeTarget, KeepAspect = true } }
            : s;
    }

    /// <summary>
    /// 생성한 그림(<paramref name="source"/> — 시트 한 장 또는 <see cref="Combine"/>한 띠, 프레임 <paramref name="frames"/>개가 <paramref name="layout"/>대로 놓임)을
    /// 대표 프리셋의 시작 설정(<paramref name="preset"/> — 그리기 설정에서 켠 것은 <see cref="WithDrawOptions"/>로 덮어쓴 것)으로 변환한다.
    /// 배경·팔레트·결과 크기·맞춤·공통 캔버스·덩어리 합치기는 모두 그 설정 그대로이고,
    /// 프레임 나누기만 생성한 프레임 수에 맞춘다: 한 장이면 나누지 않고, 여럿이면 자동 나누기(기대 프레임 수 = <paramref name="frames"/>) — 나온 프레임 수가
    /// 다르면(맞닿은 프레임 등) <paramref name="layout"/> 격자로 나눈다.
    /// <paramref name="optimize"/>면 자동 최적화(<paramref name="options"/> — 대표 프리셋의 칸별 최적화·예산, 진행은 <paramref name="onProgress"/>)의 1위
    /// (실패한 결과는 1위가 되지 않음 — 모두 실패면 시작 결과), 아니면 설정 그대로 한 번. 쓴 설정을 함께 돌려준다.
    /// 스프라이트를 못 찾으면 <see cref="InvalidOperationException"/>. 결과가 실패했는지(<see cref="PixelateResult.Failed"/>)는 부르는 쪽이 보고 넣지 않는다.
    /// </summary>
    public static (PixelateResult Result, PixelateSettings Settings) Run(PixelImage source, PixelateSettings preset, int frames, AiDrawSheetLayout layout,
        bool optimize, PixelateOptimizeOptions options, Action<PixelateOptimizeProgress>? onProgress = null, CancellationToken ct = default)
    {
        var settings = preset with
        {
            Frames = preset.Frames with
            {
                Mode = frames > 1 ? FrameSplitMode.Auto : FrameSplitMode.Single,
                ExpectedFrames = frames > 1 ? frames : 0,
                ManualRegions = null,
            },
        };
        var src = PixelateSource.Prepare(source, settings);
        if (frames > 1 && src.Split.Frames.Count != frames)
        {
            settings = settings with { Frames = settings.Frames with { Mode = FrameSplitMode.Grid, Columns = Math.Max(1, layout.Columns), Rows = Math.Max(1, layout.Rows) } };
            src = PixelateSource.Prepare(source, settings);
        }
        return Run(src, settings, optimize, options, onProgress, ct);
    }

    /// <summary>준비한 원본을 <paramref name="settings"/>로 변환한다(<see cref="Run(PixelImage, PixelateSettings, int, AiDrawSheetLayout, bool, PixelateOptimizeOptions, Action{PixelateOptimizeProgress}?, CancellationToken)"/>의 변환 부분).</summary>
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
/// 생성 기록 보관소(<c>&lt;설정 폴더&gt;/Chickxel/AiDraw/&lt;id&gt;/</c>): <c>job.json</c>, 변환한 원본 <c>source.png</c>, 생성한 그림(스프라이트 시트 <c>sheet.png</c> 또는 프레임 <c>frame-01.png</c>…).
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
        AtomicFile.WriteText(Path.Combine(dir, JobFile), JsonSerializer.Serialize(job, Json));
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
