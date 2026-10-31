using System.Drawing;

namespace Chickxel.Core.Model;

/// <summary>프로젝트에서 바뀐 부분(여러 개 조합 가능).</summary>
[Flags]
public enum ProjectChange
{
    None = 0,
    /// <summary>셀 픽셀 내용.</summary>
    Pixels = 1,
    /// <summary>레이어 목록·속성(이름·보이기·잠금·불투명도·순서·그룹·부위 크기·위치·피벗).</summary>
    Layers = 2,
    /// <summary>프레임 목록·속성(길이·피벗·이름·순서).</summary>
    Frames = 4,
    Tags = 8,
    /// <summary>캔버스 크기.</summary>
    Canvas = 16,
    /// <summary>Unity·내보내기 설정.</summary>
    Settings = 32,
    /// <summary>파일 경로(다른 이름으로 저장 등).</summary>
    Path = 64,
    All = Pixels | Layers | Frames | Tags | Canvas | Settings | Path,
}

public sealed class ProjectChangedEventArgs(ProjectChange change) : EventArgs
{
    public ProjectChange Change { get; } = change;
}

/// <summary>레이어 종류.</summary>
public enum LayerKind
{
    /// <summary>일반 레이어: 셀이 캔버스 크기이고 (0,0)에 그려진다.</summary>
    Normal,
    /// <summary>그룹(합친 레이어): 셀이 없고, 바로 아래에 붙은 부위 레이어들을 묶는다.</summary>
    Group,
    /// <summary>부위 레이어: 자기 크기의 셀을 프레임마다 갖고, 모든 프레임에 공통인 위치(<see cref="Layer.PartOffset"/>)에 그려진다.</summary>
    Part,
}

/// <summary>
/// 레이어. 순서는 <see cref="SpriteProject.Layers"/>에서 0번이 맨 아래. 그룹의 부위들은 그룹 바로 아래에 이어 붙어 있다.
/// 종류·부모·부위 속성은 <see cref="SpriteProject"/>의 메서드로만 바뀐다(실행취소됨).
/// </summary>
public sealed class Layer
{
    public Layer(string name) => Name = name;

    public string Name { get; set; }
    public bool Visible { get; set; } = true;
    /// <summary>잠긴 레이어에는 그릴 수 없다.</summary>
    public bool Locked { get; set; }
    /// <summary>불투명도 0~255.</summary>
    public byte Opacity { get; set; } = 255;

    public LayerKind Kind { get; internal set; }
    /// <summary>부위 레이어의 그룹. 그 밖에는 null.</summary>
    public Layer? Parent { get; internal set; }
    public bool IsGroup => Kind == LayerKind.Group;
    public bool IsPart => Kind == LayerKind.Part;
    /// <summary>그룹: 타임라인에서 접혀 있는지(파일에 저장, 실행취소·수정됨 표시 없음).</summary>
    public bool Collapsed { get; internal set; }
    /// <summary>타임라인에서 효과 줄(효과마다 한 줄)을 접어 두었는지(파일에 저장, 실행취소·수정됨 표시 없음).</summary>
    public bool EffectsCollapsed { get; internal set; }
    /// <summary>
    /// 그룹: 자동 배치 설정(null = 끔). 켜져 있으면 부위 위치를 <see cref="SpriteProject"/>가 편집마다 다시 계산하고, 부위를 손으로 옮길 수 없다(먼저 끄기).
    /// 바꿀 때는 <see cref="SpriteProject.SetGroupLayout"/>로(실행취소됨).
    /// </summary>
    public GroupLayout? AutoLayout { get; internal set; }
    /// <summary>부위: 그룹이 자동 배치 중이라 위치를 손으로 바꿀 수 없는지.</summary>
    public bool IsAutoLaidOut => IsPart && Parent is { IsGroup: true, AutoLayout: not null };

    /// <summary>부위: 셀 크기(부위 셀은 정확히 이 크기).</summary>
    public Size PartSize { get; internal set; }
    /// <summary>부위: 프레임 캔버스 안 왼쪽 위 위치(모든 프레임 공통, 캔버스 밖으로 나가도 됨).</summary>
    public Point PartOffset { get; internal set; }
    /// <summary>부위: 캔버스 안 영역.</summary>
    public Rectangle PartRect => new(PartOffset, PartSize);
    /// <summary>부위: 부위 아틀라스 내보내기 피벗(부위 기준 0~1, 아래 왼쪽 원점).</summary>
    public Pivot PartPivot { get; internal set; } = Pivot.Center;
    /// <summary>부위: 부위 아틀라스 내보내기 9-slice 테두리(px).</summary>
    public SpriteBorder PartBorder { get; internal set; }
    /// <summary>부위: 가져온 시트에서의 원래 칸(왼쪽 위 원점). "원래 위치 유지" 내보내기에 쓴다. 없으면 null.</summary>
    public Rectangle? PartSourceRect { get; internal set; }

    /// <summary>
    /// 레이어 효과(앞에서부터 차례로 적용, 비파괴 — 셀 픽셀은 그대로). 일반·부위는 그 셀에, 그룹은 그 부위들을 합친 그림에 입힌다.
    /// 바꿀 때는 <see cref="SpriteProject.SetLayerEffects"/>로.
    /// </summary>
    public IReadOnlyList<LayerEffect> Effects { get; internal set; } = [];

    /// <summary>켜진 효과가 하나라도 있는지(어느 프레임이든).</summary>
    public bool HasActiveEffects => Edit.LayerEffects.AnyEnabled(Effects);

    /// <summary>프레임 <paramref name="frameIndex"/>에 입혀질(켜져 있고 프레임 범위 안인) 효과가 있는지.</summary>
    public bool HasActiveEffectsAt(int frameIndex) => Edit.LayerEffects.AnyActive(Effects, frameIndex);

    /// <summary>그룹의 첫 스킨 기본 이름(스킨이 없던 예전 파일·새 그룹).</summary>
    public const string DefaultSkinName = "기본";

    private readonly List<Skin> _skins = new();
    private Skin? _activeSkin;

    /// <summary>
    /// 그룹(스킨 슬롯): 그룹 전체 스킨 목록(적어도 하나, 첫 스킨 = 기본). 같은 그룹의 스킨끼리는 서로 바뀌고(교체형), 그룹끼리는 레이어 순서대로 겹친다(겹치기형).
    /// 부위 하나에만 있는 스킨(<see cref="Skin.Owner"/>)은 빠진다 — 모두 보려면 <see cref="AllSkins"/>, 부위가 고를 수 있는 것은 <see cref="SkinChoices"/>.
    /// 비어 있던 그룹은 처음 볼 때 "기본" 스킨 하나를 만든다. 그룹이 아니면 빈 목록. 바꿀 때는 <see cref="SpriteProject"/>의 스킨 메서드로.
    /// </summary>
    public IReadOnlyList<Skin> Skins => IsGroup ? SkinList.Where(s => s.Owner is null).ToList() : Array.Empty<Skin>();

    /// <summary>그룹: 전체 스킨 + 부위 전용 스킨(저장 순서 — 전체 스킨이 먼저). 그룹이 아니면 빈 목록.</summary>
    public IReadOnlyList<Skin> AllSkins => IsGroup ? SkinList : Array.Empty<Skin>();

    /// <summary>
    /// 고를 수 있는 스킨: 부위면 그룹 전체 스킨 + 이 부위 전용 스킨, 그룹이면 <see cref="Skins"/>. 그 밖은 빈 목록.
    /// </summary>
    public IReadOnlyList<Skin> SkinChoices => IsGroup ? Skins
        : IsPart && Parent is { IsGroup: true } g ? g.SkinList.Where(s => s.Owner is null || ReferenceEquals(s.Owner, this)).ToList() : Array.Empty<Skin>();

    /// <summary>부위: 이 부위 전용 스킨들. 그 밖은 빈 목록.</summary>
    public IReadOnlyList<Skin> OwnSkins => IsPart && Parent is { IsGroup: true } g ? g.SkinList.Where(s => ReferenceEquals(s.Owner, this)).ToList() : Array.Empty<Skin>();

    /// <summary>스킨 목록 원본(그룹이 비어 있으면 "기본"을 만든다). 전체 스킨이 앞, 부위 전용 스킨이 뒤.</summary>
    internal List<Skin> SkinList
    {
        get
        {
            if (_skins.Count == 0) _skins.Add(new Skin(DefaultSkinName));
            return _skins;
        }
    }

    /// <summary>그룹: 지금 보이는(편집·재생·합성하는) 전체 스킨. 그룹이 아니면 null.</summary>
    public Skin? ActiveSkin => IsGroup ? (_activeSkin is { Owner: null } s && _skins.Contains(s) ? s : SkinList[0]) : null;

    /// <summary>그룹: 첫 스킨(기본 — 다른 스킨의 빈 셀이 이 스킨 셀을 물려받음, 늘 전체 스킨). 그룹이 아니면 null.</summary>
    public Skin? BaseSkin => IsGroup ? SkinList[0] : null;

    /// <summary>지금 고른 스킨 값 그대로(목록에 없을 수도 있음, 실행취소 복원용).</summary>
    internal Skin? ActiveSkinRaw => _activeSkin;

    internal void SetActiveSkinRaw(Skin? skin) => _activeSkin = skin;

    private Skin? _skinOverride;

    /// <summary>
    /// 부위: 그룹 스킨 대신 이 부위만 보일 스킨(재정의 — 그룹 전체 스킨이나 이 부위 전용 스킨). null이면 그룹의 지금 스킨을 따른다.
    /// 그룹에 없는 스킨(지운 스킨)·다른 부위 전용 스킨을 가리키면 null. 착장(보기 상태)이라 실행취소 대상이 아니다 — <see cref="SpriteProject.SetPartSkin"/>로 바꾼다.
    /// </summary>
    public Skin? SkinOverride => IsPart && _skinOverride is { } s && Parent is { IsGroup: true } g && g._skins.Contains(s)
                                 && (s.Owner is null || ReferenceEquals(s.Owner, this)) ? s : null;

    /// <summary>
    /// 지금 보이는 스킨: 부위면 재정의 → 그룹의 지금 스킨, 그룹이면 <see cref="ActiveSkin"/>. 그 밖(일반 레이어·그룹 없는 부위)은 null.
    /// </summary>
    public Skin? ShownSkin => IsGroup ? ActiveSkin : IsPart && Parent is { IsGroup: true } g ? SkinOverride ?? g.ActiveSkin : null;

    internal Skin? SkinOverrideRaw => _skinOverride;

    internal void SetSkinOverrideRaw(Skin? skin) => _skinOverride = skin;

    /// <summary>스킨 목록을 통째로 바꾼다(비어 있으면 그대로 두지 않고 다음에 볼 때 "기본"이 생김).</summary>
    internal void ReplaceSkinsRaw(IEnumerable<Skin> skins)
    {
        var list = skins.ToList();
        _skins.Clear();
        _skins.AddRange(list);
    }

    public override string ToString() => Name;
}

/// <summary>
/// 스킨(교체 변형): 그룹(스킨 슬롯) 안에서 이름 붙은 의상·체형 하나. 그룹의 부위 목록·크기·위치·피벗·테두리는 모든 스킨이 같이 쓰고,
/// 셀만 스킨마다 따로다. 첫 스킨이 아닌 스킨의 빈 셀은 첫 스킨 셀을 물려받는다. 이름은 그룹 안에서(부위 전용 스킨까지) 겹치지 않는다.
/// <see cref="Owner"/>가 있으면 그 부위에만 있는 "부위 전용(개별) 스킨"이다(예: Head에만 "모자") — 그 부위의 재정의로만 입히고, 빈 칸은 같은 규칙으로 첫 스킨을 빌린다.
/// </summary>
public sealed class Skin
{
    internal Skin(string name, Layer? owner = null)
    {
        Name = name;
        Owner = owner;
    }

    public string Name { get; internal set; }

    /// <summary>부위 전용 스킨이면 그 부위, 그룹 전체 스킨이면 null.</summary>
    public Layer? Owner { get; internal set; }

    public bool IsPartOnly => Owner is not null;

    public override string ToString() => Name;
}

/// <summary>착장 프리셋 항목: 그룹(슬롯)에 입힐 스킨.</summary>
public readonly record struct OutfitSlot(Layer Group, Skin Skin);

/// <summary>착장 프리셋 항목: 그룹 스킨 대신 부위 하나에 입힐 스킨(부위 재정의).</summary>
public readonly record struct OutfitPart(Layer Part, Skin Skin);

/// <summary>
/// 착장(Outfit) 프리셋: 이름 붙은 "그룹마다 스킨 + 부위 재정의" 묶음. 그룹·부위·스킨을 객체로 가리키므로 이름을 바꿔도 따라가고,
/// 지운 그룹·부위·스킨을 가리키는 항목은 적용·합성할 때 건너뛴다(실행취소로 되살리면 다시 쓰임).
/// 저장할 때 없던 그룹은 그때의 착장을 그대로 쓴다. 이름·내용은 <see cref="SpriteProject"/>의 착장 메서드로만 바뀐다(실행취소됨).
/// </summary>
public sealed class Outfit
{
    internal Outfit(string name, IReadOnlyList<OutfitSlot> slots, IReadOnlyList<OutfitPart> parts)
    {
        Name = name;
        Slots = slots;
        Parts = parts;
    }

    public string Name { get; internal set; }

    /// <summary>그룹마다 입힐 스킨(저장할 때의 그룹 순서).</summary>
    public IReadOnlyList<OutfitSlot> Slots { get; internal set; }

    /// <summary>그룹 스킨 대신 따로 입힐 부위(저장할 때 재정의가 있던 부위만).</summary>
    public IReadOnlyList<OutfitPart> Parts { get; internal set; }

    public override string ToString() => Name;
}

/// <summary>프레임: 길이와 선택적인 피벗·칸 이름 재정의.</summary>
public sealed class Frame
{
    public const int DefaultDurationMs = 100;

    private int _durationMs = DefaultDurationMs;

    /// <summary>표시 시간(ms, 최소 1).</summary>
    public int DurationMs
    {
        get => _durationMs;
        set => _durationMs = Math.Max(1, value);
    }

    /// <summary>이 프레임만의 피벗(캔버스 기준 0~1, 아래 왼쪽 원점). null이면 프로젝트 기본 피벗.</summary>
    public Pivot? Pivot { get; set; }

    /// <summary>내보낼 때 쓸 칸 이름. null이면 이름 규칙(<c>{태그}_{번호}</c> / <c>{파일}_{프레임}</c>).</summary>
    public string? Name { get; set; }
}

/// <summary>태그 재생 방향.</summary>
public enum TagDirection
{
    /// <summary>정방향: From → To.</summary>
    Forward,
    /// <summary>역방향: To → From.</summary>
    Reverse,
    /// <summary>왕복: From → To → From(양 끝은 한 번씩).</summary>
    PingPong,
}

/// <summary>태그(=애니메이션): 이름 + 연속한 프레임 범위 [From, To] + 방향·반복·색.</summary>
public sealed class AnimationTag
{
    public AnimationTag(string name, int from, int to)
    {
        Name = name;
        From = from;
        To = to;
    }

    public string Name { get; set; }
    /// <summary>시작 프레임 번호(포함).</summary>
    public int From { get; set; }
    /// <summary>끝 프레임 번호(포함).</summary>
    public int To { get; set; }
    public TagDirection Direction { get; set; } = TagDirection.Forward;
    public bool Loop { get; set; } = true;
    /// <summary>타임라인 띠 색.</summary>
    public Color32 Color { get; set; } = DefaultColors[0];

    public int Count => To - From + 1;

    public bool Contains(int frameIndex) => frameIndex >= From && frameIndex <= To;

    /// <summary>
    /// 재생 순서의 프레임 번호들. 정방향 From..To, 역방향 To..From, 왕복 From..To..From+1(양 끝 한 번씩).
    /// </summary>
    public List<int> GetPlaybackOrder()
    {
        var order = new List<int>(Count * 2);
        switch (Direction)
        {
            case TagDirection.Reverse:
                for (int i = To; i >= From; i--) order.Add(i);
                break;
            case TagDirection.PingPong:
                for (int i = From; i <= To; i++) order.Add(i);
                for (int i = To - 1; i > From; i--) order.Add(i);
                break;
            default:
                for (int i = From; i <= To; i++) order.Add(i);
                break;
        }
        return order;
    }

    public AnimationTag Clone() => new(Name, From, To) { Direction = Direction, Loop = Loop, Color = Color };

    public override string ToString() => $"{Name} [{From}..{To}]";

    /// <summary>새 태그에 차례로 쓰는 색.</summary>
    public static IReadOnlyList<Color32> DefaultColors { get; } =
    [
        new(0x4F, 0x8E, 0xF7), new(0xF2, 0x8B, 0x3C), new(0x5C, 0xC0, 0x6B), new(0xE0, 0x55, 0x6B),
        new(0xA0, 0x6C, 0xE8), new(0x3C, 0xC4, 0xC4), new(0xE8, 0xC8, 0x3C), new(0x9A, 0x9A, 0x9A),
    ];
}

/// <summary>시트 배치 방식(프레임 시트·부위 아틀라스 공통).</summary>
public enum SheetLayout
{
    /// <summary>격자: <see cref="SheetOptions.Order"/>의 정렬 기준·줄바꿈 단위와 칸 수(<see cref="SheetOptions.Grid"/>)대로 줄·열에 놓는다.</summary>
    Grid,
    /// <summary>자동 배치: 빈틈이 적게 채워 넣는다(최대 가로·넣는 순서를 씀, 넣는 순서의 "목록 순서"는 정렬 기준대로).</summary>
    Packed,
}

/// <summary>격자 칸 수 정하기(격자 배치).</summary>
public enum SheetGrid
{
    /// <summary>자동: 시트의 긴 변(가로·세로 중 큰 값)이 가장 작게, 같으면 넓이(빈 칸)가 가장 작게, 그래도 같으면 가로로 넓게.</summary>
    Auto,
    /// <summary>가로 N칸: 한 줄에 N칸씩(넘치면 다음 줄).</summary>
    Columns,
    /// <summary>세로 N칸: 전체 줄 수가 N 이하가 되는 가장 적은 가로 칸 수(그룹이 N개보다 많으면 줄바꿈 없이).</summary>
    Rows,
}

/// <summary>격자 정렬 기준 하나.</summary>
public enum GridKey
{
    /// <summary>부위(타임라인 위 그룹·위 부위부터). 프레임 시트에는 없는 축이라 건너뛴다.</summary>
    Part,
    /// <summary>스킨(부위 아틀라스 = 그룹 안 스킨 번호, 프레임 시트 = 내보낼 스킨 항목 순서).</summary>
    Skin,
    /// <summary>
    /// 태그(태그 순서, 태그 없는 프레임은 끝). 있으면 프레임 기준은 태그 안 번호가 된다. 프레임 시트는 여러 태그에 든 프레임을 태그마다 넣고,
    /// 부위 아틀라스는 첫 태그에만 넣는다(같은 스프라이트를 두 번 만들지 않음).
    /// </summary>
    Tag,
    /// <summary>프레임 번호(같은 번호는 같은 열 — 빈 칸은 비워 둠).</summary>
    Frame,
}

/// <summary>
/// 격자 정렬·묶음: 정렬 기준(1차, 2차 …) 순서와 줄바꿈 단위(앞에서 몇 개 기준이 같은 칸끼리 한 줄로 묶어 새 줄에서 시작할지, 0 = 줄바꿈 없이 이어서).
/// 줄 안의 자리는 나머지 기준으로 정하고 같은 값은 같은 열(프레임 기준은 프레임 번호 그대로라 빈 프레임 칸은 비움). 위치만 바뀌고 칸(스프라이트) 이름은 그대로다.
/// 프레임 시트에는 부위 축이 없으므로 부위 기준은 건너뛴다(줄바꿈 단위도 남은 기준 기준으로 줄어듦).
/// </summary>
public sealed record GridOrder
{
    public GridOrder(IReadOnlyList<GridKey> keys, int rowBreak)
    {
        Keys = keys;
        RowBreak = rowBreak;
    }

    public IReadOnlyList<GridKey> Keys { get; }

    /// <summary>앞에서 이만큼의 기준이 같은 칸끼리 한 줄(그 묶음마다 새 줄). 0 = 모두 이어서(칸 수로만 줄바꿈).</summary>
    public int RowBreak { get; }

    /// <summary>부위 → 스킨 → 프레임, 부위 × 스킨마다 한 줄(프레임 시트: 스킨 항목마다 한 줄 — 기본값).</summary>
    public static GridOrder ByPart { get; } = new([GridKey.Part, GridKey.Skin, GridKey.Frame], 2);

    /// <summary>부위 → 스킨 → 태그 → 태그 안 프레임, 부위 × 스킨 × 태그마다 한 줄(프레임 시트: 태그마다 한 줄).</summary>
    public static GridOrder ByPartTag { get; } = new([GridKey.Part, GridKey.Skin, GridKey.Tag, GridKey.Frame], 3);

    /// <summary>프레임 → 부위 → 스킨, 프레임마다 한 줄.</summary>
    public static GridOrder ByFrame { get; } = new([GridKey.Frame, GridKey.Part, GridKey.Skin], 1);

    /// <summary>
    /// 바로잡는다: 부위·스킨·프레임이 한 번씩(빠진 것은 끝에), 태그는 있으면 한 번 프레임 바로 앞, 줄바꿈 단위는 0 ~ 기준 수 − 1.
    /// </summary>
    public GridOrder Normalized()
    {
        var keys = new List<GridKey>();
        foreach (var k in Keys)
            if (k != GridKey.Tag && !keys.Contains(k)) keys.Add(k);
        foreach (var k in (GridKey[])[GridKey.Part, GridKey.Skin, GridKey.Frame])
            if (!keys.Contains(k)) keys.Add(k);
        if (Keys.Contains(GridKey.Tag)) keys.Insert(keys.IndexOf(GridKey.Frame), GridKey.Tag);
        var normalized = new GridOrder(keys, Math.Clamp(RowBreak, 0, keys.Count - 1));
        return normalized == this ? this : normalized;
    }

    /// <summary>
    /// 이 시트에 쓰는 기준과 줄바꿈 단위: <paramref name="hasParts"/>가 false(프레임 시트)면 부위 기준을 빼고, 줄바꿈 단위는 앞쪽 기준 중 남은 수.
    /// </summary>
    public (IReadOnlyList<GridKey> Keys, int RowBreak) For(bool hasParts)
    {
        if (hasParts) return (Keys, RowBreak);
        var keys = Keys.Where(k => k != GridKey.Part).ToList();
        return (keys, Keys.Take(RowBreak).Count(k => k != GridKey.Part));
    }

    public bool Equals(GridOrder? other) => other is not null && RowBreak == other.RowBreak && Keys.SequenceEqual(other.Keys);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(RowBreak);
        foreach (var k in Keys) h.Add(k);
        return h.ToHashCode();
    }

    public override string ToString() => Chickxel.Core.Localization.DomainStrings.FormatNaming_Rows(keys: string.Join(" → ", Keys), row: RowBreak);
}

/// <summary>칸(스프라이트) 이름 규칙(프레임 시트·부위 아틀라스 공통).</summary>
public enum SpriteNaming
{
    /// <summary>
    /// 프레임 번호: 프레임 시트 <c>{시트}_{프레임}</c>, 부위 아틀라스 프레임 0 = <c>{부위}</c>, 그다음 <c>{부위}_{프레임}</c>(프레임을 늘려도 이름이 그대로).
    /// </summary>
    FrameNumber,
    /// <summary>태그 기준: 프레임 시트 <c>{태그}_{태그 안 번호}</c>, 부위 아틀라스 <c>{부위}_{태그}_{태그 안 번호}</c>. 태그 없는 프레임은 프레임 번호 규칙.</summary>
    ByTag,
}

/// <summary>내보낼 스킨: 지금 보이는 것만 / 고른 스킨(과 착장 프리셋).</summary>
public enum SkinSource
{
    /// <summary>
    /// 지금 착장만: 프레임 시트는 지금 착장 합성 한 벌, 부위 아틀라스·Sprite Library는 부위마다 지금 보이는 스킨(빌려 보이는 칸의 첫 스킨 스프라이트 포함).
    /// </summary>
    Current,
    /// <summary>
    /// 고른 스킨: 체크한 스킨(<see cref="SheetOptions.ExcludedSkins"/>에 없는 것)과 착장 프리셋(<see cref="SheetOptions.Outfits"/>, 프레임 시트만).
    /// 프레임 시트는 스킨마다 "그 그룹만 그 스킨, 나머지 그룹은 지금 착장" 합성 한 벌씩(스킨마다 따로 파일이 아니면 한 이미지에 모두).
    /// </summary>
    Selected,
}

/// <summary>자동 배치에서 넣는 순서.</summary>
public enum AtlasPackOrder
{
    /// <summary>목록 순서대로(정렬 기준대로).</summary>
    KeepOrder,
    /// <summary>큰 것(넓이)부터.</summary>
    LargestFirst,
}

/// <summary>내보낼 이미지 파일 형식(Unity가 가져올 수 있는 것들).</summary>
public enum ExportImageFormat
{
    /// <summary>PNG(무손실, 투명 유지) — 기본.</summary>
    Png,
    /// <summary>JPG(손실 압축, 투명 없음 — 투명한 곳은 흰색).</summary>
    Jpg,
    /// <summary>TGA(무손실 32비트, 투명 유지).</summary>
    Tga,
    /// <summary>BMP(무손실 32비트, 투명 유지).</summary>
    Bmp,
}

public static class ExportImageFormatExtensions
{
    /// <summary>점을 포함한 파일 확장자(".png" 등).</summary>
    public static string Extension(this ExportImageFormat format) => format switch
    {
        ExportImageFormat.Jpg => ".jpg",
        ExportImageFormat.Tga => ".tga",
        ExportImageFormat.Bmp => ".bmp",
        _ => ".png",
    };
}

/// <summary>
/// 시트 설정 한 벌: 프레임 시트와 부위 아틀라스에 똑같이 적용된다(<see cref="ExportSettings.Common"/>, 부위 아틀라스만 다르게 하려면 <see cref="ExportSettings.PartsOverride"/>).
/// 한쪽에 없는 축(프레임 시트의 부위)은 건너뛴다. 목록 값은 바꿀 때 새 목록으로 바꿔 넣는다.
/// </summary>
public sealed class SheetOptions
{
    private int _gridCount = 4;
    private int _spacing = 1;
    private int _maxWidth;
    private GridOrder _order = GridOrder.ByPart;

    /// <summary>"가로 N칸"·"세로 N칸"의 최대 N.</summary>
    public const int MaxGridCount = 1024;

    /// <summary>배치(격자 / 자동 배치).</summary>
    public SheetLayout Layout { get; set; } = SheetLayout.Grid;

    /// <summary>정렬 기준과 줄바꿈 단위(넣을 때 바로잡음, <see cref="GridOrder.Normalized"/>). 기본 = 부위 → 스킨 → 프레임, 부위 × 스킨마다 한 줄.</summary>
    public GridOrder Order
    {
        get => _order;
        set => _order = value.Normalized();
    }

    /// <summary>격자 칸 수 정하기.</summary>
    public SheetGrid Grid { get; set; } = SheetGrid.Auto;

    /// <summary>"가로 N칸"·"세로 N칸"의 N(1 이상).</summary>
    public int GridCount
    {
        get => _gridCount;
        set => _gridCount = Math.Clamp(value, 1, MaxGridCount);
    }

    /// <summary>칸 사이 간격(px, 새 프로젝트 기본 1 — Unity에서 옆 칸 색이 번지지 않게).</summary>
    public int Spacing
    {
        get => _spacing;
        set => _spacing = Math.Max(0, value);
    }

    /// <summary>칸마다 투명한 테두리를 잘라낸다(피벗은 같은 지점을, 9-slice 테두리는 같은 선을 가리키도록 보정).</summary>
    public bool Trim { get; set; }

    /// <summary>시트 가로·세로를 2의 거듭제곱으로 맞춘다.</summary>
    public bool PowerOfTwo { get; set; }

    /// <summary>자동 배치 최대 가로(px). 0이면 자동(전체 넓이의 제곱근 정도, 가장 넓은 칸 이상).</summary>
    public int MaxWidth
    {
        get => _maxWidth;
        set => _maxWidth = Math.Max(0, value);
    }

    /// <summary>자동 배치에서 넣는 순서.</summary>
    public AtlasPackOrder PackOrder { get; set; } = AtlasPackOrder.KeepOrder;

    /// <summary>태그마다 따로 파일(<c>{시트}_{태그}</c>, 태그 없는 프레임은 <c>{시트}_untagged</c>). 태그가 없거나 프레임 시트 Single 모드면 한 장.</summary>
    public bool SplitTags { get; set; }

    /// <summary>
    /// 스킨마다 따로 파일: 고른 스킨 항목마다(<see cref="SkinSource.Selected"/>) <c>{시트}@{스킨}</c>(첫 스킨 항목·부위 아틀라스 첫 스킨은 원래 이름).
    /// 끄면 한 이미지에 모두(프레임 시트 칸 이름 <c>{이름}@{스킨}</c>). 칸 이름은 파일마다 같다.
    /// </summary>
    public bool SplitSkins { get; set; }

    /// <summary>부위마다 따로 파일(<c>{부위 아틀라스}_{부위}</c>, 부위 아틀라스만 해당).</summary>
    public bool SplitParts { get; set; }

    /// <summary>칸 이름 규칙. 새 프로젝트는 태그 기준.</summary>
    public SpriteNaming Naming { get; set; } = SpriteNaming.ByTag;

    /// <summary>내보낼 스킨(지금 착장만 / 고른 스킨). 기본 = 지금 착장만.</summary>
    public SkinSource Skins { get; set; } = SkinSource.Current;

    /// <summary>고른 스킨에서 뺄 스킨(체크 해제). 비면 부위가 있는 그룹의 모든 스킨.</summary>
    public IReadOnlyList<Skin> ExcludedSkins { get; set; } = [];

    /// <summary>고른 스킨에 더할 착장 프리셋(프레임 시트만 — 착장마다 합성 한 벌, <c>@{착장}</c>). 지운 착장은 건너뛴다.</summary>
    public IReadOnlyList<Outfit> Outfits { get; set; } = [];

    /// <summary>뺄 최상위 레이어(그룹 = 슬롯째, 일반 레이어): 프레임 시트 합성에서 빼고, 부위 아틀라스에서는 그 그룹의 부위를 뺀다.</summary>
    public IReadOnlyList<Layer> ExcludedLayers { get; set; } = [];

    /// <summary>
    /// 첫 스킨이 아닌 스킨이 물려받아 보이는 칸도 그 스킨 이름의 스프라이트(<c>{이름}@{스킨}</c>)로 구워 넣는다(부위 아틀라스 — 프레임 시트 합성은 늘 보이는 그림).
    /// 끄면(기본) 물려받는 칸은 첫 스킨 스프라이트를 같이 쓰고, 첫 스킨을 빼도 그 스프라이트는 남긴다.
    /// </summary>
    public bool BakeInheritedSkins { get; set; }

    public SheetOptions Clone() => (SheetOptions)MemberwiseClone();

    /// <summary>다른 설정의 값을 모두 복사해 온다(목록은 바꿔 넣는 불변 목록이라 그대로 같이 씀).</summary>
    public void CopyFrom(SheetOptions other)
    {
        Layout = other.Layout;
        Order = other.Order;
        Grid = other.Grid;
        GridCount = other.GridCount;
        Spacing = other.Spacing;
        Trim = other.Trim;
        PowerOfTwo = other.PowerOfTwo;
        MaxWidth = other.MaxWidth;
        PackOrder = other.PackOrder;
        SplitTags = other.SplitTags;
        SplitSkins = other.SplitSkins;
        SplitParts = other.SplitParts;
        Naming = other.Naming;
        Skins = other.Skins;
        ExcludedSkins = other.ExcludedSkins;
        Outfits = other.Outfits;
        ExcludedLayers = other.ExcludedLayers;
        BakeInheritedSkins = other.BakeInheritedSkins;
    }
}

/// <summary>
/// 저장 시 이미지 시트(프레임 시트·부위 아틀라스) + Unity 메타 내보내기 설정. 시트 설정은 <see cref="Common"/> 한 벌을 두 결과가 같이 쓰고,
/// <see cref="PartsOverride"/>가 있으면 부위 아틀라스만 그것을 쓴다("시트별 다르게" — 예전 파일을 같은 결과로 옮길 때 등).
/// </summary>
public sealed class ExportSettings
{
    private int _singleFrameIndex;

    /// <summary>프로젝트를 저장할 때 시트도 내보낼지.</summary>
    public bool ExportOnSave { get; set; } = true;

    /// <summary>내보낼 폴더. null이면 프로젝트 파일과 같은 폴더.</summary>
    public string? Folder { get; set; }

    /// <summary>프레임 시트 PNG 이름(확장자 없이). null이면 프로젝트 파일 이름.</summary>
    public string? FileName { get; set; }

    /// <summary>프레임(애니메이션) 시트를 내보낼지.</summary>
    public bool ExportFrames { get; set; } = true;

    /// <summary>
    /// 부위가 있으면 부위 아틀라스(부위 × 프레임마다 스프라이트 하나)도 내보낼지. 새 프로젝트 기본은 꺼짐(내보낼 결과 = 프레임 시트만).
    /// 저장된 파일은 저장된 값(이 값이 없던 예전 파일은 켜짐), PNG를 부위로 가져오면 켜짐.
    /// </summary>
    public bool ExportParts { get; set; }

    /// <summary>부위 아틀라스 PNG 이름(확장자 없이). null이면 <c>{프레임 시트 이름}_parts</c>.</summary>
    public string? PartsFileName { get; set; }

    /// <summary>내보낼 이미지 형식(프레임 시트·부위 아틀라스 공통).</summary>
    public ExportImageFormat ImageFormat { get; set; } = ExportImageFormat.Png;

    private int _jpegQuality = 90;

    /// <summary>JPG 품질 1~100.</summary>
    public int JpegQuality { get => _jpegQuality; set => _jpegQuality = Math.Clamp(value, 1, 100); }

    /// <summary>두 결과가 같이 쓰는 시트 설정.</summary>
    public SheetOptions Common { get; private set; } = new();

    /// <summary>부위 아틀라스만 다른 시트 설정("시트별 다르게"). null이면 <see cref="Common"/>.</summary>
    public SheetOptions? PartsOverride { get; set; }

    /// <summary>프레임 시트가 쓰는 시트 설정(= <see cref="Common"/>).</summary>
    public SheetOptions Frames => Common;

    /// <summary>부위 아틀라스가 쓰는 시트 설정(<see cref="PartsOverride"/> ?? <see cref="Common"/>).</summary>
    public SheetOptions Parts => PartsOverride ?? Common;

    /// <summary>Single 모드에서 내보낼 프레임 번호(프레임 시트만).</summary>
    public int SingleFrameIndex
    {
        get => _singleFrameIndex;
        set => _singleFrameIndex = Math.Max(0, value);
    }

    /// <summary>Unity 메타를 쓸지. null이면 자동(Unity 프로젝트 Assets 안이거나 이미 .meta가 있으면 씀).</summary>
    public bool? WriteMeta { get; set; }

    /// <summary>
    /// 원래 위치 유지(부위 아틀라스 자동 배치): 가져온 칸 안에 아직 들어가는 부위는 원래 위치(왼쪽 위)에 두고, 새로 생겼거나 커진 것만 빈 곳에 배치.
    /// </summary>
    public bool KeepImportedRects { get; set; } = true;

    /// <summary>
    /// 부위 아틀라스 옆에 Unity Sprite Library Asset(<c>.spriteLib</c>, com.unity.2d.animation)을 그룹 × 스킨마다 같이 쓴다
    /// (부위 아틀라스가 켜져 있을 때, 스프라이트를 가리키려고 부위 아틀라스 <c>.meta</c>도 항상 씀).
    /// </summary>
    public bool WriteSpriteLibrary { get; set; }

    public ExportSettings Clone()
    {
        var copy = (ExportSettings)MemberwiseClone();
        copy.Common = Common.Clone();
        copy.PartsOverride = PartsOverride?.Clone();
        return copy;
    }

    /// <summary>다른 설정의 값을 모두 복사해 온다(객체는 그대로).</summary>
    public void CopyFrom(ExportSettings other)
    {
        ExportOnSave = other.ExportOnSave;
        Folder = other.Folder;
        FileName = other.FileName;
        ExportFrames = other.ExportFrames;
        ExportParts = other.ExportParts;
        PartsFileName = other.PartsFileName;
        ImageFormat = other.ImageFormat;
        JpegQuality = other.JpegQuality;
        Common.CopyFrom(other.Common);
        PartsOverride = other.PartsOverride?.Clone();
        SingleFrameIndex = other.SingleFrameIndex;
        WriteMeta = other.WriteMeta;
        KeepImportedRects = other.KeepImportedRects;
        WriteSpriteLibrary = other.WriteSpriteLibrary;
    }
}
