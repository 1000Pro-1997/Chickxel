namespace Chickxel.Core.Model;

/// <summary>애니메이션 프레임: 칸 이름 + 표시 시간(ms).</summary>
public sealed class AnimationFrame
{
    public const int MinMs = 1;

    private int _ms;

    /// <summary>표시할 칸(<see cref="Slice.Name"/>).</summary>
    public string Slice { get; set; }

    /// <summary>프레임 길이(밀리초, 최소 1).</summary>
    public int Ms
    {
        get => _ms;
        set => _ms = Math.Max(MinMs, value);
    }

    public AnimationFrame(string slice, int ms)
    {
        Slice = slice;
        Ms = ms;
    }

    public AnimationFrame Clone() => new(Slice, Ms);

    public override string ToString() => $"{Slice} ({Ms}ms)";
}

/// <summary>애니메이션 = 이름 + 프레임 목록 + 반복 여부 + 기본 FPS.</summary>
public sealed class AnimationClip
{
    public const int DefaultFps = 10;

    private int _fps = DefaultFps;

    public string Name { get; set; }
    public bool Loop { get; set; } = true;

    /// <summary>새 프레임을 추가하거나 "모두 같은 길이로" 할 때 쓰는 기본 FPS(1~120).</summary>
    public int Fps
    {
        get => _fps;
        set => _fps = Math.Clamp(value, 1, 120);
    }

    public List<AnimationFrame> Frames { get; } = new();

    public AnimationClip(string name) => Name = name;

    /// <summary>기본 FPS에 해당하는 프레임 길이(ms).</summary>
    public int DefaultFrameMs => Math.Max(AnimationFrame.MinMs, (int)Math.Round(1000.0 / Fps));

    /// <summary>한 바퀴 전체 길이(ms).</summary>
    public int TotalMs => Frames.Sum(f => f.Ms);

    /// <summary>기본 FPS 길이로 프레임을 추가한다.</summary>
    public AnimationFrame AddFrame(string slice, int? ms = null)
    {
        var frame = new AnimationFrame(slice, ms ?? DefaultFrameMs);
        Frames.Add(frame);
        return frame;
    }

    /// <summary>모든 프레임 길이를 기본 FPS 길이로 맞춘다.</summary>
    public void ApplyFpsToAllFrames()
    {
        foreach (var f in Frames) f.Ms = DefaultFrameMs;
    }

    public AnimationClip Clone()
    {
        var c = new AnimationClip(Name) { Loop = Loop, Fps = Fps };
        c.Frames.AddRange(Frames.Select(f => f.Clone()));
        return c;
    }

    public override string ToString() => $"{Name} ({Frames.Count})";
}
