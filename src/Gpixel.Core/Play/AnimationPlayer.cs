using Gpixel.Core.Model;

namespace Gpixel.Core.Play;

/// <summary>
/// 애니메이션 재생기: 경과 시간(ms) → 현재 프레임. 반복이 아니면 마지막 프레임에서 멈춘다.
/// </summary>
public sealed class AnimationPlayer
{
    public AnimationClip? Clip { get; private set; }

    /// <summary>현재 클립 시작부터 흐른 시간(ms).</summary>
    public double TimeMs { get; private set; }

    public bool IsPlaying { get; set; } = true;

    /// <summary>재생 속도 배율(1 = 보통).</summary>
    public double Speed { get; set; } = 1.0;

    public AnimationPlayer(AnimationClip? clip = null) => Clip = clip;

    /// <summary>현재 프레임 번호(클립이 없거나 비면 -1).</summary>
    public int FrameIndex => Clip is null ? -1 : FrameAt(Clip, TimeMs);

    /// <summary>현재 프레임(없으면 null).</summary>
    public AnimationFrame? CurrentFrame => FrameIndex is var i and >= 0 ? Clip!.Frames[i] : null;

    /// <summary>반복하지 않는 클립이 끝까지 재생되었는지.</summary>
    public bool IsFinished => Clip is { Loop: false } c && c.Frames.Count > 0 && TimeMs >= c.TotalMs;

    /// <summary>
    /// 클립을 바꾼다. 같은 클립이면 그대로 이어서 재생하고(<paramref name="restart"/>가 false일 때),
    /// 다른 클립이면 처음부터 재생한다.
    /// </summary>
    public void Play(AnimationClip? clip, bool restart = false)
    {
        if (!restart && ReferenceEquals(clip, Clip)) return;
        Clip = clip;
        TimeMs = 0;
        IsPlaying = true;
    }

    public void Reset() => TimeMs = 0;

    /// <summary>시간을 진행한다(재생 중일 때만).</summary>
    public void Update(double deltaMs)
    {
        if (!IsPlaying || Clip is null || deltaMs <= 0) return;
        TimeMs += deltaMs * Speed;
        int total = Clip.TotalMs;
        if (total <= 0) return;
        if (Clip.Loop) TimeMs %= total;
        else if (TimeMs > total) TimeMs = total;
    }

    /// <summary>다음 프레임 시작으로 건너뛴다(한 프레임 진행 버튼).</summary>
    public void StepFrame(int direction = 1)
    {
        if (Clip is null || Clip.Frames.Count == 0) return;
        int n = Clip.Frames.Count;
        int next = FrameIndex + Math.Sign(direction == 0 ? 1 : direction);
        next = Clip.Loop ? ((next % n) + n) % n : Math.Clamp(next, 0, n - 1);
        TimeMs = FrameStartMs(Clip, next);
    }

    /// <summary>클립의 <paramref name="timeMs"/> 시점 프레임 번호(반복이면 감아서, 아니면 마지막에 멈춤).</summary>
    public static int FrameAt(AnimationClip clip, double timeMs)
    {
        var frames = clip.Frames;
        if (frames.Count == 0) return -1;
        int total = clip.TotalMs;
        if (total <= 0) return 0;
        double t = clip.Loop ? ((timeMs % total) + total) % total : Math.Clamp(timeMs, 0, total);
        double acc = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            acc += frames[i].Ms;
            if (t < acc) return i;
        }
        return frames.Count - 1;
    }

    /// <summary>프레임 시작 시각(ms).</summary>
    public static int FrameStartMs(AnimationClip clip, int index)
    {
        int t = 0;
        for (int i = 0; i < index && i < clip.Frames.Count; i++) t += clip.Frames[i].Ms;
        return t;
    }
}
