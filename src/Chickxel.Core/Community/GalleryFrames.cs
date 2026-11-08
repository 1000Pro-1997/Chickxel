using Chickxel.Core.Model;

namespace Chickxel.Core.Community;

/// <summary>갤러리에 GIF로 올릴 프레임 범위.</summary>
public enum GalleryFrameMode
{
    /// <summary>모든 프레임을 순서대로.</summary>
    All,
    /// <summary>태그 하나를 그 재생 순서대로(정방향·역방향·왕복 — GIF 내보내기의 태그와 같음).</summary>
    Tag,
    /// <summary>프레임 번호 범위(1부터, 시작이 끝보다 크면 거꾸로 재생).</summary>
    Range,
    /// <summary>타임라인에서 고른 프레임들(번호 순서).</summary>
    Selected,
}

/// <summary>갤러리에 올릴 GIF의 프레임 순서를 고른다(그림은 만들지 않음 — <see cref="IO.GifWriter.ProjectFramesAt"/>가 이 순서로 합성).</summary>
public static class GalleryFrames
{
    /// <summary>
    /// <paramref name="mode"/>에 맞는 프레임 번호(0부터) 순서. 프로젝트에 없는 번호는 빼고, 고를 것이 없으면(태그·고른 프레임 없음) 빈 목록.
    /// </summary>
    /// <param name="tag"><see cref="GalleryFrameMode.Tag"/>의 태그(재생 순서 그대로, 왕복이면 끝 프레임을 한 번만 지나 되돌아옴).</param>
    /// <param name="from"><see cref="GalleryFrameMode.Range"/>의 시작 번호(1부터, 프로젝트 범위로 맞춤).</param>
    /// <param name="to"><see cref="GalleryFrameMode.Range"/>의 끝 번호(1부터, 프로젝트 범위로 맞춤). 시작보다 작으면 시작부터 거꾸로.</param>
    /// <param name="selected"><see cref="GalleryFrameMode.Selected"/>의 프레임 번호들(0부터, 겹친 번호는 한 번, 작은 번호부터).</param>
    public static List<int> Select(SpriteProject project, GalleryFrameMode mode, AnimationTag? tag = null, int from = 1, int to = int.MaxValue,
        IEnumerable<int>? selected = null)
    {
        int count = project.Frames.Count;
        if (count == 0) return [];
        switch (mode)
        {
            case GalleryFrameMode.Tag:
                return tag is null ? [] : tag.GetPlaybackOrder().Where(i => i >= 0 && i < count).ToList();
            case GalleryFrameMode.Range:
            {
                int a = Math.Clamp(from, 1, count) - 1, b = Math.Clamp(to, 1, count) - 1;
                var order = new List<int>(Math.Abs(b - a) + 1);
                int step = a <= b ? 1 : -1;
                for (int i = a; ; i += step)
                {
                    order.Add(i);
                    if (i == b) break;
                }
                return order;
            }
            case GalleryFrameMode.Selected:
                return selected is null ? [] : selected.Where(i => i >= 0 && i < count).Distinct().Order().ToList();
            default:
                return Enumerable.Range(0, count).ToList();
        }
    }
}
