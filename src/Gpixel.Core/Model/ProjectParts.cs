using System.Drawing;

namespace Gpixel.Core.Model;

/// <summary>프로젝트 종류.</summary>
public enum ProjectKind
{
    /// <summary>모든 프레임이 같은 캔버스를 쓰는 애니메이션(프레임·태그).</summary>
    Animation,
    /// <summary>스프라이트마다 크기·피벗이 다른 묶음(예: 캐릭터 부위). 내보내면 한 장의 아틀라스로 자동 배치.</summary>
    Atlas,
}

/// <summary>프로젝트에서 바뀐 부분(여러 개 조합 가능).</summary>
[Flags]
public enum ProjectChange
{
    None = 0,
    /// <summary>셀 픽셀 내용.</summary>
    Pixels = 1,
    /// <summary>레이어 목록·속성(이름·보이기·잠금·불투명도·순서).</summary>
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
    /// <summary>아틀라스 스프라이트 목록·속성(이름·크기·피벗·테두리·순서).</summary>
    Sprites = 128,
    All = Pixels | Layers | Frames | Tags | Canvas | Settings | Path | Sprites,
}

public sealed class ProjectChangedEventArgs(ProjectChange change) : EventArgs
{
    public ProjectChange Change { get; } = change;
}

/// <summary>레이어. 순서는 <see cref="SpriteProject.Layers"/>에서 0번이 맨 아래.</summary>
public sealed class Layer
{
    public Layer(string name) => Name = name;

    public string Name { get; set; }
    public bool Visible { get; set; } = true;
    /// <summary>잠긴 레이어에는 그릴 수 없다.</summary>
    public bool Locked { get; set; }
    /// <summary>불투명도 0~255.</summary>
    public byte Opacity { get; set; } = 255;

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

/// <summary>스프라이트 시트 배치 방식.</summary>
public enum SheetLayout
{
    /// <summary>프레임 순서대로 가로 <see cref="ExportSettings.Columns"/>칸씩(0 = 한 줄).</summary>
    Columns,
    /// <summary>태그마다 한 줄. 태그에 속하지 않은 프레임은 마지막 줄.</summary>
    RowPerTag,
}

/// <summary>
/// 아틀라스 스프라이트: 이름, 자기 크기, 피벗(스프라이트 기준 0~1, 아래 왼쪽 원점), 9-slice 테두리,
/// 가져온 시트에서의 원래 위치. 셀은 <see cref="SpriteProject"/>가 레이어마다 갖는다.
/// 속성은 <see cref="SpriteProject"/>의 Set…/Rename/Resize 메서드로 바꿔야 실행취소된다.
/// </summary>
public sealed class AtlasSprite
{
    public AtlasSprite(string name, int width, int height)
    {
        Name = name;
        Width = width;
        Height = height;
    }

    public string Name { get; internal set; }
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    public Size Size => new(Width, Height);
    public Pivot Pivot { get; internal set; } = Pivot.Center;
    public SpriteBorder Border { get; internal set; }

    /// <summary>가져온 시트에서의 원래 칸(왼쪽 위 원점). "원래 위치 유지" 내보내기에 쓴다. 없으면 null.</summary>
    public Rectangle? SourceRect { get; internal set; }

    public override string ToString() => $"{Name} ({Width}x{Height})";
}

/// <summary>아틀라스 배치 순서.</summary>
public enum AtlasPackOrder
{
    /// <summary>스프라이트 목록 순서대로.</summary>
    KeepOrder,
    /// <summary>큰 것(넓이)부터.</summary>
    LargestFirst,
}

/// <summary>저장 시 PNG 시트 + Unity 메타 내보내기 설정.</summary>
public sealed class ExportSettings
{
    private int _columns;
    private int _spacing;
    private int _singleFrameIndex;
    private int _atlasMaxWidth;

    /// <summary>프로젝트를 저장할 때 시트도 내보낼지.</summary>
    public bool ExportOnSave { get; set; } = true;

    /// <summary>내보낼 폴더. null이면 프로젝트 파일과 같은 폴더.</summary>
    public string? Folder { get; set; }

    /// <summary>내보낼 PNG 이름(확장자 없이). null이면 프로젝트 파일 이름.</summary>
    public string? FileName { get; set; }

    public SheetLayout Layout { get; set; } = SheetLayout.Columns;

    /// <summary>가로 칸 수(<see cref="SheetLayout.Columns"/>일 때). 0이면 모든 프레임을 한 줄로.</summary>
    public int Columns
    {
        get => _columns;
        set => _columns = Math.Max(0, value);
    }

    /// <summary>칸 사이 간격(px).</summary>
    public int Spacing
    {
        get => _spacing;
        set => _spacing = Math.Max(0, value);
    }

    /// <summary>프레임마다 투명한 테두리를 잘라낸다(피벗은 같은 지점을 가리키도록 보정).</summary>
    public bool Trim { get; set; }

    /// <summary>Single 모드에서 내보낼 프레임 번호.</summary>
    public int SingleFrameIndex
    {
        get => _singleFrameIndex;
        set => _singleFrameIndex = Math.Max(0, value);
    }

    /// <summary>Unity 메타를 쓸지. null이면 자동(Unity 프로젝트 Assets 안이거나 이미 .meta가 있으면 씀).</summary>
    public bool? WriteMeta { get; set; }

    /// <summary>아틀라스 최대 가로(px). 0이면 자동(전체 넓이의 제곱근 정도, 가장 넓은 스프라이트 이상).</summary>
    public int AtlasMaxWidth
    {
        get => _atlasMaxWidth;
        set => _atlasMaxWidth = Math.Max(0, value);
    }

    /// <summary>아틀라스 가로·세로를 2의 거듭제곱으로 맞춘다.</summary>
    public bool AtlasPowerOfTwo { get; set; }

    public AtlasPackOrder AtlasOrder { get; set; } = AtlasPackOrder.KeepOrder;

    /// <summary>
    /// 원래 위치 유지: 가져온 칸 안에 아직 들어가는 스프라이트는 원래 위치(왼쪽 위)에 두고, 새로 생겼거나 커진 것만 빈 곳에 배치.
    /// </summary>
    public bool KeepImportedRects { get; set; } = true;

    public ExportSettings Clone() => (ExportSettings)MemberwiseClone();

    /// <summary>다른 설정의 값을 모두 복사해 온다(객체는 그대로).</summary>
    public void CopyFrom(ExportSettings other)
    {
        ExportOnSave = other.ExportOnSave;
        Folder = other.Folder;
        FileName = other.FileName;
        Layout = other.Layout;
        Columns = other.Columns;
        Spacing = other.Spacing;
        Trim = other.Trim;
        SingleFrameIndex = other.SingleFrameIndex;
        WriteMeta = other.WriteMeta;
        AtlasMaxWidth = other.AtlasMaxWidth;
        AtlasPowerOfTwo = other.AtlasPowerOfTwo;
        AtlasOrder = other.AtlasOrder;
        KeepImportedRects = other.KeepImportedRects;
    }
}
