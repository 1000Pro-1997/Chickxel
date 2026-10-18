using Gpixel.Core.Model;

namespace Gpixel.Core.Play;

/// <summary>재생할 프레임 하나: 합성된 이미지(캔버스 크기), 캔버스 기준 피벗, 표시 시간.</summary>
public sealed record PlaybackFrame(int FrameIndex, PixelImage Image, Pivot Pivot, int Ms);

/// <summary>
/// 프로젝트 태그 → 재생 프레임 목록(방향 반영). 움직임 테스트·미리보기에서 쓴다.
/// 컨트롤러의 <c>SelectAnimation(hasClip)</c>에는 <see cref="SpriteProject.HasTag"/>를 넘기면 된다.
/// </summary>
public static class ProjectPlayback
{
    /// <summary>태그를 재생 순서(정방향·역방향·왕복)대로 펼친다. 같은 프레임은 한 번만 합성한다.</summary>
    public static List<PlaybackFrame> BuildFrames(SpriteProject project, AnimationTag tag)
        => Build(project, tag.GetPlaybackOrder().Where(i => i >= 0 && i < project.Frames.Count));

    /// <summary>이름으로 태그를 찾아 펼친다. 없으면 빈 목록.</summary>
    public static List<PlaybackFrame> BuildFrames(SpriteProject project, string tagName)
        => project.FindTag(tagName) is { } tag ? BuildFrames(project, tag) : [];

    /// <summary>모든 프레임을 순서대로.</summary>
    public static List<PlaybackFrame> BuildAllFrames(SpriteProject project)
        => Build(project, Enumerable.Range(0, project.Frames.Count));

    private static List<PlaybackFrame> Build(SpriteProject project, IEnumerable<int> order)
    {
        var cache = new Dictionary<int, PixelImage>();
        var list = new List<PlaybackFrame>();
        foreach (var i in order)
        {
            if (!cache.TryGetValue(i, out var image)) cache[i] = image = project.Flatten(i);
            list.Add(new PlaybackFrame(i, image, project.GetFramePivot(i), project.Frames[i].DurationMs));
        }
        return list;
    }

    /// <summary>시점(ms)의 목록 번호. 반복이면 감고, 아니면 마지막에서 멈춘다. 비었으면 -1.</summary>
    public static int FrameAt(IReadOnlyList<PlaybackFrame> frames, double timeMs, bool loop)
    {
        if (frames.Count == 0) return -1;
        int total = frames.Sum(f => f.Ms);
        if (total <= 0) return 0;
        double t = loop ? ((timeMs % total) + total) % total : Math.Clamp(timeMs, 0, total);
        double acc = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            acc += frames[i].Ms;
            if (t < acc) return i;
        }
        return frames.Count - 1;
    }
}
