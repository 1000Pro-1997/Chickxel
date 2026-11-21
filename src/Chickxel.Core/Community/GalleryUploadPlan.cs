namespace Chickxel.Core.Community;

/// <summary>
/// 갤러리에 올릴 목록의 줄 하나가 만들 그림: 형식과 프레임 번호(0부터) 순서(PNG는 한 장, GIF는 <see cref="GalleryFrames.Select"/>로 고른 순서).
/// </summary>
public sealed record GalleryImageSource(CommunityArtType Type, IReadOnlyList<int> Frames)
{
    public static GalleryImageSource Png(int frame) => new(CommunityArtType.Png, [frame]);

    public static GalleryImageSource Gif(IReadOnlyList<int> frames) => new(CommunityArtType.Gif, frames);
}

/// <summary>
/// 올릴 목록에서 같은 그림이 될 줄을 찾는다: 형식이 같고 프레임 순서가 똑같으면 같은 그림(같은 프레임의 PNG 둘, 태그 "idle" = 1–4와 프레임 1–4의 GIF 둘 —
/// 배율·속도는 목록 전체가 같이 쓰므로 결과 파일도 같다). 같은 그림이면 앞 줄만 올리고 뒤 줄은 뺀다. 프레임이 없는 줄은 비교하지 않는다(올릴 수 없는 줄).
/// </summary>
public sealed class GalleryUploadPlan
{
    private GalleryUploadPlan(IReadOnlyList<int?> sameAs, IReadOnlyList<int> unique)
    {
        SameAs = sameAs;
        Unique = unique;
    }

    /// <summary>줄마다: 같은 그림인 앞 줄 번호(0부터), 처음 나온 그림이면 null.</summary>
    public IReadOnlyList<int?> SameAs { get; }

    /// <summary>올릴 줄 번호(0부터, 목록 순서 — 첫 줄 = 대표 그림).</summary>
    public IReadOnlyList<int> Unique { get; }

    /// <summary>빼는(앞 줄과 같은) 줄 수.</summary>
    public int DuplicateCount => SameAs.Count - Unique.Count;

    public static GalleryUploadPlan Create(IReadOnlyList<GalleryImageSource> items)
    {
        var sameAs = new int?[items.Count];
        var unique = new List<int>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Frames.Count > 0)
                foreach (int u in unique)
                    if (items[u].Type == item.Type && items[u].Frames.SequenceEqual(item.Frames))
                    {
                        sameAs[i] = u;
                        break;
                    }
            if (sameAs[i] is null) unique.Add(i);
        }
        return new GalleryUploadPlan(sameAs, unique);
    }
}
