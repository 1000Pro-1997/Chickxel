using Avalonia.VisualTree;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 튜토리얼: 켤 때 뜨는 튜토리얼 창(TutorialWindow), 메뉴 줄 오른쪽 학사모 단추, 세 가지 따라 하기(TutorialSpotlight 단계 목록).
// 단계마다 비추는 컨트롤·넘어갈 조건·예시 파일은 저장소 루트 튜토리얼.md에 적어 두고, GuideShots tutorial-check로 점검한다.
public sealed partial class MainForm
{
    /// <summary>예시 파일(내장 <c>Chickxel.Tutorial.*</c>)을 꺼내 두는 폴더.</summary>
    public static string TutorialFolder => Path.Combine(Platform.AppFolder, "Tutorial");

    /// <summary>'그림을 픽셀 아트로'에서 미리 넣어 두는 예시 그림(AI가 만든 듯한 흐린 그림).</summary>
    public const string TutorialPixelateExample = "pixelate-example.jpg";

    /// <summary>'그림을 픽셀 아트로'에서 결과 크기로 고르게 하는 가로(예시 그림에 잘 맞는 값 — 이 값이어야 넘어감).</summary>
    private const int TutorialPixelateWidth = 64;

    /// <summary>픽셀로 정리 창의 결과를 문서에 넣은 횟수(적용 · 새 탭에 추가 — 튜토리얼이 넣었는지 봄).</summary>
    private int _pixelateApplied;

    /// <summary>'효과 쓰기'에서 새 탭으로 여는 예시 픽셀 그림.</summary>
    private const string TutorialEffectExample = "effect-example.png";

    /// <summary>'3D를 픽셀 아트로'에서 3D 모델 창에 넣는 예시 모델(상자로 만든 병아리, 걷기 애니메이션).</summary>
    public const string TutorialModelExample = "model-example.glb";

    /// <summary>메뉴 줄 오른쪽(갤러리 단추 왼쪽)의 튜토리얼 단추(학사모): 튜토리얼 창을 다시 연다.</summary>
    private Button BuildTutorialButton()
    {
        var button = new Button
        {
            Content = new IconView(VectorIcon.GraduationCap) { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 2, 0),
            MinHeight = 0,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(UiStrings.Tutorial_ButtonTip);
        button.Click += (_, _) => TutorialWindow.Open(this, _settings);
        return button;
    }

    /// <summary>켤 때(지난번 탭을 연 뒤) 튜토리얼 창을 띄운다 — "다시 보지 않기"로 끄지 않았으면 매번.</summary>
    private void ShowTutorialAtStartup()
    {
        if (_settings.ShowTutorialOnStart) TutorialWindow.Open(this, _settings);
    }

    /// <summary>
    /// 튜토리얼 하나를 처음부터 시작한다(튜토리얼 창의 카드). 끝까지 마치면 이 컴퓨터 설정(<see cref="AppSettings.TutorialsDone"/>)에 적고
    /// 튜토리얼 창을 다시 띄워 그 카드에 "완료"를 보인다(다음 것을 고르게). 도중에 그만둬도(말풍선 닫기, 대상 창 닫기) 튜토리얼 창을 다시 띄운다.
    /// 먼저 마칠 튜토리얼이 남았으면 시작하지 않고 튜토리얼 창(잠긴 카드에 선행 튜토리얼)을 띄운다.
    /// </summary>
    internal void StartTutorial(TutorialKind kind)
    {
        if (TutorialWindow.Prerequisites(kind).Any(p => !_settings.TutorialsDone.Contains(p.ToString())))
        {
            TutorialWindow.Open(this, _settings);
            return;
        }
        var steps = kind switch
        {
            TutorialKind.Pixelate => PixelateTutorial(),
            TutorialKind.Effects => EffectsTutorial(),
            TutorialKind.Ai => AiTutorial(),
            TutorialKind.Model => ModelTutorial(),
            TutorialKind.EffectMaker => EffectMakerTutorial(),
            TutorialKind.Export => ExportTutorial(),
            TutorialKind.LayersFrames => LayersFramesTutorial(),
            TutorialKind.Canvas => CanvasTutorial(),
            TutorialKind.Tilemap => TilemapTutorial(),
            TutorialKind.Skins => SkinsTutorial(),
            _ => LevelTutorial(),
        };
        string title = TutorialWindow.TitleOf(kind);
        Activate();
        TutorialSpotlight.Start(this, title, steps, () =>
        {
            if (!_settings.TutorialsDone.Contains(kind.ToString()))
            {
                _settings.TutorialsDone.Add(kind.ToString());
                _settings.Save();
            }
            TutorialWindow.Open(this, _settings);
        }, () => TutorialWindow.Open(this, _settings));
    }

    /// <summary>
    /// 내장 예시 파일을 <see cref="TutorialFolder"/>에 꺼내고 경로를 돌려준다. 같은 크기의 파일이 이미 있으면 그대로 쓴다
    /// (다른 프로그램이 열어 두어 덮어쓸 수 없어도 튜토리얼이 멈추지 않게).
    /// </summary>
    internal static string ExtractTutorialFile(string name)
    {
        Directory.CreateDirectory(TutorialFolder);
        var path = Path.Combine(TutorialFolder, name);
        using var source = typeof(MainForm).Assembly.GetManifestResourceStream("Chickxel.Tutorial." + name)
            ?? throw new InvalidOperationException("missing tutorial resource " + name);
        if (File.Exists(path) && new FileInfo(path).Length == source.Length) return path;
        using var target = File.Create(path);
        source.CopyTo(target);
        return path;
    }

    // ───────────── 그림을 픽셀 아트로 ─────────────

    /// <summary>
    /// 그림을 픽셀 아트로(꼭 해야 넘어가는 단계가 대부분): 메뉴 AI > AI 그림 픽셀로 정리…로 픽셀로 정리 창을 직접 열게 하고(열린 메뉴에서 그 항목만 누를 수 있음)
    /// → 예시 그림을 넣었다고 알림 → 후보 칸 우클릭 '이 프리셋만 돌리기'로 돌린 뒤 '취소'로 멈추기(다 돌면 저절로 다음)
    /// → 설정 칸을 펴고 프리셋 '기본' 불러오기 → 간편 설정의 결과 크기를 우클릭 목록으로 바꾸기 → 재생(정리하기)으로 돌려 끝나기 기다리기
    /// → 결과가 든 후보 칸 한 번 누르기 → 결과 크게 보기 → 색 빼기와 원본에서 색 더하기(움짤 둘을 나란히) → 적용(창이 닫힘) → 결과가 들어간 캔버스를 보이며 마침.
    /// </summary>
    private List<TutorialStep> PixelateTutorial()
    {
        string excludedBefore = "", pinnedBefore = "";
        int tileClicksBefore = 0, appliedBefore = 0;
        // 돌리기 단계: 첫 준비가 끝난 것을 봤는지(첫 준비 중의 처음 후보 만들기는 세지 않음), 프리셋 목록을 열어 봤는지.
        bool readySeen = false, presetListOpened = false;
        PixelateDialog? D() => _pixelate is { IsVisible: true } d ? d : null;
        bool Excluded() => D() is { } d && d.TutorialExcludedText != excludedBefore;
        bool Pinned() => D() is { } d && d.TutorialPinnedText != pinnedBefore;
        return
        [
            new()
            {
                Id = "pixelate.open",
                Text = () => UiStrings.FormatTutorial_Px_Open(menu: MenuPath("AI/AI 그림 픽셀로 정리…"), file: UiStrings.Pixelate_MenuItem),
                Target = () => TopMenuOf("AI/AI 그림 픽셀로 정리…"),
                // 메뉴를 열면 그 항목만 빛나고 다른 항목은 누를 수 없다(메뉴 줄의 다른 메뉴도 어두운 곳이라 막힘).
                MenuItem = () => Command("AI/AI 그림 픽셀로 정리…").Item,
                Done = () => D() is not null,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "pixelate.source",
                // 연 창에 예시 그림을 넣는다(끌어 놓기·파일 찾기와 같은 길 — 이미 그림이 있던 창이면 예시로 바꿈).
                Enter = () => D()?.TutorialLoad(ExtractTutorialFile(TutorialPixelateExample)),
                Text = () => UiStrings.Tutorial_Px_Source,
                Target = () => D()?.TutorialSourceArea,
            },
            new()
            {
                // 첫 준비 중이면 기다리라는 글, 끝나면 '같은 색상 계열' 칸 우클릭 → '이 프리셋만 돌리기'. 돌기 시작하면 저절로 다음(취소 단계)으로.
                Id = "pixelate.rerun",
                Clips = [new("pixelate-rerun")],
                Text = () => D() is { TutorialReady: true } d
                    ? UiStrings.FormatTutorial_Px_Rerun(preset: d.TutorialIdleTile("shade")?.Preset ?? "", menu: UiStrings.Pixelate_Opt_RunPreset)
                    : UiStrings.Tutorial_Px_Wait,
                Target = () => D() is { } d ? d.TutorialReady ? d.TutorialIdleTile("shade")?.Tile ?? d.TutorialCandidates : d.TutorialCandidates : null,
                MenuItem = () => TutorialSpotlight.OpenMenuItem(UiStrings.Pixelate_Opt_RunPreset),
                Done = () =>
                {
                    if (D() is not { } d) return false;
                    if (d.TutorialReady) readySeen = true;
                    // 첫 준비 중의 처음 후보 만들기는 세지 않는다(준비가 끝난 것을 본 뒤 돌기 시작해야).
                    return readySeen && d.TutorialRunning;
                },
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 도는 동안 자동 최적화 단추가 '취소'가 된다 — 눌러 멈추면(또는 그 사이 다 돌면) 저절로 다음으로.
                Id = "pixelate.cancel",
                Text = () => UiStrings.Tutorial_Px_Optimize,
                Target = () => D()?.TutorialOptimizeButton,
                Done = () => D() is { TutorialRunning: false },
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 설정 칸을 펴고 프리셋 목록을 열어 '기본'을 고르면(목록이 닫히며 '기본'이 골라져 있으면) 저절로 다음으로 — 이미 '기본'이어도 목록에서 골라 보게.
                Id = "pixelate.preset",
                Enter = () => presetListOpened = false,
                Text = () => D() is { TutorialSettingsOpen: true } d
                    ? UiStrings.FormatTutorial_Px_Preset(preset: d.TutorialDefaultPresetName ?? UiStrings.Pixelate_Preset_Default)
                    : UiStrings.FormatTutorial_Px_Settings(button: UiStrings.Pixelate_Settings_Show),
                Target = () => D() is { } d ? d.TutorialSettingsOpen ? d.TutorialPresetBox : d.TutorialSettingsToggle : null,
                Done = () =>
                {
                    if (D() is not { TutorialSettingsOpen: true } d) return false;
                    if (d.TutorialPresetBox.IsDropDownOpen) presetListOpened = true;
                    return presetListOpened && !d.TutorialPresetBox.IsDropDownOpen && d.TutorialDefaultPresetChosen;
                },
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 결과 크기 '자동'을 끄고 가로를 권하는 크기(예시 그림에 잘 맞는 64)로 바꾸면(우클릭 예시 값·입력·끌기 어느 것이든) 저절로 다음으로(다른 값은 넘어가지 않음).
                Id = "pixelate.quick",
                Clips = [new("pixelate-size")],
                Text = () => UiStrings.FormatTutorial_Px_Quick(auto: UiStrings.Pixelate_Auto, size: TutorialPixelateWidth),
                Target = () => D()?.TutorialQuickSizeRow(),
                Done = () => D()?.TutorialFixedWidth == TutorialPixelateWidth,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 재생(정리하기)을 누르면(돌기 시작하면) 저절로 다음(결과 칸 누르기)으로 — 끝나기는 다음 단계에서 기다린다.
                Id = "pixelate.run",
                Text = () => UiStrings.FormatTutorial_Px_Run(button: UiStrings.Pixelate_Run),
                Target = () => D()?.TutorialRunButton,
                Done = () => D() is { TutorialRunning: true },
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 정리가 끝나기를 기다린 뒤, 결과가 들어간 후보 칸(저절로 골라져 있음)을 한 번 눌러 보게 한다 — 후보 칸을 누르면 그 결과가 미리보기에 보인다는 것을 익히게.
                Id = "pixelate.pick",
                Enter = () => tileClicksBefore = D()?.TutorialTileClicks ?? 0,
                Text = () => D() is { TutorialRunning: true }
                    ? UiStrings.Tutorial_Px_Running
                    : UiStrings.FormatTutorial_Px_Pick(name: D()?.TutorialSelectedTile?.Name ?? ""),
                Target = () => D() is { } d ? d.TutorialRunning ? d.TutorialCandidates : d.TutorialSelectedTile?.Tile ?? d.TutorialCandidates : null,
                Done = () => D() is { TutorialRunning: false } d && d.TutorialTileClicks != tileClicksBefore,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "pixelate.zoom",
                Text = () => UiStrings.Tutorial_Px_Zoom,
                Target = () => D()?.TutorialPreview,
                Done = () => D()?.TutorialZoomOpen == true,
                Required = true,
                // 크게 보기가 대화상자를 덮으므로 열리면 바로 다음(색 단계 — 크게 보기 안의 색 줄)으로.
                AutoAdvance = true,
            },
            new()
            {
                // 색 빼기·더하기를 한 단계에: 움짤 둘(왼쪽 빼기, 오른쪽 더하기)을 나란히. 저절로 넘어가지 않아 마음껏 빼고 더해 본 뒤 '다음'으로만 넘어간다.
                // + 칸을 누른 뒤(스포이드)에는 원본 그림을 비추고 말풍선을 숨긴다(넓은 말풍선이 누를 원본을 가리지 않게) — 원본을 누르거나 우클릭으로 취소하면 다시 보인다.
                Id = "pixelate.colors",
                Clips =
                [
                    new("pixelate-colors", () => UiStrings.Tutorial_Px_ExcludeClip),
                    new("pixelate-pin", () => UiStrings.FormatTutorial_Px_PinClip(button: UiStrings.Pixelate_Pin_Add)),
                ],
                Enter = () =>
                {
                    excludedBefore = D()?.TutorialExcludedText ?? "";
                    pinnedBefore = D()?.TutorialPinnedText ?? "";
                },
                Text = () => (Excluded(), Pinned()) switch
                {
                    (true, true) => UiStrings.Tutorial_Px_ColorsBoth,
                    (true, false) => UiStrings.Tutorial_Px_ColorsPin,
                    (false, true) => UiStrings.Tutorial_Px_ColorsExclude,
                    _ => UiStrings.Tutorial_Px_Colors,
                },
                Target = () => D()?.TutorialColors,
                // 스포이드 중이면 원본 그림 자리(TargetArea가 Target보다 먼저).
                TargetArea = () => D() is { TutorialPicking: true } d ? d.TutorialPickSource : null,
                HideBubble = () => D()?.TutorialPicking == true,
                // + 칸을 누른 뒤 원본 그림(비춘 칸 밖일 수 있음)을 누르게.
                PassThrough = true,
            },
            new()
            {
                // 적용(또는 새 탭에 추가·후보 칸 두 번 누르기)으로 결과가 캔버스에 들어가 창이 닫히면 다음(캔버스 보기)으로.
                Id = "pixelate.apply",
                // 크게 보기가 대화상자를 덮고 있으면 닫아 적용 단추가 보이게.
                Enter = () =>
                {
                    appliedBefore = _pixelateApplied;
                    D()?.TutorialCloseZoom();
                },
                Text = () => UiStrings.FormatTutorial_Px_Apply(button: D()?.TutorialApplyButton.Content as string ?? UiStrings.Pixelate_Apply),
                Target = () => D()?.TutorialApplyButton,
                Done = () => _pixelateApplied != appliedBefore,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 마침: 결과가 들어간 캔버스를 비추고 자유롭게 고쳐 보라고 한다('끝내기').
                Id = "pixelate.done",
                // 이미 열린 문서에 넣었으면 변형 모드(옮기기·크기)로 들어와 있어 확정하는 법도 알린다.
                Text = () => _ctx.IsTransforming ? UiStrings.Tutorial_Px_DonePlace : UiStrings.Tutorial_Px_Done,
                Target = () => _canvas,
            },
        ];
    }

    // ───────────── 3D를 픽셀 아트로 ─────────────

    /// <summary>
    /// 3D를 픽셀 아트로: 메뉴 3D > 3D 모델 열기…로 3D 모델 창 열기(모달, 열린 메뉴에서 그 항목만 누를 수 있음) → 예시 병아리 모델을 넣고 원본·결과 두 칸 안내
    /// → 원본 모델을 끌어 돌리기 → 원본 색 바꾸기(색 칸 우클릭, 움짤) → 출력(크기·프레임) → 프리셋·카메라·빛·픽셀 다듬기·외곽선 → 가져오기(창이 닫히고 그린 프레임이 새 탭으로 열림)
    /// → 들어간 캔버스를 보이며 마침.
    /// </summary>
    private List<TutorialStep> ModelTutorial()
    {
        string angleBefore = "";
        var sizeBefore = default(PixelSize);
        int colorsBefore = 0;
        // 마지막으로 보인 3D 모델 창(닫힌 뒤에도 가져오기로 닫혔는지 보려고).
        ModelImportDialog? opened = null;
        ModelImportDialog? D()
        {
            var d = OwnedWindows.OfType<ModelImportDialog>().FirstOrDefault(w => w.IsVisible);
            if (d is not null) opened = d;
            return d;
        }
        return
        [
            new()
            {
                Id = "model.open",
                Text = () => UiStrings.FormatTutorial_Md_Open(menu: MenuPath("3D/3D 모델 열기…")),
                Target = () => TopMenuOf("3D/3D 모델 열기…"),
                // 메뉴를 열면 그 항목만 빛나고 다른 항목은 누를 수 없다.
                MenuItem = () => Command("3D/3D 모델 열기…").Item,
                Done = () => D() is not null,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "model.load",
                // 연 창에 예시 모델을 넣는다(모델 열기·끌어 놓기와 같은 길).
                Enter = () => D()?.TutorialLoad(ExtractTutorialFile(TutorialModelExample)),
                Text = () => UiStrings.FormatTutorial_Md_Load(button: UiStrings.Model_OpenModel),
                Target = () => D()?.TutorialPreview,
            },
            new()
            {
                Id = "model.rotate",
                Enter = () => angleBefore = D()?.TutorialAngle ?? "",
                Text = () => UiStrings.Tutorial_Md_Rotate,
                Target = () => D()?.TutorialPreview,
                Done = () => D() is { } d && d.TutorialAngle != angleBefore,
                Required = true,
            },
            new()
            {
                // 원본 색 바꾸기(색 칸 우클릭 → 색 바꾸기… → 색 고르기 창). 해 보면 저절로, 안 해도 '다음'.
                Id = "model.colors",
                Clips = [new("model-colors")],
                BigClip = true,
                Enter = () => colorsBefore = D()?.TutorialChangedColors ?? 0,
                Text = () => UiStrings.FormatTutorial_Md_Colors(menu: UiStrings.Model_SourceColorChange),
                Target = () => D()?.TutorialSourceColors,
                MenuItem = () => TutorialSpotlight.OpenMenuItem(UiStrings.Model_SourceColorChange),
                Done = () => D() is { } d && d.TutorialChangedColors > colorsBefore,
            },
            new()
            {
                Id = "model.output",
                Enter = () => sizeBefore = D()?.TutorialSize ?? default,
                Text = () => UiStrings.Tutorial_Md_Output,
                Target = () => D()?.TutorialOutput,
                Done = () => D() is { } d && d.TutorialSize != sizeBefore,
            },
            new()
            {
                Id = "model.look",
                Text = () => UiStrings.Tutorial_Md_Look,
                Target = () => D()?.TutorialSettings,
                PassThrough = true,
            },
            new()
            {
                // 가져오기로 창이 닫히면(취소로 닫으면 그만둔 것) 다음(캔버스 보기)으로 — 그린 프레임은 창이 닫힌 뒤 진행 창을 거쳐 새 탭으로 열린다.
                Id = "model.import",
                Text = () => UiStrings.FormatTutorial_Md_Import(button: UiStrings.Model_Import),
                Target = () => D()?.TutorialImport,
                Done = () => D() is null && opened is { DialogResult: DialogResult.OK },
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // 마침: 가져온 프레임이 들어간 캔버스를 비추고 자유롭게 고쳐 보라고 한다('끝내기').
                Id = "model.done",
                Text = () => UiStrings.Tutorial_Md_Done,
                Target = () => _canvas,
            },
        ];
    }

    // ───────────── 효과 쓰기 ─────────────

    /// <summary>
    /// 효과 쓰기: 예시 픽셀 그림(5프레임, 들썩임)을 새 탭(저장하지 않는 문서)으로 열고, 타임라인에서 3~5프레임 칸 끌어 고르기 → 도구 막대의 효과 단추로 효과 목록 열기
    /// → 외곽선 상자를 고른 칸으로 끌어 놓거나 두 번 누르기(그 프레임에만 들어감) → 오른쪽 효과 설정에서 값 바꾸기 → 타임라인 효과 줄 안내
    /// → 효과 목록 '추가 다운로드'로 상점(홈페이지 에셋 페이지) 열기(누르면 끝).
    /// </summary>
    private List<TutorialStep> EffectsTutorial()
    {
        // 효과를 넣을 프레임(0부터): 3~5프레임.
        const int From = 2, To = 4;
        SpriteProject? example = null;
        int effectsBefore = 0;
        LayerEffect[] snapshot = [];
        bool sectionShown = false, storeOpened = false;
        Button? hooked = null;
        int EffectCount() => example?.Layers.Sum(l => l.Effects.Count) ?? 0;
        LayerEffect[] Effects() => example?.Layers.SelectMany(l => l.Effects).ToArray() ?? [];
        bool Current() => example is not null && ReferenceEquals(_ctx.Project, example);
        (Control Owner, Rect Area)? Cels() => Current() ? _timeline.TutorialCels(0, From, To) : null;
        return
        [
            new()
            {
                Id = "effects.select",
                Enter = () =>
                {
                    example = TutorialEffectProject();
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                    // 효과 목록이 이미 열려 있으면 닫아 두고 다음 단계에서 단추로 직접 열게 한다.
                    if (_effectListShown) SetLeftPanes(_filesShown, false, animate: false);
                },
                Clips = [new("effects-select")],
                Text = () => UiStrings.FormatTutorial_Fx_Select(from: From + 1, to: To + 1),
                TargetArea = Cels,
                DragAcross = true,
                Done = () => Current() && _timeline.TutorialCelsSelected(0, From, To),
                Required = true,
                // 세 칸을 끌어 고르고 놓으면 바로 다음(효과 목록 열기)으로.
                AutoAdvance = true,
            },
            new()
            {
                Id = "effects.open",
                Text = () => UiStrings.Tutorial_Fx_Open,
                Target = () => _tbEffects,
                // 단추를 눌러 목록이 열리면 바로 다음(외곽선 상자를 고른 칸으로 끌기)으로.
                Done = () => _effectListShown,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "effects.add",
                Clips = [new("effects-add")],
                Enter = () =>
                {
                    effectsBefore = EffectCount();
                    if (TutorialOutlineTile() is { } tile) Dispatcher.UIThread.Post(tile.BringIntoView, DispatcherPriority.Background);
                },
                Text = () => UiStrings.FormatTutorial_Fx_Add(effect: TutorialOutlineTile()?.Title ?? "", from: From + 1, to: To + 1),
                Target = () => TutorialOutlineTile(),
                // 끌어 놓을 자리: 고른 3~5프레임 칸(함께 빛나고, 상자에서 그 칸으로 끄는 모습을 되풀이해 보임).
                // 끌기는 상자가 포인터를 잡고 놓을 때 칸 자리를 계산하므로 덮개가 막아도 되고, 두 칸 모두 누르기가 통과한다.
                DropTarget = Cels,
                Done = () => Current() && EffectCount() > effectsBefore && !_timeline.PreviewingEffectDrop,
                Required = true,
                // 끌어다 놓거나 두 번 눌러 효과가 들어가면 바로 다음(효과 설정)으로.
                AutoAdvance = true,
            },
            new()
            {
                Id = "effects.adjust",
                Clips = [new("effects-adjust")],
                Enter = () =>
                {
                    snapshot = Effects();
                    sectionShown = false;
                },
                Text = () => UiStrings.Tutorial_Fx_Adjust,
                Target = () =>
                {
                    var section = _toolOptions.EffectSection;
                    // 오른쪽 독 아래쪽이라 처음 보일 때 한 번 그리로 굴린다.
                    if (section.IsEffectivelyVisible && !sectionShown)
                    {
                        sectionShown = true;
                        section.BringIntoView();
                    }
                    return section;
                },
                Done = () => Current() && !Effects().SequenceEqual(snapshot, ReferenceEqualityComparer.Instance),
                Required = true,
            },
            new()
            {
                Id = "effects.timeline",
                Clips = [new("effects-timeline")],
                Text = () => UiStrings.Tutorial_Fx_Timeline,
                Target = () => _timeline,
            },
            new()
            {
                Id = "effects.store",
                // 상점(홈페이지 에셋 페이지)을 열면 끝 — 누르기만 보고 브라우저는 단추가 연다.
                Enter = () =>
                {
                    storeOpened = false;
                    if (!_effectListShown) SetLeftPanes(_filesShown, true, animate: true);
                },
                Text = () => UiStrings.FormatTutorial_Fx_Store(button: UiStrings.EffectList_MoreDownloads),
                Target = () =>
                {
                    var store = EffectListButton(UiStrings.EffectList_MoreDownloads);
                    if (store is not null && !ReferenceEquals(store, hooked))
                    {
                        hooked = store;
                        store.Click += (_, _) => storeOpened = true;
                    }
                    return store;
                },
                Done = () => storeOpened,
                AutoAdvance = true,
            },
        ];
    }

    /// <summary>효과 목록 아래 단추(추가 다운로드 · 효과 만들기)를 단추 글로 찾는다(목록이 닫혀 있으면 null).</summary>
    private Button? EffectListButton(string label)
        => _effectListShown
            ? _effectList.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label))
            : null;

    /// <summary>'효과 쓰기' 예시 문서: 늑대 그림 5프레임(2·4프레임은 1px 위로 — 들썩임), 레이어 하나.</summary>
    private static SpriteProject TutorialEffectProject()
    {
        var image = PngIO.Load(ExtractTutorialFile(TutorialEffectExample));
        var up = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height - 1; y++)
            Array.Copy(image.Pixels, (y + 1) * image.Width, up.Pixels, y * image.Width, image.Width);
        PixelImage[] frames = [image, up, image.Clone(), up.Clone(), image.Clone()];
        return SpriteProject.FromFrames(frames, [.. frames.Select(_ => 120)], UiStrings.Tutorial_ExampleLayer);
    }

    /// <summary>효과 목록의 외곽선 상자(없으면 첫 상자, 목록이 닫혀 있으면 null).</summary>
    private EffectTile? TutorialOutlineTile()
    {
        if (!_effectListShown) return null;
        var tiles = _effectList.GetVisualDescendants().OfType<EffectTile>().Where(t => t.IsVisible).ToList();
        return tiles.FirstOrDefault(t => t.Key.Contains("outline", StringComparison.OrdinalIgnoreCase)) ?? tiles.FirstOrDefault();
    }

    // ───────────── AI로 그리기 ─────────────

    /// <summary>
    /// AI로 그리기: 위 막대의 AI 채팅 단추로 채팅 창 열기(메뉴 AI > AI 채팅, Ctrl+L도) → Claude·Codex 중 하나에 로그인(설치·로그인 안내는 채팅 창이 함, 연결되면 저절로 다음)
    /// → 그리기 설정 단추 → 입력 칸에 요청 적기(로그인 안 했으면 나중에 할 수 있다는 안내) → 레이어 자물쇠(잠근 레이어는 AI가 MCP로 못 바꿈)
    /// → 외부 AI 앱(Claude Desktop·Codex 등)을 MCP로 붙이는 메뉴(AI > 외부 AI 연결…).
    /// </summary>
    private List<TutorialStep> AiTutorial()
    {
        Ai.ChatWindow? Chat() => _chat is { IsVisible: true } chat ? chat : null;
        return
        [
            new()
            {
                Id = "ai.open",
                Text = () => UiStrings.FormatTutorial_Ai_Open(menu: MenuPath("AI/AI 채팅"), key: KeyText("AI/AI 채팅")),
                Target = () => _tbChat,
                Done = () => Chat() is not null,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "ai.login",
                Text = () => UiStrings.Tutorial_Ai_Login,
                Target = () => Chat()?.TutorialProviderToggle,
                Done = () => _chat?.AnyProviderReady == true,
                PassThrough = true,
            },
            new()
            {
                Id = "ai.settings",
                Text = () => UiStrings.Tutorial_Ai_Settings,
                Target = () => Chat()?.TutorialDrawSettings,
                PassThrough = true,
            },
            new()
            {
                Id = "ai.input",
                Text = () => _chat?.AnyProviderReady == true ? UiStrings.Tutorial_Ai_Ready : UiStrings.Tutorial_Ai_Later,
                // 로그인 전이면 입력 칸이 없어(화면에서 빠져 있음 — 떨어진 컨트롤도 IsEffectivelyVisible은 참) Claude·Codex 고르기 줄을 비춘다.
                Target = () => Chat() is { } chat
                    ? chat.TutorialInput is { IsEffectivelyVisible: true } input && TopLevel.GetTopLevel(input) is not null ? input : chat.TutorialProviderToggle
                    : null,
                PassThrough = true,
            },
            new()
            {
                Id = "ai.lock",
                Text = () => UiStrings.Tutorial_Ai_Lock,
                // 지금 레이어의 자물쇠(문서가 없으면 대상 없이 가운데 말풍선). 눌러 잠가 볼 수 있게.
                TargetArea = () => _ctx.HasProject ? _timeline.TutorialLock(_ctx.CurrentLayer) : null,
                PassThrough = true,
                // 채팅 창이 타임라인(자물쇠)과 말풍선을 가리므로 이 단계 동안만 숨긴다(다음 단계에서 다시 보임).
                HideWindows = () => [_chat],
            },
            new()
            {
                Id = "ai.external",
                Text = () => UiStrings.FormatTutorial_Ai_External(menu: MenuPath("AI/외부 AI 연결…")),
                Target = () => TopMenuOf("AI/외부 AI 연결…"),
                PassThrough = true,
            },
        ];
    }

    /// <summary>명령이 든 메뉴 줄의 위 메뉴(파일·AI 등 — 튜토리얼이 비춤).</summary>
    private MenuItem? TopMenuOf(string commandId) => Command(commandId).Item?.Parent as MenuItem;

    /// <summary>명령의 메뉴 길("AI > 외부 AI 연결…" — 바로 가기 밑줄 표시를 뺀 글).</summary>
    private string MenuPath(string commandId)
    {
        static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\(_\w\)|_", "").Trim();
        var info = Command(commandId).Info;
        return $"{Plain(info.Category)} > {Plain(info.Name)}";
    }
}
