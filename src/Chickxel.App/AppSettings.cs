using System.Text.Json;
using System.Text.Json.Serialization;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>창 배치·최근 목록·보기 옵션 등 사용자 설정(<c>&lt;설정 폴더&gt;/Chickxel/settings.json</c>, <see cref="Platform.AppDataDirectory"/>).</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 10;

    public List<string> RecentFolders { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
    public string? LastFolder { get; set; }
    /// <summary>끌 때 열려 있던 문서 탭(좌우 묶음·순서·고른 탭·포커스) — 켤 때 다시 연다(없어진 파일은 건너뜀). null = 없음.</summary>
    public TabSession? OpenTabs { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    /// <summary>화면 언어 코드(예: "ko", "en", CSV로 더한 "ja"). 처음 켤 때 컴퓨터 언어로 정함(맞는 언어가 없으면 영어) — <see cref="Localization.UiLanguage"/>.</summary>
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
    /// <summary>타임라인 프레임 미리보기(셀 썸네일). 끄면 프레임 칸이 좁아지고 그림이 있는 칸에 점만 보인다.</summary>
    public bool TimelineThumbnails { get; set; } = true;
    /// <summary>타임라인 레이어 이름 칸 폭(px). 이름 칸과 프레임 칸 사이 경계선을 끌어 바꾼다.</summary>
    public int TimelineNameWidth { get; set; } = 270;

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
    /// <summary>연필 쪽 브러시(지우개가 아닌 브러시 도구가 같이 씀).</summary>
    public int BrushSize { get; set; } = 1;
    public BrushShape BrushShape { get; set; } = BrushShape.Square;
    /// <summary>지우개 브러시(연필 쪽과 따로 기억).</summary>
    public int EraserBrushSize { get; set; } = 1;
    public BrushShape EraserBrushShape { get; set; } = BrushShape.Square;
    /// <summary>채우기 허용 오차(0~100%).</summary>
    public int FillTolerance { get; set; }
    /// <summary>채우기·스마트 선택 색 비교 방식(없던 설정은 기본 = 채널 차이 최댓값).</summary>
    public FillColorMode FillColorMode { get; set; }
    public bool FillDiagonal { get; set; }
    /// <summary>채우기 기준: 보이는 레이어 전체(false = 현재 레이어).</summary>
    public bool FillSampleVisible { get; set; }
    /// <summary>스포이드 기준: 보이는 레이어 전체(false = 현재 레이어).</summary>
    public bool PickerSampleVisible { get; set; } = true;
    /// <summary>밝게/어둡게 세기(%)와 색을 바꾸는 방식.</summary>
    public int DodgeStrength { get; set; } = EditorContext.DefaultDodgeStrength;
    public DodgeMode DodgeMode { get; set; } = DodgeBurn.DefaultMode;
    public DitherPattern DitherPattern { get; set; } = DitherPattern.Checker;
    /// <summary>글씨 도구 설정(글꼴 이름·크기·굵게·기울임·간격·배율·줄 맞춤, 마지막으로 쓴 것). null = 기본.</summary>
    public Chickxel.Core.Text.TextStyle? TextStyle { get; set; }
    /// <summary>
    /// 예전 버전의 글씨 도구 글꼴(TextFont 열거형 번호 또는 이름 — 읽기만). <see cref="Load"/>가 <see cref="TextStyle"/>의 글꼴 이름으로 옮기고 다시 쓰지 않는다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? TextFont { get; set; }
    /// <summary>지우개 강도(1~100%)와 채널(R·G·B 모두 = 투명하게, 일부 = 그 색만 빼기).</summary>
    public int EraserStrength { get; set; } = 100;
    public bool EraserRed { get; set; } = true;
    public bool EraserGreen { get; set; } = true;
    public bool EraserBlue { get; set; } = true;
    public bool PixelPerfect { get; set; }
    public bool FillContiguous { get; set; } = true;
    public bool ShapeFilled { get; set; }
    /// <summary>도형 도구 모양·정다각형 꼭짓점 수·별 뿔 수·나선 감은 수·선/곡선 ㄱ자(4방향) 잇기.</summary>
    public ShapeKind ShapeKind { get; set; } = ShapeKind.Rectangle;
    public int ShapeSides { get; set; } = 5;
    public int StarPoints { get; set; } = 5;
    public int SpiralTurns { get; set; } = 3;
    public bool ShapeOrthogonal { get; set; }
    /// <summary>연필·지우개 획의 픽셀 잇기 ㄱ자(4방향, 기본 켬). 끄면 대각선으로도 잇는다.</summary>
    public bool PencilOrthogonal { get; set; } = true;
    public bool RightButtonErases { get; set; }
    /// <summary>이동 도구 순환(밖으로 나간 픽셀을 반대편으로). 끄면(기본) 일반 레이어는 캔버스 밖으로 나간 픽셀을 남긴다.</summary>
    public bool MoveWrap { get; set; }
    /// <summary>예전 기본 "순환 켬"을 새 기본 "끔"(캔버스 밖 그림 남기기)으로 한 번 옮겼는지(이후 사용자가 다시 켜도 보존).</summary>
    public bool MoveKeepOutsideApplied { get; set; } = true;
    /// <summary>변형 모드(Ctrl+T) 리샘플링(기본 RotSprite).</summary>
    public ResampleMode TransformResample { get; set; } = ResampleMode.RotSprite;
    /// <summary>그라데이션 정지점("위치(0~1):#RRGGBBAA", 2개 이상). null = 기본(검정 → 흰색).</summary>
    public List<string>? GradientStops { get; set; }
    /// <summary>그라데이션 불투명도 정지점("위치(0~1):불투명도(0~255)", 2개 이상). null = 기본(100% → 100%).</summary>
    public List<string>? GradientAlphaStops { get; set; }
    public GradientInterpolation GradientInterpolation { get; set; } = GradientInterpolation.Smooth;
    public GradientDither GradientDither { get; set; } = GradientDither.None;
    public GradientColorSpace? GradientColorSpace { get; set; } = Chickxel.Core.Edit.GradientColorSpace.OkLch;
    /// <summary>그라데이션 연필 정지점(형식은 <see cref="GradientStops"/>와 같음). null = 기본(검정 → 흰색).</summary>
    public List<string>? PencilGradientStops { get; set; }
    /// <summary>그라데이션 연필 불투명도 정지점(형식은 <see cref="GradientAlphaStops"/>와 같음). null = 기본(100% → 100%).</summary>
    public List<string>? PencilGradientAlphaStops { get; set; }
    public GradientInterpolation PencilGradientInterpolation { get; set; } = GradientInterpolation.Smooth;
    public GradientDither PencilGradientDither { get; set; } = GradientDither.None;
    public GradientColorSpace PencilGradientColorSpace { get; set; } = Chickxel.Core.Edit.GradientColorSpace.OkLch;
    /// <summary>그라데이션 연필 방식(획 전체·반복)과 반복 간격(px).</summary>
    public GradientStrokeMode PencilGradientMode { get; set; } = GradientStrokeMode.WholeStroke;
    public int PencilGradientInterval { get; set; } = GradientStroke.DefaultInterval;
    /// <summary>모양 펜: 고른 세트 id(null = 첫 세트), 지금 색으로 물들이기, 끌기 간격(px, 1 이상), 최적화 간격. 세트 모음 자체는 ShapePen/shape-sets.json.</summary>
    public string? ShapePenSet { get; set; }
    public bool ShapePenTint { get; set; }
    public int ShapePenSpacing { get; set; } = 1;
    public bool ShapePenOptimizedSpacing { get; set; } = true;
    /// <summary>대칭 그리기 축을 피벗에 둔다(좌우·상하 대칭 켜기 자체는 저장하지 않음).</summary>
    public bool MirrorAtPivot { get; set; }
    public string PrimaryColor { get; set; } = "#000000";
    /// <summary>이미지를 열 때마다 캔버스를 화면에 맞춘다(위 막대 "맞추기" 토글).</summary>
    public bool AutoFit { get; set; } = true;
    public string SecondaryColor { get; set; } = "#00000000";
    public List<string> RecentColors { get; set; } = new();
    /// <summary>색 고르기 방식(사각형·벌집·색상환·슬라이더).</summary>
    public ColorPickerMode ColorPickerMode { get; set; } = ColorPickerMode.Square;
    /// <summary>색 패널 맨 아래 "레이어 색"을 접어 두었는지.</summary>
    public bool LayerColorsCollapsed { get; set; }
    /// <summary>색 패널의 색 편집기("색": 고르기 칸·알파 막대·입력 칸)를 접어 두었는지.</summary>
    public bool ColorCollapsed { get; set; }
    /// <summary>색 패널의 "최근 색"을 접어 두었는지.</summary>
    public bool RecentColorsCollapsed { get; set; }
    /// <summary>팔레트 열의 팔레트들(위 → 아래). null = 처음 실행(기본 팔레트 하나).</summary>
    public List<PaletteSetting>? Palettes { get; set; }
    /// <summary>예전 버전의 팔레트 하나(읽기만 — <see cref="Load"/>가 <see cref="Palettes"/> 첫 항목으로 옮기고 다시 쓰지 않음).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Palette { get; set; }
    /// <summary>팔레트 웹(Lospec) 창에서 마지막으로 고른 정렬(default·downloads·newest·alphabetical·favorites, <see cref="LospecPaletteDialog.SortKey"/>).</summary>
    public string LospecSort { get; set; } = LospecPaletteDialog.DefaultSortKey;

    // 프로젝트·미리보기
    public float LastPpu { get; set; } = 16f;
    public string PreviewBackground { get; set; } = "checker";
    public int PreviewZoom { get; set; } = 0;
    public int PreviewFps { get; set; } = 0;

    // AI 채팅(Claude Code·Codex)
    /// <summary>채팅에 쓰는 CLI("claude" / "codex", 모르는 값 = claude).</summary>
    public string ChatProvider { get; set; } = "claude";
    /// <summary>채팅의 "지금 그림만": 보낼 때 열린 문서 하나만 AI가 고침(다른 파일은 열거나 만들지 않음). 두 탭이 같이 씀. 잠긴 레이어는 이 설정과 관계없이 늘 건드리지 않는다.</summary>
    public bool AiCurrentFileOnly { get; set; }
    /// <summary>AI 참고 자료 폴더(null = 기본 <c>&lt;설정 폴더&gt;/Chickxel/References</c>, <see cref="AppReferences.FolderOf"/>).</summary>
    public string? ReferencesFolder { get; set; }
    /// <summary>
    /// AI 참고 자료에 기본 예시 그림을 보임(목록·AI 검색·그림 생성의 스타일 참고). 기본 예시의 그림 품질이 낮아 지금은 기본으로 끈다
    /// (환경 설정에서 다시 켤 수 있음).
    /// </summary>
    public bool ReferencesShowExamples { get; set; }
    /// <summary>기본 예시 끄기를 예전 설정에도 한 번 적용했는지.</summary>
    public bool ReferencesExamplesOffApplied { get; set; }
    /// <summary>탭마다 마지막으로 보던 저장된 대화: AI id("claude"/"codex") → 대화 id. 없으면 대화 목록을 보던 것.</summary>
    public Dictionary<string, string>? ChatLastOpen { get; set; }
    /// <summary>Claude 모델 별칭(null = Claude Code 기본값, "opus"/"sonnet"/"haiku").</summary>
    public string? AiModel { get; set; }
    /// <summary>claude 실행 파일 경로를 직접 정함(null = 자동으로 찾음).</summary>
    public string? ClaudePath { get; set; }
    /// <summary>Claude Code가 알려 준 실제 모델 ID: 보낸 별칭(기본값은 "default:" + Claude Code 설정의 모델) → ID. 목록에 버전을 보이는 데 쓴다.</summary>
    public Dictionary<string, string>? ClaudeResolvedModels { get; set; }
    /// <summary>ffmpeg 실행 파일 경로를 직접 정함(null = PATH·흔한 설치 위치에서 찾음). 영상·AVIF·HEIC 열기에 쓴다.</summary>
    public string? FfmpegPath { get; set; }
    /// <summary>추천 Codex 모델(목록에 "추천"으로 표시, 새 설정의 기본값).</summary>
    public const string RecommendedCodexModel = "gpt-6-astra";
    /// <summary>Codex 모델(null = Codex 설정의 기본값). 처음엔 <see cref="RecommendedCodexModel"/>.</summary>
    public string? CodexModel { get; set; } = RecommendedCodexModel;
    /// <summary>
    /// Codex 기본 모델을 추천 모델로 옮겼는지. 예전 설정에서 고르지 않은 채(null) 남은 Codex 모델을 한 번만 추천 모델로 바꾸고,
    /// 그 뒤에 사용자가 "기본값(Codex 설정)"을 고르면 그대로 둔다.
    /// </summary>
    public bool CodexRecommendedApplied { get; set; }
    /// <summary>codex 실행 파일 경로를 직접 정함(null = 자동으로 찾음).</summary>
    public string? CodexPath { get; set; }
    public int ChatX { get; set; } = -1;
    public int ChatY { get; set; } = -1;
    public int ChatWidth { get; set; } = 420;
    public int ChatHeight { get; set; } = 640;
    public bool ChatOpen { get; set; }
    /// <summary>AI 채팅을 열거나 다시 앞으로 가져올 때 메인 창 가운데에 둔다.</summary>
    public bool ChatCenterOnOpen { get; set; } = true;
    /// <summary>AI 그리기 설정(채팅의 설정 칸 — 그리는 방식, 생성·변환 옵션).</summary>
    public Chickxel.Core.AiDraw.AiDrawSettings AiDraw { get; set; } = new();
    /// <summary>예전 기본 "그림 생성"을 새 기본 "자동"으로 한 번 옮겼는지(이후 사용자가 다시 그림 생성을 골라도 보존).</summary>
    public bool AiDrawAutoDefaultApplied { get; set; } = true;
    /// <summary>
    /// AI 상태 창을 다시 보지 않기로 한 CLI 구성 지문. 찾은 AI CLI 경로나 직접 지정 경로가 바뀌면 지문이 달라져 다시 보인다(앱·CLI 업데이트로는 바뀌지 않음).
    /// </summary>
    public string? AiStatusDismissedFingerprint { get; set; }
    /// <summary>
    /// 외부 AI 연결: 이 컴퓨터의 다른 AI 앱(Claude Desktop·Cursor·Claude Code 등)이 <c>Chickxel --mcp</c>로 켜져 있는 Chickxel의 MCP 도구를 쓰게 한다
    /// (<see cref="Ai.McpScope.External"/>). 기본 끔.
    /// </summary>
    public bool AiExternalMcp { get; set; }
    /// <summary>외부 AI 연결에서 <c>open_file</c>(경로를 받아 아무 그림·프로젝트나 엶)을 허락한다. 기본 끔.</summary>
    public bool AiExternalOpenFile { get; set; }
    /// <summary>외부 AI 연결에서 <c>generate_pixel_art</c>(내 Codex로 그림 생성 — Codex 사용량을 씀)를 허락한다. 기본 끔.</summary>
    public bool AiExternalGenerate { get; set; }

    // AI 그림 픽셀로 정리
    /// <summary>마지막 단계 설정(JSON — <see cref="Chickxel.Core.Pixelate.PixelatePresets.SerializeSettings"/>). null = 기본.</summary>
    public string? PixelateSettings { get; set; }
    /// <summary>창 설정(후보 수·가중치·넣기·AI 다듬기 등).</summary>
    public PixelateOptions? Pixelate { get; set; }

    // 내보내기 설정 창(Ctrl+E)
    public int ExportX { get; set; } = -1;
    public int ExportY { get; set; } = -1;
    public int ExportWidth { get; set; } = 1100;
    public int ExportHeight { get; set; } = 760;
    /// <summary>왼쪽 설정 칸 너비 비율(0~1, 나머지는 오른쪽 미리보기).</summary>
    public double ExportSplit { get; set; } = 0.46;
    /// <summary>접어 둔 설정 묶음 이름(설정 프리셋·공통·파일·프레임 시트·부위 아틀라스·고급).</summary>
    public List<string> ExportCollapsed { get; set; } = new();

    // GIF 애니메이션 내보내기(마지막으로 쓴 값, 앱 전체 — 속도는 갤러리에 올리기도 같이 씀)
    /// <summary>재생 횟수(0 = 무한 반복).</summary>
    public int GifPlayCount { get; set; }
    /// <summary>배경색 위에 합성(꺼짐 = 투명 배경).</summary>
    public bool GifUseBackground { get; set; }
    /// <summary>배경색(#RRGGBB, 투명으로 돌려도 기억).</summary>
    public string GifBackgroundColor { get; set; } = "#FFFFFF";
    /// <summary>재생 속도 방식(원래 속도 / 배속 / FPS).</summary>
    public Chickxel.Core.IO.GifTimingMode GifTimingMode { get; set; }
    /// <summary>배속(배속 방식일 때).</summary>
    public double GifSpeed { get; set; } = 1;
    /// <summary>초당 프레임 수(FPS 방식일 때).</summary>
    public int GifFps { get; set; } = Chickxel.Core.IO.GifTiming.DefaultFps;

    // 팔레트 색으로 변환(마지막으로 쓴 값, 앱 전체)
    /// <summary>색 고르는 규칙.</summary>
    public Chickxel.Core.Edit.PaletteMatchRule PaletteConvertRule { get; set; }
    /// <summary>가장 가까운 색 규칙의 색 거리.</summary>
    public Chickxel.Core.Edit.PaletteMatchDistance PaletteConvertDistance { get; set; }

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
    /// <summary>자동 저장(기본 꺼짐): 편집이 <see cref="AutoSaveIdleSeconds"/>초 동안 없으면 지금 문서를 Ctrl+S처럼 저장(파일이 있는 문서만).</summary>
    public bool AutoSave { get; set; }
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
    /// <summary>
    /// Discord 활동 상태(Rich Presence, "Chickxel 하는 중"): 이 컴퓨터에서 실행 중인 Discord 앱에만 로컬로 알린다(<see cref="Chickxel.App.DiscordPresence"/>).
    /// </summary>
    public bool DiscordPresence { get; set; } = true;

    // 스크립트 효과
    /// <summary>
    /// 스크립트 효과를 실행할지 사용자가 정한 것: 원문 해시(SHA-256) → 실행(true)·실행 안 함(false). 없는 해시는 아직 묻지 않은 것이다(13.5).
    /// 이 컴퓨터에서 쓰거나 가져온 스크립트는 자동으로 true가 된다(<see cref="ScriptTrustStore"/>).
    /// </summary>
    public Dictionary<string, bool> ScriptTrust { get; set; } = new();

    // 에셋 게시
    /// <summary>에셋 게시 창이 기억하는 계정 표시 이름과 이 컴퓨터에서 올린 패키지(<see cref="AssetPublishDialog"/>).</summary>
    public AssetPublishSettings AssetPublish { get; set; } = new();

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
    public static string FilePath => Path.Combine(Platform.AppFolder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>설정 파일을 읽는다. 없거나 깨졌으면 기본값.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    !document.RootElement.TryGetProperty(nameof(GradientColorSpace), out _))
                    settings.GradientColorSpace = null;
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    !document.RootElement.TryGetProperty(nameof(AiDrawAutoDefaultApplied), out _))
                    settings.AiDrawAutoDefaultApplied = false;
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    !document.RootElement.TryGetProperty(nameof(MoveKeepOutsideApplied), out _))
                    settings.MoveKeepOutsideApplied = false;
                return Migrate(settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new AppSettings();
    }

    /// <summary>
    /// 예전 형식을 옮긴다: 팔레트 하나(<see cref="Palette"/>) → <see cref="Palettes"/> 첫 항목 "팔레트 1",
    /// 단축키 명령 "AI/Claude 채팅" → "AI/AI 채팅"(Codex가 더해져 이름이 바뀜), 글씨 도구 글꼴(<see cref="TextFont"/>) → <see cref="TextStyle"/>의 글꼴 이름.
    /// </summary>
    private static AppSettings Migrate(AppSettings s)
    {
        if (s.Palette is { } old)
        {
            s.Palettes ??= [new PaletteSetting { Name = "팔레트 1", Colors = old }];
            s.Palette = null;
        }
        if (s.Shortcuts is { } keys && keys.Remove("AI/Claude 채팅", out var chat)) keys.TryAdd("AI/AI 채팅", chat);
        if (!s.CodexRecommendedApplied)
        {
            s.CodexModel ??= RecommendedCodexModel;
            s.CodexRecommendedApplied = true;
        }
        if (!s.ReferencesExamplesOffApplied)
        {
            s.ReferencesShowExamples = false;
            s.ReferencesExamplesOffApplied = true;
        }
        s.AiDraw ??= new();
        if (!s.AiDrawAutoDefaultApplied)
        {
            if (s.AiDraw.Mode == Chickxel.Core.AiDraw.AiDrawMode.Generate) s.AiDraw.Mode = Chickxel.Core.AiDraw.AiDrawMode.Auto;
            s.AiDrawAutoDefaultApplied = true;
        }
        if (!s.MoveKeepOutsideApplied)
        {
            s.MoveWrap = false;
            s.MoveKeepOutsideApplied = true;
        }
        s.AiDraw.FrameCounts ??= Chickxel.Core.AiDraw.AiDrawActions.DefaultFrameCounts();
        if (s.TextFont is { } font)
        {
            if (s.TextStyle is null && LegacyFontName(font) is { } name) s.TextStyle = new() { FontName = name };
            s.TextFont = null;
        }
        return s;
    }

    /// <summary>예전 TextFont 값(열거형 번호 0~4 또는 이름)의 글꼴 이름. 모르는 값이면 null(기본 글꼴).</summary>
    private static string? LegacyFontName(JsonElement value)
    {
        string[] names = ["Galmuri7", "Galmuri9", "Galmuri11", "Galmuri11Bold", "Galmuri14"];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i) && i >= 0 && i < names.Length) return names[i];
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } s)
            return names.FirstOrDefault(n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>설정 파일을 쓴다. 실패하면 false(설정 저장 실패는 작업을 막지 않는다).</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            AtomicFile.WriteText(FilePath, JsonSerializer.Serialize(this, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// "설정 초기화"(환경 설정)가 그대로 두는 값: 최근 목록·다시 열 탭, 창·패널 배치와 캔버스 확대율(끌 때 지금 창 상태로 다시 저장되는 것),
    /// 사용자가 만든 것(팔레트·최근 색·픽셀로 정리 프리셋과 옵션·에셋 게시 기록), 스크립트 신뢰 결정(설치한 에셋의 스크립트도 여기에 있어
    /// 지우면 설치한 효과가 멈춤), 저장된 AI 대화 위치·모델 ID 캐시.
    /// </summary>
    private static readonly HashSet<string> KeptOnReset =
    [
        nameof(RecentFolders), nameof(RecentFiles), nameof(LastFolder), nameof(OpenTabs),
        nameof(WindowX), nameof(WindowY), nameof(WindowWidth), nameof(WindowHeight), nameof(Maximized), nameof(FilesVisible),
        nameof(LeftWidth), nameof(PaletteWidth), nameof(RightWidth), nameof(BottomHeight), nameof(BrowserTreeHeight), nameof(PreviewHeight),
        nameof(TimelineNameWidth), nameof(Zoom),
        nameof(ChatX), nameof(ChatY), nameof(ChatWidth), nameof(ChatHeight), nameof(ChatOpen),
        nameof(ExportX), nameof(ExportY), nameof(ExportWidth), nameof(ExportHeight), nameof(ExportSplit), nameof(ExportCollapsed),
        nameof(Palettes), nameof(RecentColors), nameof(Pixelate), nameof(PixelateSettings), nameof(AssetPublish), nameof(ScriptTrust),
        nameof(ChatLastOpen), nameof(ClaudeResolvedModels),
    ];

    /// <summary>
    /// 설정을 처음 값(<c>new AppSettings()</c>)으로 되돌린다 — 단축키·화면 언어(null = 다음 시작 때 컴퓨터 언어) 포함, <see cref="KeptOnReset"/>는 그대로.
    /// 같은 객체를 고치므로 이 설정을 들고 있는 창들이 바로 새 값을 본다. 저장은 부른 쪽이 한다.
    /// </summary>
    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0 || KeptOnReset.Contains(p.Name)) continue;
            p.SetValue(this, p.GetValue(defaults));
        }
        Migrate(this);
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

/// <summary>
/// 에셋 게시: 마지막으로 게시 창을 연 계정의 표시 이름(개인 내보내기의 작성자 이름 — 로그인하지 않았을 때 씀), 마지막으로 알던 계정 핸들
/// (로그아웃해도 내가 올린 에셋을 내 것으로 봄 — <see cref="AppAssets.MyHandle"/>)과 이 컴퓨터에서 올린 패키지(최근 것부터, 다음 버전 제안·같은 에셋 다시 올리기).
/// </summary>
public sealed class AssetPublishSettings
{
    public const int MaxSaved = 50;

    public string AuthorName { get; set; } = "";
    public string LastHandle { get; set; } = "";
    public List<AssetPublishRecord> Saved { get; set; } = new();
}

/// <summary>올린 패키지 하나: 패키지 정보(언어별 이름·짧은 설명 포함)와 올린 개인 에셋 ID, 패키지 SHA-256.</summary>
public sealed class AssetPublishRecord
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, AssetTranslation> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Assets { get; set; } = new();
    public DateTime SavedUtc { get; set; }
}
