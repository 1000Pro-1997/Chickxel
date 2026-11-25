using System.Drawing;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.AiDraw;

/// <summary>
/// 생성한 그림을 픽셀 아트 정리 창에 넘길 준비: 프레임을 같은 크기로 맞춰 가로로 잇는다(프레임마다 한 칸). 변환 설정은 사용자가 정리 창에서 고른다.
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
}

/// <summary>
/// 생성 기록 하나(채팅의 "변환하기"로 정리 창을 연다): 이름, 프레임 수와 원본 안 프레임 격자(가로·세로 칸 수 — 띠는 프레임 수 × 1, 시트는 시트 배치),
/// 동작, 프레임마다 보낸 프롬프트, 만든 때. 원본 그림(이은 그림)은 같은 폴더의 <see cref="AiDrawJobStore.SourceFile"/>.
/// 예전 기록에 있던 변환 설정(<c>Settings</c>)은 읽지 않는다(격자가 없으면 한 장으로 연다).
/// </summary>
public sealed class AiDrawJob
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Frames { get; set; } = 1;
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public string? Action { get; set; }
    public List<string> Prompts { get; set; } = new();
    public DateTime CreatedUtc { get; set; }
    public int FrameDurationMs { get; set; } = 100;
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

    /// <summary>기록이 남아 있는지(지워졌으면 "변환하기"를 보이지 않음).</summary>
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
