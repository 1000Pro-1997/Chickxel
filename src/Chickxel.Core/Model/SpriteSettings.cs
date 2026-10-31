namespace Chickxel.Core.Model;

/// <summary>Unity 스프라이트 모드.</summary>
public enum SpriteMode
{
    /// <summary>이미지 전체가 스프라이트 1개(메타 spriteMode 1).</summary>
    Single = 1,
    /// <summary>잘린 칸마다 스프라이트(메타 spriteMode 2).</summary>
    Multiple = 2,
}

/// <summary>Unity Alpha Source(alphaUsage).</summary>
public enum SpriteAlphaSource
{
    None = 0,
    InputTextureAlpha = 1,
    FromGrayScale = 2,
}

/// <summary>Unity Wrap Mode(wrapU/V/W).</summary>
public enum TextureWrap
{
    Repeat = 0,
    Clamp = 1,
    Mirror = 2,
    MirrorOnce = 3,
}

/// <summary>Unity Filter Mode.</summary>
public enum TextureFilter
{
    Point = 0,
    Bilinear = 1,
    Trilinear = 2,
}

/// <summary>Unity Resize Algorithm(플랫폼 설정).</summary>
public enum TextureResize
{
    Mitchell = 0,
    Bilinear = 1,
}

/// <summary>Unity Format(플랫폼 설정, textureFormat). Automatic = -1.</summary>
public enum TextureFormatChoice
{
    Automatic = -1,
    RGB24 = 3,
    RGBA32 = 4,
}

/// <summary>Unity Compression(플랫폼 설정, textureCompression).</summary>
public enum TextureCompressionChoice
{
    None = 0,
    NormalQuality = 1,
    HighQuality = 2,
    LowQuality = 3,
}

/// <summary>
/// Unity 텍스처 가져오기 설정(Texture Type = Sprite (2D and UI)). 인스펙터와 같은 항목을 .meta에 쓴다.
/// 기본값은 픽셀 아트용(Point, 압축 없음, 밉맵 끔, Clamp).
/// </summary>
public sealed class SpriteSettings
{
    public const float DefaultPixelsPerUnit = 16f;

    /// <summary>Max Size에서 고를 수 있는 값(0 = 자동: 이미지가 들어가는 가장 작은 2의 거듭제곱).</summary>
    public static readonly int[] MaxTextureSizes = [32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384];

    private float _ppu = DefaultPixelsPerUnit;
    private int _extrude = 1, _aniso = 1, _maxSize, _quality = 50;

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

    /// <summary>Extrude Edges 0~32(spriteExtrude, Tight 메시에서 쓰임).</summary>
    public int ExtrudeEdges { get => _extrude; set => _extrude = Math.Clamp(value, 0, 32); }

    /// <summary>Generate Physics Shape(spriteGenerateFallbackPhysicsShape).</summary>
    public bool GeneratePhysicsShape { get; set; } = true;

    /// <summary>sRGB (Color Texture).</summary>
    public bool SRgb { get; set; } = true;

    public SpriteAlphaSource AlphaSource { get; set; } = SpriteAlphaSource.InputTextureAlpha;

    public bool AlphaIsTransparency { get; set; } = true;

    public bool GenerateMipMaps { get; set; }

    public TextureWrap WrapMode { get; set; } = TextureWrap.Clamp;

    public TextureFilter FilterMode { get; set; } = TextureFilter.Point;

    /// <summary>Aniso Level 0~16.</summary>
    public int AnisoLevel { get => _aniso; set => _aniso = Math.Clamp(value, 0, 16); }

    /// <summary>Max Size: 0 = 자동, 아니면 <see cref="MaxTextureSizes"/> 중 하나(가까운 값으로 맞춤).</summary>
    public int MaxTextureSize
    {
        get => _maxSize;
        set => _maxSize = value <= 0 ? 0 : MaxTextureSizes.MinBy(s => Math.Abs(s - value));
    }

    public TextureResize ResizeAlgorithm { get; set; } = TextureResize.Mitchell;

    public TextureFormatChoice Format { get; set; } = TextureFormatChoice.Automatic;

    public TextureCompressionChoice Compression { get; set; } = TextureCompressionChoice.None;

    /// <summary>Use Crunch Compression.</summary>
    public bool CrunchCompression { get; set; }

    /// <summary>Compressor Quality 0~100(crunch).</summary>
    public int CompressionQuality { get => _quality; set => _quality = Math.Clamp(value, 0, 100); }

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
        ExtrudeEdges = other.ExtrudeEdges;
        GeneratePhysicsShape = other.GeneratePhysicsShape;
        SRgb = other.SRgb;
        AlphaSource = other.AlphaSource;
        AlphaIsTransparency = other.AlphaIsTransparency;
        GenerateMipMaps = other.GenerateMipMaps;
        WrapMode = other.WrapMode;
        FilterMode = other.FilterMode;
        AnisoLevel = other.AnisoLevel;
        MaxTextureSize = other.MaxTextureSize;
        ResizeAlgorithm = other.ResizeAlgorithm;
        Format = other.Format;
        Compression = other.Compression;
        CrunchCompression = other.CrunchCompression;
        CompressionQuality = other.CompressionQuality;
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
