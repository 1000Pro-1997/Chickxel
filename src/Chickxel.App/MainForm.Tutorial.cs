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
    /// 튜토리얼 창을 다시 띄워 그 카드에 "완료"를 보인다(다음 것을 고르게). 먼저 마칠 튜토리얼이 남았으면 시작하지 않고 튜토리얼 창(잠긴 카드에 선행 튜토리얼)을 띄운다.
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
        });
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
    /// 그림을 픽셀 아트로: 픽셀로 정리 창을 직접 열게 하고(메뉴 AI > AI 그림 픽셀로 정리…, 파일 칸 우클릭도 안내) 예시 그림을 넣어 그림 넣는 자리를 안내한 뒤
    /// → 준비 기다리기 → 설정 펼치기 → 간편 설정 값 바꾸기(여기까지 꼭 해야 넘어감)
    /// → 후보 칸 우클릭 '이 프리셋만 돌리기' → 재생(정리하기) → 최적화는 끝나야 결과가 나오고 취소하는 법 → 결과 색 빼기·더하기 → 넣기 안내.
    /// 최적화는 오래 걸려 끝나기를 기다리지 않는다: 돌리기 단계는 해 보면(최적화가 시작되면) 저절로, 안 해도 '다음'으로 넘어간다.
    /// </summary>
    private List<TutorialStep> PixelateTutorial()
    {
        string settingsBefore = "", paletteBefore = "";
        PixelateDialog? D() => _pixelate is { IsVisible: true } d ? d : null;
        return
        [
            new()
            {
                Id = "pixelate.open",
                Text = () => UiStrings.FormatTutorial_Px_Open(menu: MenuPath("AI/AI 그림 픽셀로 정리…"), file: UiStrings.Pixelate_MenuItem),
                Target = () => TopMenuOf("AI/AI 그림 픽셀로 정리…"),
                Done = () => D() is not null,
                Required = true,
                // 메뉴를 열고, 파일 칸에서 우클릭으로 열어도 되게.
                PassThrough = true,
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
                Id = "pixelate.wait",
                Text = () => UiStrings.Tutorial_Px_Wait,
                Target = () => D()?.TutorialCandidates,
                Done = () => D()?.TutorialReady == true,
                Required = true,
            },
            new()
            {
                Id = "pixelate.settings",
                Text = () => UiStrings.FormatTutorial_Px_Settings(button: UiStrings.Pixelate_Settings_Show),
                Target = () => D()?.TutorialSettingsToggle,
                Done = () => D()?.TutorialSettingsOpen == true,
                Required = true,
            },
            new()
            {
                Id = "pixelate.quick",
                Enter = () => settingsBefore = D()?.TutorialSettingsText ?? "",
                Text = () => UiStrings.Tutorial_Px_Quick,
                Target = () => D()?.TutorialQuickSection(),
                Done = () => D() is { } d && d.TutorialSettingsText != settingsBefore,
                Required = true,
            },
            new()
            {
                Id = "pixelate.rerun",
                Clip = "pixelate-rerun",
                Text = () => UiStrings.FormatTutorial_Px_Rerun(preset: D()?.TutorialIdleTile("shade")?.Preset ?? "", menu: UiStrings.Pixelate_Opt_RunPreset),
                Target = () => D() is { } d ? d.TutorialIdleTile("shade")?.Tile ?? d.TutorialCandidates : null,
                Done = () => D()?.TutorialRunning == true,
            },
            new()
            {
                Id = "pixelate.run",
                Text = () => UiStrings.FormatTutorial_Px_Run(button: UiStrings.Pixelate_Run),
                Target = () => D()?.TutorialRunButton,
                Done = () => D()?.TutorialRunning == true,
            },
            new()
            {
                Id = "pixelate.optimize",
                Clip = "pixelate-optimize",
                Text = () => UiStrings.Tutorial_Px_Optimize,
                Target = () => D()?.TutorialOptimizeButton,
                // 도는 중이면 이 단추가 '취소'(비춘 칸 안) — 우클릭 '모두 취소'도 그대로.
            },
            new()
            {
                Id = "pixelate.colors",
                Clip = "pixelate-colors",
                Enter = () => paletteBefore = D()?.TutorialPaletteText ?? "",
                Text = () => UiStrings.Tutorial_Px_Colors,
                Target = () => D()?.TutorialColors,
                Done = () => D() is { } d && d.TutorialPaletteText != paletteBefore,
                // + 단추를 누른 뒤 원본 칸(비춘 칸 밖)을 눌러 색을 더한다.
                PassThrough = true,
            },
            new()
            {
                Id = "pixelate.apply",
                Text = () => UiStrings.FormatTutorial_Px_Apply(button: D()?.TutorialApplyButton.Content as string ?? UiStrings.Pixelate_Apply),
                Target = () => D()?.TutorialApplyButton,
            },
        ];
    }

    // ───────────── 3D를 픽셀 아트로 ─────────────

    /// <summary>
    /// 3D를 픽셀 아트로: 메뉴 3D > 3D 모델 열기…로 3D 모델 창 열기(모달) → 예시 병아리 모델을 넣고 원본·결과 두 칸 안내 → 원본 모델을 끌어 돌리기
    /// → 출력(크기·프레임) → 프리셋·카메라·빛·픽셀 다듬기·외곽선 → 가져오기(새 탭으로 열림 — 창이 닫히면 마침).
    /// </summary>
    private List<TutorialStep> ModelTutorial()
    {
        string angleBefore = "";
        var sizeBefore = default(PixelSize);
        ModelImportDialog? D() => OwnedWindows.OfType<ModelImportDialog>().FirstOrDefault(w => w.IsVisible);
        return
        [
            new()
            {
                Id = "model.open",
                Text = () => UiStrings.FormatTutorial_Md_Open(menu: MenuPath("3D/3D 모델 열기…")),
                Target = () => TopMenuOf("3D/3D 모델 열기…"),
                Done = () => D() is not null,
                Required = true,
                PassThrough = true,
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
                Id = "model.import",
                Text = () => UiStrings.FormatTutorial_Md_Import(button: UiStrings.Model_Import),
                Target = () => D()?.TutorialImport,
            },
        ];
    }

    // ───────────── 효과 쓰기 ─────────────

    /// <summary>
    /// 효과 쓰기: 예시 픽셀 그림(5프레임, 들썩임)을 새 탭(저장하지 않는 문서)으로 열고, 도구 막대의 효과 단추 → 타임라인에서 3~5프레임 칸 끌어 고르기
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
                Id = "effects.open",
                Enter = () =>
                {
                    example = TutorialEffectProject();
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                },
                Text = () => UiStrings.Tutorial_Fx_Open,
                Target = () => _tbEffects,
                Done = () => _effectListShown,
                Required = true,
            },
            new()
            {
                Id = "effects.select",
                Text = () => UiStrings.FormatTutorial_Fx_Select(from: From + 1, to: To + 1),
                TargetArea = Cels,
                DragAcross = true,
                Done = () => Current() && _timeline.TutorialCelsSelected(0, From, To),
                Required = true,
            },
            new()
            {
                Id = "effects.add",
                Clip = "effects-add",
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
            },
            new()
            {
                Id = "effects.adjust",
                Clip = "effects-adjust",
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
                Clip = "effects-timeline",
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
                // 로그인 전이면 입력 칸이 없어 Claude·Codex 고르기 줄을 비춘다.
                Target = () => Chat() is { } chat ? chat.TutorialInput is { IsEffectivelyVisible: true } input ? input : chat.TutorialProviderToggle : null,
                PassThrough = true,
            },
            new()
            {
                Id = "ai.lock",
                Text = () => UiStrings.Tutorial_Ai_Lock,
                // 지금 레이어의 자물쇠(문서가 없으면 대상 없이 가운데 말풍선). 눌러 잠가 볼 수 있게.
                TargetArea = () => _ctx.HasProject ? _timeline.TutorialLock(_ctx.CurrentLayer) : null,
                PassThrough = true,
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
