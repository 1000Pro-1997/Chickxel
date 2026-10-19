using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.Edit;

namespace Gpixel.App;

/// <summary>창 배치·최근 목록·보기 옵션 등 사용자 설정(<c>&lt;설정 폴더&gt;/Gpixel/settings.json</c>, <see cref="Platform.AppDataDirectory"/>).</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public List<string> RecentFolders { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
    public string? LastFolder { get; set; }
    public bool IncludeSubfolders { get; set; } = true;

    // 창 배치
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 900;
    public bool Maximized { get; set; }
    public bool FilesVisible { get; set; } = true;
    public int LeftWidth { get; set; } = 230;
    public int PaletteWidth { get; set; } = 214;
    public int RightWidth { get; set; } = 300;
    public int BottomHeight { get; set; } = 280;
    public int BrowserTreeHeight { get; set; } = 180;
    public int PreviewHeight { get; set; } = 230;

    // 보기
    public int Zoom { get; set; } = 8;
    public bool ShowPixelGrid { get; set; } = true;
    public bool ShowPivot { get; set; } = true;
    public bool ShowTileGrid { get; set; }
    public bool TiledMode { get; set; }
    public int TileWidth { get; set; } = 16;
    public int TileHeight { get; set; } = 16;
    public string PixelGridColor { get; set; } = "#FFFFFF40";
    public string TileGridColor { get; set; } = "#5AC8FF96";
    public bool OnionSkin { get; set; }

    // 도구
    public int BrushSize { get; set; } = 1;
    public BrushShape BrushShape { get; set; } = BrushShape.Square;
    /// <summary>채우기 허용 오차(0~100%).</summary>
    public int FillTolerance { get; set; }
    public bool FillDiagonal { get; set; }
    /// <summary>채우기 기준: 보이는 레이어 전체(false = 현재 레이어).</summary>
    public bool FillSampleVisible { get; set; }
    /// <summary>스포이드 기준: 보이는 레이어 전체(false = 현재 레이어).</summary>
    public bool PickerSampleVisible { get; set; } = true;
    /// <summary>밝게/어둡게 세기(%).</summary>
    public int DodgeStrength { get; set; } = EditorContext.DefaultDodgeStrength;
    public DitherPattern DitherPattern { get; set; } = DitherPattern.Checker;
    public bool PixelPerfect { get; set; }
    public bool FillContiguous { get; set; } = true;
    public bool ShapeFilled { get; set; }
    public bool RightButtonErases { get; set; }
    public bool MoveWrap { get; set; } = true;
    /// <summary>변형 모드(Ctrl+T) 리샘플링(기본 RotSprite).</summary>
    public ResampleMode TransformResample { get; set; } = ResampleMode.RotSprite;
    /// <summary>그라데이션 정지점("위치(0~1):#RRGGBBAA", 2개 이상). null = 기본(검정 → 흰색).</summary>
    public List<string>? GradientStops { get; set; }
    /// <summary>그라데이션 불투명도 정지점("위치(0~1):불투명도(0~255)", 2개 이상). null = 기본(100% → 100%).</summary>
    public List<string>? GradientAlphaStops { get; set; }
    public GradientInterpolation GradientInterpolation { get; set; } = GradientInterpolation.Smooth;
    public GradientDither GradientDither { get; set; } = GradientDither.None;
    public string PrimaryColor { get; set; } = "#000000";
    /// <summary>이미지를 열 때마다 캔버스를 화면에 맞춘다(위 막대 "맞추기" 토글).</summary>
    public bool AutoFit { get; set; } = true;
    public string SecondaryColor { get; set; } = "#00000000";
    public List<string> RecentColors { get; set; } = new();
    public List<string>? Palette { get; set; }

    // 프로젝트·미리보기
    public float LastPpu { get; set; } = 16f;
    public string PreviewBackground { get; set; } = "checker";
    public int PreviewZoom { get; set; } = 0;
    public int PreviewFps { get; set; } = 0;

    // Claude 채팅
    /// <summary>Claude 모델 별칭(null = Claude Code 기본값, "sonnet"/"opus"/"haiku").</summary>
    public string? AiModel { get; set; }
    /// <summary>claude 실행 파일 경로를 직접 정함(null = 자동으로 찾음).</summary>
    public string? ClaudePath { get; set; }
    public int ChatX { get; set; } = -1;
    public int ChatY { get; set; } = -1;
    public int ChatWidth { get; set; } = 420;
    public int ChatHeight { get; set; } = 640;
    public bool ChatOpen { get; set; }

    // 내보내기 설정 창(Ctrl+E)
    public int ExportX { get; set; } = -1;
    public int ExportY { get; set; } = -1;
    public int ExportWidth { get; set; } = 1100;
    public int ExportHeight { get; set; } = 760;
    /// <summary>왼쪽 설정 칸 너비 비율(0~1, 나머지는 오른쪽 미리보기).</summary>
    public double ExportSplit { get; set; } = 0.46;
    /// <summary>접어 둔 설정 묶음 이름(설정 프리셋·공통·파일·프레임 시트·부위 아틀라스·고급).</summary>
    public List<string> ExportCollapsed { get; set; } = new();

    // 편집·파일 브라우저(환경 설정 창)
    /// <summary>실행취소 단계 수(1~10000).</summary>
    public int UndoSteps { get; set; } = UndoStack.DefaultCapacity;
    /// <summary>앱 휴지통 보관 기간(일). 지나면 영구 삭제.</summary>
    public int TrashDays { get; set; } = 7;
    /// <summary>사용자 단축키: 명령 Id("분류/이름") → 키 표기 목록(빈 목록 = 단축키 없음). 기본값과 다른 명령만 담는다.</summary>
    public Dictionary<string, List<string>>? Shortcuts { get; set; }

    /// <summary>처음 실행할 때 쓰는 기본 팔레트(DawnBringer 32).</summary>
    public static readonly string[] DefaultPalette =
    [
        "000000", "222034", "45283C", "663931", "8F563B", "DF7126", "D9A066", "EEC39A",
        "FBF236", "99E550", "6ABE30", "37946E", "4B692F", "524B24", "323C39", "3F3F74",
        "306082", "5B6EE1", "639BFF", "5FCDE4", "CBDBFC", "FFFFFF", "9BADB7", "847E87",
        "696A6A", "595652", "76428A", "AC3232", "D95763", "D77BBA", "8F974A", "8A6F30",
    ];

    [JsonIgnore]
    public static string FilePath => Path.Combine(Platform.AppDataDirectory, "Gpixel", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>설정 파일을 읽는다. 없거나 깨졌으면 기본값.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new AppSettings();
    }

    /// <summary>설정 파일을 쓴다. 실패하면 false(설정 저장 실패는 작업을 막지 않는다).</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void PushRecent(List<string> list, string path)
    {
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxRecent) list.RemoveRange(MaxRecent, list.Count - MaxRecent);
    }
}
