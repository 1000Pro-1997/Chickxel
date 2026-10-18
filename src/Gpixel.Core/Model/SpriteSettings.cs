namespace Gpixel.Core.Model;

/// <summary>Unity 스프라이트 모드.</summary>
public enum SpriteMode
{
    /// <summary>이미지 전체가 스프라이트 1개(메타 spriteMode 1).</summary>
    Single = 1,
    /// <summary>잘린 칸마다 스프라이트(메타 spriteMode 2).</summary>
    Multiple = 2,
}

/// <summary>Unity 가져오기 설정 중 편집기에서 다루는 값들.</summary>
public sealed class SpriteSettings
{
    public const float DefaultPixelsPerUnit = 16f;

    private float _ppu = DefaultPixelsPerUnit;

    public SpriteMode Mode { get; set; } = SpriteMode.Single;

    /// <summary>Pixels Per Unit(0보다 큼).</summary>
    public float PixelsPerUnit
    {
        get => _ppu;
        set => _ppu = value > 0 && float.IsFinite(value) ? value : DefaultPixelsPerUnit;
    }

    /// <summary>Single 모드 피벗이자 새 칸의 기본 피벗.</summary>
    public Pivot DefaultPivot { get; set; } = Pivot.Center;

    /// <summary>Single 모드 9-slice 테두리.</summary>
    public SpriteBorder Border { get; set; }

    /// <summary>메시 Full Rect(true, spriteMeshType 0) / Tight(false, 1).</summary>
    public bool FullRectMesh { get; set; } = true;

    /// <summary>Read/Write 사용(isReadable).</summary>
    public bool Readable { get; set; }

    public SpriteSettings Clone() => (SpriteSettings)MemberwiseClone();

    /// <summary>다른 설정의 값을 모두 복사해 온다(객체는 그대로).</summary>
    public void CopyFrom(SpriteSettings other)
    {
        Mode = other.Mode;
        PixelsPerUnit = other.PixelsPerUnit;
        DefaultPivot = other.DefaultPivot;
        Border = other.Border;
        FullRectMesh = other.FullRectMesh;
        Readable = other.Readable;
    }
}

/// <summary>격자 잘라내기 설정(사이드카 <c>grid</c>). 캔버스 칸 격자 표시에도 쓴다.</summary>
public sealed class GridSettings
{
    public int CellW { get; set; } = 16;
    public int CellH { get; set; } = 16;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int PadX { get; set; }
    public int PadY { get; set; }

    public GridSettings Clone() => (GridSettings)MemberwiseClone();
}
