using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>창 배치·최근 목록·보기 옵션 등 사용자 설정(<c>&lt;설정 폴더&gt;/Gpixel/settings.json</c>, <see cref="Platform.AppDataDirectory"/>).</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public List<string> RecentFolders { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
    public string? LastFolder { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    /// <summary>화면 언어(문화권 이름, 예: "ko"). null = OS 언어. 그 언어 번역이 없으면 기본(한국어) 문구.</summary>
    public string? Language { get; set; }

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
    /// <summary>픽셀 격자를 보이기 시작하는 배율.</summary>
    public int PixelGridMinZoom { get; set; } = 6;
    /// <summary>타일 모드 반복 겹 수(0 = 자동).</summary>
    public int TileRepeat { get; set; }
    public bool TileDim { get; set; } = true;
    public int OnionBefore { get; set; } = 1;
    public int OnionAfter { get; set; } = 1;
    /// <summary>양파 껍질 불투명도(%).</summary>
    public int OnionOpacity { get; set; } = 30;
    public bool OnionTint { get; set; } = true;

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
    /// <summary>색 고르기 방식(사각형·벌집·색상환·슬라이더).</summary>
    public ColorPickerMode ColorPickerMode { get; set; } = ColorPickerMode.Square;
    /// <summary>팔레트 열의 팔레트들(위 → 아래). null = 처음 실행(기본 팔레트 하나).</summary>
    public List<PaletteSetting>? Palettes { get; set; }
    /// <summary>예전 버전의 팔레트 하나(읽기만 — <see cref="Load"/>가 <see cref="Palettes"/> 첫 항목으로 옮기고 다시 쓰지 않음).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Palette { get; set; }

    // 프로젝트·미리보기
    public float LastPpu { get; set; } = 16f;
    public string PreviewBackground { get; set; } = "checker";
    public int PreviewZoom { get; set; } = 0;
    public int PreviewFps { get; set; } = 0;

    // AI 채팅(Claude Code·Codex)
    /// <summary>채팅에 쓰는 CLI("claude" / "codex", 모르는 값 = claude).</summary>
    public string ChatProvider { get; set; } = "claude";
    /// <summary>채팅의 "지금 레이어만": 보낼 때의 문서·현재 레이어(그룹이면 그 부위들)만 AI가 고침(나머지는 보기만). 두 탭이 같이 씀.</summary>
    public bool AiCurrentLayerOnly { get; set; }
    /// <summary>Claude 모델 별칭(null = Claude Code 기본값, "opus"/"sonnet"/"haiku").</summary>
    public string? AiModel { get; set; }
    /// <summary>claude 실행 파일 경로를 직접 정함(null = 자동으로 찾음).</summary>
    public string? ClaudePath { get; set; }
    /// <summary>Claude Code가 알려 준 실제 모델 ID: 보낸 별칭(기본값은 "default:" + Claude Code 설정의 모델) → ID. 목록에 버전을 보이는 데 쓴다.</summary>
    public Dictionary<string, string>? ClaudeResolvedModels { get; set; }
    /// <summary>Codex 모델(null = Codex 설정의 기본값, 예: "gpt-6-astra").</summary>
    public string? CodexModel { get; set; }
    /// <summary>codex 실행 파일 경로를 직접 정함(null = 자동으로 찾음).</summary>
    public string? CodexPath { get; set; }
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

    // GIF 애니메이션 내보내기(마지막으로 쓴 값, 앱 전체)
    /// <summary>재생 횟수(0 = 무한 반복).</summary>
    public int GifPlayCount { get; set; }
    /// <summary>배경색 위에 합성(꺼짐 = 투명 배경).</summary>
    public bool GifUseBackground { get; set; }
    /// <summary>배경색(#RRGGBB, 투명으로 돌려도 기억).</summary>
    public string GifBackgroundColor { get; set; } = "#FFFFFF";

    // 편집·파일 브라우저(환경 설정 창)
    /// <summary>실행취소 단계 수(1~10000).</summary>
    public int UndoSteps { get; set; } = UndoStack.DefaultCapacity;
    /// <summary>앱 휴지통 보관 기간(일). 0 = 제한 없음(기본). 지나면 영구 삭제.</summary>
    public int TrashKeepDays { get; set; } = TrashPolicy.Default.Days;
    /// <summary>앱 휴지통 최대 용량(MB). 0 = 제한 없음. 넘치면 오래된 항목부터 영구 삭제(기본 1GB).</summary>
    public int TrashMaxMegabytes { get; set; } = TrashPolicy.Default.MaxMegabytes;

    [JsonIgnore]
    public TrashPolicy TrashPolicy
    {
        get => new(Math.Max(0, TrashKeepDays), Math.Max(0, TrashMaxMegabytes));
        set { TrashKeepDays = value.Days; TrashMaxMegabytes = value.MaxMegabytes; }
    }
    /// <summary>파일을 닫아도 실행취소 기록을 앱 설정 폴더에 남긴다(<see cref="UndoHistoryKeeper"/>). 끄면 새로 쓰지도 읽지도 않는다.</summary>
    public bool UndoHistoryEnabled { get; set; } = true;
    /// <summary>남긴 실행취소 기록 보관 기간(일, 마지막으로 쓰거나 연 뒤로). 0 = 제한 없음.</summary>
    public int UndoHistoryKeepDays { get; set; } = UndoHistoryPolicy.DefaultDays;
    /// <summary>남긴 실행취소 기록 전체 최대 용량(MB). 넘치면 오래 안 쓴 기록부터 지운다.</summary>
    public int UndoHistoryMaxMegabytes { get; set; } = UndoHistoryPolicy.DefaultMaxMegabytes;

    [JsonIgnore]
    public UndoHistoryPolicy UndoHistoryPolicy
    {
        get => new(Math.Max(0, UndoHistoryKeepDays), Math.Max(0, UndoHistoryMaxMegabytes));
        set { UndoHistoryKeepDays = value.Days; UndoHistoryMaxMegabytes = value.MaxMegabytes; }
    }
    /// <summary>사용자 단축키: 명령 Id("분류/이름") → 키 표기 목록(빈 목록 = 단축키 없음). 기본값과 다른 명령만 담는다.</summary>
    public Dictionary<string, List<string>>? Shortcuts { get; set; }

    // 저장(환경 설정 창)
    /// <summary>자동 저장: 편집이 <see cref="AutoSaveIdleSeconds"/>초 동안 없으면 지금 문서를 Ctrl+S처럼 저장(파일이 있는 문서만).</summary>
    public bool AutoSave { get; set; } = true;
    /// <summary>
    /// 자동 저장까지 기다리는 시간(초, <see cref="MinAutoSaveDelay"/>~<see cref="MaxAutoSaveDelay"/>). 기본값을 10초에서 3초로 바꾸면서
    /// 설정 이름도 바꿨다(예전 "AutoSaveDelaySeconds"에 저장된 옛 기본값 10은 읽지 않음).
    /// </summary>
    public int AutoSaveIdleSeconds { get; set; } = DefaultAutoSaveDelay;
    public const int DefaultAutoSaveDelay = 3, MinAutoSaveDelay = 1, MaxAutoSaveDelay = 600;

    [JsonIgnore]
    public TimeSpan AutoSaveDelay => TimeSpan.FromSeconds(Math.Clamp(AutoSaveIdleSeconds, MinAutoSaveDelay, MaxAutoSaveDelay));

    // 업데이트
    /// <summary>시작할 때마다 새 버전을 자동으로 확인(배포판만, 새 버전이면 업데이트 창). 도움말 > 업데이트 확인은 이 설정과 관계없이 확인한다.</summary>
    public bool UpdateCheck { get; set; } = true;
    /// <summary>"다음 업데이트까지 띄우지 않기"로 고른 버전(자동 확인에서 알리지 않음). null = 없음.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>처음 실행할 때 쓰는 기본 팔레트(DawnBringer 32).</summary>
    public const string DefaultPaletteName = "DawnBringer 32";

    /// <inheritdoc cref="DefaultPaletteName"/>
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
                return Migrate(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new AppSettings();
    }

    /// <summary>
    /// 예전 형식을 옮긴다: 팔레트 하나(<see cref="Palette"/>) → <see cref="Palettes"/> 첫 항목 "팔레트 1",
    /// 단축키 명령 "AI/Claude 채팅" → "AI/AI 채팅"(Codex가 더해져 이름이 바뀜).
    /// </summary>
    private static AppSettings Migrate(AppSettings s)
    {
        if (s.Palette is { } old)
        {
            s.Palettes ??= [new PaletteSetting { Name = "팔레트 1", Colors = old }];
            s.Palette = null;
        }
        if (s.Shortcuts is { } keys && keys.Remove("AI/Claude 채팅", out var chat)) keys.TryAdd("AI/AI 채팅", chat);
        return s;
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

/// <summary>설정 파일에 저장하는 팔레트 하나(이름, "#RRGGBB" 색 목록, 접힘).</summary>
public sealed class PaletteSetting
{
    public string Name { get; set; } = "";
    public List<string> Colors { get; set; } = new();
    public bool Collapsed { get; set; }

    public static PaletteSetting From(NamedPalette p)
        => new() { Name = p.Name, Colors = [.. p.Colors.Select(c => c.ToHex())], Collapsed = p.Collapsed };

    /// <summary>읽을 수 없는 색 문자열은 건너뛴다.</summary>
    public NamedPalette ToPalette()
        => new(Name, [.. Colors.Select(h => Color32.TryParseHex(h, out var c) ? c : (Color32?)null).OfType<Color32>()], Collapsed);
}
