using Avalonia.VisualTree;
using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 튜토리얼 창 "더 알아보기"의 작은 튜토리얼: 효과 만들기 · 내보내기 · 레이어와 프레임 · 레벨.
// 단계마다 비추는 컨트롤·넘어갈 조건은 저장소 루트 튜토리얼.md에 적어 두고, GuideShots tutorial-check로 점검한다.
public sealed partial class MainForm
{
    // ───────────── 효과 만들기 ─────────────

    /// <summary>
    /// 효과 만들기(AI로 그리기 · 효과 쓰기를 마친 뒤): 효과 목록 아래 '효과 만들기'로 창 열기 → 오른쪽 AI 칸에 효과를 말로 부탁 → 가운데 코드 칸
    /// → 미리보기 → 왼쪽 내 효과 목록과 넣기·올리기.
    /// </summary>
    private List<TutorialStep> EffectMakerTutorial()
    {
        EffectMakerWindow? M() => EffectMakerWindow.Current is { IsVisible: true } m ? m : null;
        return
        [
            new()
            {
                Id = "maker.open",
                Enter = () =>
                {
                    if (!_effectListShown) SetLeftPanes(_filesShown, true, animate: true);
                },
                Text = () => UiStrings.FormatTutorial_Mk_Open(button: UiStrings.EffectMaker_Title, menu: MenuPath("AI/효과 만들기")),
                Target = () => EffectListButton(UiStrings.EffectMaker_Title),
                Done = () => M() is not null,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "maker.ai",
                Text = () => UiStrings.Tutorial_Mk_Ai,
                Target = () => M()?.TutorialAi,
                PassThrough = true,
            },
            new()
            {
                Id = "maker.code",
                Text = () => UiStrings.Tutorial_Mk_Code,
                Target = () => M()?.TutorialCode,
                PassThrough = true,
            },
            new()
            {
                Id = "maker.preview",
                Text = () => UiStrings.Tutorial_Mk_Preview,
                Target = () => M()?.TutorialPreview,
                PassThrough = true,
            },
            new()
            {
                Id = "maker.use",
                Text = () => UiStrings.FormatTutorial_Mk_Use(insert: UiStrings.EffectMaker_Insert, upload: UiStrings.EffectMaker_Upload),
                Target = () => M()?.TutorialList,
                PassThrough = true,
            },
        ];
    }

    // ───────────── 내보내기 ─────────────

    /// <summary>
    /// 내보내기: 예시 그림(5프레임)을 새 탭으로 열고 파일 > 내보내기…로 창 열기 → 왼쪽 내보내기 목록(줄 하나 = 파일 하나) → 더하기로 줄 늘리기
    /// → 새 줄의 형식 바꾸기(시트 + GIF를 한 번에 여러 장) → 줄 앞 체크 = 저장할 때 저절로 다시 내보내기 → 내보내기 단추.
    /// </summary>
    private List<TutorialStep> ExportTutorial()
    {
        SpriteProject? example = null;
        int rowsBefore = 0;
        ExportFormat formatBefore = ExportFormat.Image;
        ExportDialog? D() => OwnedWindows.OfType<ExportDialog>().FirstOrDefault(w => w.IsVisible);
        int Rows() => example?.Export.Entries.Count ?? 0;
        return
        [
            new()
            {
                Id = "export.open",
                Enter = () =>
                {
                    example = TutorialEffectProject();
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                },
                Text = () => UiStrings.FormatTutorial_Ex_Open(menu: MenuPath("파일/내보내기…"), key: KeyText("파일/내보내기…")),
                Target = () => TopMenuOf("파일/내보내기…"),
                MenuItem = () => Command("파일/내보내기…").Item,
                Done = () => D() is not null,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "export.list",
                Text = () => UiStrings.Tutorial_Ex_List,
                Target = () => D()?.TutorialListPane,
            },
            new()
            {
                Id = "export.add",
                Enter = () => rowsBefore = Rows(),
                Text = () => UiStrings.FormatTutorial_Ex_Add(button: UiStrings.ExportList_Add),
                Target = () => D()?.TutorialAdd,
                Done = () => Rows() > rowsBefore,
                Required = true,
                // 누르면 바로 다음(새 줄의 형식 바꾸기)으로.
                AutoAdvance = true,
            },
            new()
            {
                Id = "export.format",
                Enter = () => formatBefore = D()?.TutorialFormatKind ?? ExportFormat.Image,
                Text = () => UiStrings.Tutorial_Ex_Format,
                Target = () => D()?.TutorialFormat,
                Done = () => D() is { } d && d.TutorialFormatKind != formatBefore,
            },
            new()
            {
                Id = "export.onsave",
                // 왼쪽: 줄 앞 체크 켜기, 오른쪽: 저장하면 GIF가 생김(파일 칸 프로젝트 묶음).
                Clips =
                [
                    new("export-onsave-check", () => UiStrings.Tutorial_Ex_OnSaveCheckClip),
                    new("export-onsave-save", () => UiStrings.FormatTutorial_Ex_OnSaveSaveClip(key: KeyText("파일/저장(+ 시트 내보내기)"))),
                ],
                Text = () => UiStrings.FormatTutorial_Ex_OnSave(menu: MenuPath("파일/저장(+ 시트 내보내기)"), key: KeyText("파일/저장(+ 시트 내보내기)")),
                Target = () => D()?.TutorialListPane,
                PassThrough = true,
            },
            new()
            {
                Id = "export.run",
                Text = () => UiStrings.Tutorial_Ex_Run,
                // 목록에서 고른 줄만 내보내짐(체크한 줄이어도 고르지 않았으면 안 내보냄).
                Clips = [new("export-run", () => UiStrings.Tutorial_Ex_RunClip)],
                Target = () => D()?.TutorialExport,
            },
        ];
    }

    // ───────────── 레이어와 프레임 ─────────────

    /// <summary>
    /// 레이어와 프레임: 예시 그림(4프레임 — 1·2 같은 그림, 3·4 1px 위)을 새 탭으로 열고 레이어 이름 칸 → 새 레이어 더하기 → 새 프레임 더하기
    /// → 프레임 번호 우클릭으로 지우기 → 1·2프레임 칸을 골라 셀 링크 → 링크된 칸 안내.
    /// </summary>
    private List<TutorialStep> LayersFramesTutorial()
    {
        SpriteProject? example = null;
        Layer? wolf = null;
        int before = 0;
        bool Current() => example is not null && ReferenceEquals(_ctx.Project, example);
        int Wolf() => Current() && wolf is not null ? example!.IndexOf(wolf) : -1;
        return
        [
            new()
            {
                Id = "layers.intro",
                Enter = () =>
                {
                    example = TutorialLayersProject();
                    wolf = example.Layers[0];
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                },
                Text = () => UiStrings.Tutorial_Lf_Layer,
                TargetArea = () => _timeline.TutorialLayerName(Wolf()),
            },
            new()
            {
                Id = "layers.add",
                Enter = () => before = example?.Layers.Count ?? 0,
                Text = () => UiStrings.Tutorial_Lf_AddLayer,
                TargetArea = () => Current() ? _timeline.TutorialAddLayer() : null,
                Done = () => Current() && example!.Layers.Count > before,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "frames.add",
                Enter = () => before = example?.Frames.Count ?? 0,
                Text = () => UiStrings.Tutorial_Lf_AddFrame,
                TargetArea = () => Current() ? _timeline.TutorialAddFrame() : null,
                // + 를 오른쪽으로 끌어 여러 개 더하는 길을 말풍선이 가리지 않게.
                KeepClear = () => Current() ? _timeline.TutorialAddFrameDrag() : null,
                Done = () => Current() && example!.Frames.Count > before,
                Required = true,
                // 프레임은 끌기를 놓을 때 더해져 끌기 도중에 넘어가지 않는다.
                AutoAdvance = true,
            },
            new()
            {
                Id = "frames.delete",
                Enter = () => before = example?.Frames.Count ?? 0,
                Text = () => UiStrings.Tutorial_Lf_DeleteFrame,
                TargetArea = () => Current() ? _timeline.TutorialFrameHeaders() : null,
                // 우클릭 메뉴가 열리면 '삭제'에 노란 테두리.
                MenuItem = () => TutorialSpotlight.OpenMenuItem(UiStrings.S_TimelinePanel_ShowFrameMenu_F31A4BEC0A),
                Done = () => Current() && example!.Frames.Count < before,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "frames.link",
                Text = () => UiStrings.FormatTutorial_Lf_Link(menu: UiStrings.Timeline_LinkCels),
                // 링크가 무엇인지(묶음마다 띠 색이 다름, 여러 묶음) 움짤로.
                Clips = [new("frames-link", () => UiStrings.Tutorial_Lf_LinkClip)],
                TargetArea = () => _timeline.TutorialCels(Wolf(), 0, 1),
                DragAcross = true,
                MenuItem = () => TutorialSpotlight.OpenMenuItem(UiStrings.Timeline_LinkCels),
                // '셀 링크'를 누르면 바로 다음으로.
                Done = () => Wolf() is >= 0 and var l && example!.Frames.Count > 1 && example.AreCelsLinked(l, 0, 1),
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "frames.linked",
                Text = () => UiStrings.FormatTutorial_Lf_Linked(menu: UiStrings.Timeline_UnlinkCels),
                // 링크된 한 칸에 그리면 다른 칸도 같이 바뀌는 모습.
                Clips = [new("frames-linked", () => UiStrings.Tutorial_Lf_LinkedClip)],
                TargetArea = () => _timeline.TutorialCels(Wolf(), 0, 1),
            },
        ];
    }

    /// <summary>'레이어와 프레임' 예시 문서: 늑대 그림 4프레임(1·2프레임은 같은 그림, 3·4프레임은 1px 위), 레이어 하나.</summary>
    private static SpriteProject TutorialLayersProject()
    {
        var image = PngIO.Load(ExtractTutorialFile(TutorialEffectExample));
        var up = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height - 1; y++)
            Array.Copy(image.Pixels, (y + 1) * image.Width, up.Pixels, y * image.Width, image.Width);
        PixelImage[] frames = [image, image.Clone(), up, up.Clone()];
        return SpriteProject.FromFrames(frames, [.. frames.Select(_ => 150)], UiStrings.Tutorial_ExampleLayer);
    }

    // ───────────── 레벨 ─────────────

    /// <summary>
    /// 레벨: 메뉴 줄의 계정 단추(Lv 표시, 경험치 얻는 법) → 갤러리에 올리기 단추(올리는 모습은 큰 움짤 — 실제로 올리지 않음) → 계정 단추에서 계정 관리 열기
    /// → 계정 창의 레벨 보상 '변경하기…' 누르기(누르면 끝 — 그 순간 새싹 보상 <see cref="LevelRewards.Tutorial"/>을 받고 나서 홈페이지 보상 쪽이 열림).
    /// </summary>
    private List<TutorialStep> LevelTutorial()
    {
        Control? Account() => _helpMenuRow.GetVisualDescendants().OfType<AccountButton>().FirstOrDefault(b => b.IsEffectivelyVisible);
        bool changed = false;
        return
        [
            new()
            {
                Id = "level.badge",
                Text = () => AppCommunity.Client.User is { Level: > 0 } user ? UiStrings.FormatTutorial_Lv_Badge(level: user.Level) : UiStrings.Tutorial_Lv_SignIn,
                Target = Account,
                // 로그인 전이면 눌러 로그인할 수 있게(계정 창은 모달이라 막지 않음).
                PassThrough = true,
            },
            new()
            {
                Id = "level.upload",
                Clips = [new("level-upload")],
                BigClip = true,
                Text = () => UiStrings.FormatTutorial_Lv_Upload(menu: MenuPath("파일/갤러리에 올리기…")),
                Target = () => _galleryButton,
            },
            new()
            {
                // 계정 단추 → 계정 관리…(계정이 없으면 단추가 바로 계정 창을 엶). 계정 창이 뜨면 다음으로.
                Id = "level.rewards",
                Text = () => UiStrings.FormatTutorial_Lv_Rewards(menu: UiStrings.Account_Manage),
                Target = Account,
                PassThrough = true,
                Done = () => AccountDialog.Shown is not null,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                // '변경하기…'를 누르면 새싹 보상을 받은 뒤 홈페이지 보상 쪽이 열리고 튜토리얼이 끝난다. 로그인 전이면 계정 창에서 로그인부터.
                Id = "level.change",
                Enter = () => RewardViews.BeforeChange = async () =>
                {
                    RewardViews.BeforeChange = null;
                    if (TutorialSpotlight.CurrentStepId != "level.change") return;
                    changed = true;
                    try
                    {
                        await AppCommunity.Client.ClaimTutorialRewardsAsync();
                    }
                    catch (CommunityException)
                    {
                        // 예전 서버·연결 끊김: 보상 없이 홈페이지만 연다.
                    }
                },
                Text = () => AccountDialog.Shown?.TutorialChange is not null
                    ? UiStrings.FormatTutorial_Lv_Change(button: UiStrings.Account_RewardsChange, title: LevelRewards.Find("title.sprout")!.Name, frame: LevelRewards.Find("frame.sprout")!.Name)
                    : UiStrings.Tutorial_Lv_ChangeSignIn,
                Target = () => AccountDialog.Shown?.TutorialChange ?? AccountDialog.Shown?.Content as Control,
                // 로그인 전이면 계정 창의 로그인 단추를 누를 수 있게.
                PassThrough = true,
                Done = () => changed,
                Required = true,
                AutoAdvance = true,
            },
        ];
    }

    // ───────────── 캔버스 다루기 ─────────────

    /// <summary>
    /// 캔버스 다루기: 예시 늑대 5프레임을 새 탭으로 열고 휠 확대·가운데 끌기(스페이스+끌기) 옮기기 → Ctrl 누른 채 끌기(잠깐 옮기기 — 오른쪽 끌기 포함)·Ctrl+휠 붓 크기
    /// → Ctrl+Alt+C 캔버스 크기(왼쪽 끌기 = 그림까지, 오른쪽 끌기 = 캔버스만, Enter 적용 · Esc 취소) → Alt+Shift+C 레이어 전체 변형.
    /// </summary>
    private List<TutorialStep> CanvasTutorial()
    {
        SpriteProject? example = null;
        bool Current() => example is not null && ReferenceEquals(_ctx.Project, example);
        Control? Canvas() => Current() ? _canvas : null;
        return
        [
            new()
            {
                Id = "canvas.view",
                // 처음 탭을 열면 화면에 맞추며 배율이 바뀌므로 넘어갈 조건 없이 '다음'으로.
                Enter = () =>
                {
                    example = TutorialEffectProject();
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                },
                Text = () => UiStrings.Tutorial_Cv_View,
                Target = Canvas,
            },
            new()
            {
                Id = "canvas.ctrl",
                Text = () => UiStrings.Tutorial_Cv_Ctrl,
                // 왼쪽·오른쪽 단추로 끈 결과를 나란히(피벗이 같이 가는지).
                Clips = [new("canvas-ctrl-left", () => UiStrings.Tutorial_Cv_CtrlLeftClip), new("canvas-ctrl-right", () => UiStrings.Tutorial_Cv_CtrlRightClip)],
                // 캔버스를 가리지 않고 옆에 들어가게 좁게(움짤은 줄여 맞춤).
                BubbleWidth = 450,
                Target = Canvas,
            },
            new()
            {
                Id = "canvas.resize",
                Text = () => UiStrings.FormatTutorial_Cv_Resize(key: KeyText("편집/캔버스 크기…"), menu: MenuPath("편집/캔버스 크기…")),
                Target = Canvas,
                Done = () => Current() && _ctx.IsCanvasResizing,
                Required = true,
                // 캔버스 크기 모드에 들어가면 바로 다음(손잡이 설명).
                AutoAdvance = true,
                PassThrough = true,
            },
            new()
            {
                Id = "canvas.resize.edit",
                Text = () => UiStrings.Tutorial_Cv_ResizeEdit,
                // 손잡이를 왼쪽·오른쪽 단추로 끈 결과를 나란히(그림까지 / 캔버스만).
                Clips = [new("canvas-resize-left", () => UiStrings.Tutorial_Cv_ResizeLeftClip), new("canvas-resize-right", () => UiStrings.Tutorial_Cv_ResizeRightClip)],
                // 손잡이를 끌 캔버스를 가리지 않고 옆에 들어가게 좁게(움짤은 줄여 맞춤).
                BubbleWidth = 450,
                Target = Canvas,
                Done = () => Current() && !_ctx.IsCanvasResizing,
                Required = true,
                // 적용하거나 취소하면 바로 다음.
                AutoAdvance = true,
                // 오른쪽 도구 설정의 확인·취소 단추도 쓸 수 있게.
                PassThrough = true,
            },
            new()
            {
                Id = "canvas.layer",
                Text = () => UiStrings.FormatTutorial_Cv_Layer(key: KeyText("편집/레이어 크기…"), menu: MenuPath("편집/레이어 크기…")),
                Target = Canvas,
                Done = () => Current() && _ctx.IsLayerTransform,
                Required = true,
                // 레이어 전체 변형에 들어가면 바로 다음(주황 상자 설명).
                AutoAdvance = true,
                PassThrough = true,
            },
            new()
            {
                Id = "canvas.layer.edit",
                Text = () => UiStrings.FormatTutorial_Cv_LayerEdit(key: KeyText("편집/변형(회전·크기)…")),
                // 한 번 바꾸면 모든 프레임이 같이 바뀌는 모습(캔버스 + 타임라인 칸).
                Clips = [new("canvas-layer-frames", () => UiStrings.Tutorial_Cv_LayerFramesClip)],
                // 캔버스 옆 자리에 들어가는 가장 넓은 폭(움짤 가로가 넓어 400보다 크게).
                BubbleWidth = 460,
                Target = Canvas,
                Done = () => Current() && !_ctx.IsLayerTransform,
                Required = true,
                PassThrough = true,
            },
        ];
    }

    // ───────────── 타일맵 ─────────────

    /// <summary>
    /// 타일맵: 빈 예시 그림(20×12 칸)을 새 탭으로 열고 타임라인의 레이어 이름 우클릭 > 새로 만들기 > 새 타일맵 레이어(메뉴 프레임·레이어 > 새 타일맵 레이어·Alt+Shift+N도)로
    /// 타일맵 레이어 만들기(만들면 바로 다음) → 왼쪽 타일셋에서 타일 고르기 → 타일 도구 → 캔버스에 칠하기 → 칸 격자 단추.
    /// </summary>
    private List<TutorialStep> TilemapTutorial()
    {
        SpriteProject? example = null;
        int placedBefore = 0;
        int picksBefore = 0;
        bool Current() => example is not null && ReferenceEquals(_ctx.Project, example);
        bool OnTilemap() => Current() && _ctx.IsTilemapSelected;
        int Placed() => OnTilemap() ? _ctx.CurrentTiles?.Map.Count ?? 0 : 0;
        return
        [
            new()
            {
                Id = "tile.create",
                Enter = () =>
                {
                    example = new SpriteProject(20 * 16, 12 * 16);
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                },
                Text = () => UiStrings.FormatTutorial_Tm_Create(
                    newMenu: UiStrings.Timeline_NewMenu.Replace("_", ""), item: UiStrings.Tile_MenuAddLayer.Replace("_", ""),
                    menu: MenuPath("프레임·레이어/새 타일맵 레이어…"), key: KeyText("프레임·레이어/새 타일맵 레이어…")),
                // 타임라인의 지금 레이어 이름 칸을 비추고, 우클릭 메뉴의 '새로 만들기'를 펼치면 '새 타일맵 레이어'가 빛난다.
                TargetArea = () => Current() ? _timeline.TutorialLayerName(_ctx.CurrentLayer) : null,
                MenuItem = () => _timeline.TutorialTilemapMenuItem,
                Done = OnTilemap,
                Required = true,
                // 타일맵 레이어를 만들면(우클릭 메뉴·메뉴 줄·단축키 어느 것이든) 바로 다음(타일 고르기)으로.
                AutoAdvance = true,
                // 우클릭 메뉴를 열 수 있게(메뉴는 비춘 칸 밖에 뜸).
                PassThrough = true,
            },
            new()
            {
                Id = "tile.tileset",
                Enter = () => picksBefore = _ctx.TilePicks,
                Text = () => UiStrings.Tutorial_Tm_Tileset,
                Target = () => OnTilemap() ? _tilesetPanel : null,
                // 타일 칸(붓에 담김)이나 자동 타일 세트를 하나 고를 때까지 '다음'이 없고, 고르면 저절로 다음으로(이미 골라 둔 것을 다시 눌러도).
                Done = () => OnTilemap() && _ctx.TilePicks > picksBefore,
                Required = true,
                AutoAdvance = true,
            },
            new()
            {
                Id = "tile.tools",
                Text = () => UiStrings.Tutorial_Tm_Tools,
                Target = () => OnTilemap() ? _tileToolBar : null,
            },
            new()
            {
                Id = "tile.paint",
                Enter = () => placedBefore = Placed(),
                Text = () => UiStrings.Tutorial_Tm_Paint,
                Target = () => OnTilemap() ? _canvas : null,
                Done = () => Placed() > placedBefore,
                Required = true,
                // 왼쪽 타일셋에서 다른 타일을 골라 가며 칠할 수 있게.
                PassThrough = true,
            },
            new()
            {
                Id = "tile.cells",
                Text = () => UiStrings.Tutorial_Tm_Cells,
                Target = () => OnTilemap() ? _cbTileCells : null,
                PassThrough = true,
            },
        ];
    }

    // ───────────── 레이어 묶기와 스킨 ─────────────

    /// <summary>
    /// 레이어 묶기와 스킨: 예시 늑대 4프레임을 새 탭으로 열고 늑대 레이어에 자식 레이어 만들기(새 자식 레이어… · Shift 끌어 놓기) → 묶음(들여쓰기·접기, 부모를 따름)
    /// → 스킨 추가(우클릭 > 스킨 추가 > 지금 그림 복제…) → 스킨 칩으로 바꾸기 → 겹쳐 쌓기(묶음 안 스킨은 갈아 끼움, 레이어는 순서대로 겹침).
    /// </summary>
    private List<TutorialStep> SkinsTutorial()
    {
        SpriteProject? example = null;
        Layer? wolf = null;
        int skinsBefore = 0;
        object? activeBefore = null;
        bool Current() => example is not null && ReferenceEquals(_ctx.Project, example);
        int Wolf() => Current() && wolf is not null ? example!.IndexOf(wolf) : -1;
        return
        [
            new()
            {
                Id = "skins.child",
                Enter = () =>
                {
                    example = TutorialLayersProject();
                    wolf = example.Layers[0];
                    OpenDetachedDocument(example, UiStrings.Tutorial_ExampleTitle, () => { });
                },
                Text = () => UiStrings.FormatTutorial_Sk_Child(menu: MenuPath("프레임·레이어/새 자식 레이어…"), key: KeyText("프레임·레이어/새 자식 레이어…")),
                // Shift 끌어 놓기로 자식 만드는 길을 움짤로.
                Clips = [new("skins-shift-drag", () => UiStrings.Tutorial_Sk_ShiftDragClip)],
                TargetArea = () => _timeline.TutorialLayerName(Wolf()),
                // 우클릭 메뉴: '새로 만들기' → 펼치면 '새 자식 레이어…'에 노란 테두리.
                MenuItem = () => TutorialSpotlight.OpenMenuItem(UiStrings.Timeline_NewMenu, UiStrings.S_TimelinePanel_ShowLayerMenu_804EB6B1D9),
                Done = () => Current() && example!.Layers.Any(l => l.Parent is not null),
                Required = true,
                AutoAdvance = true,
                // 우클릭 메뉴·메뉴 줄·다른 레이어 이름을 Shift로 끌어 놓기 모두 되게.
                PassThrough = true,
            },
            new()
            {
                Id = "skins.tree",
                Text = () => UiStrings.Tutorial_Sk_Tree,
                TargetArea = () => _timeline.TutorialLayerName(Wolf()),
            },
            new()
            {
                Id = "skins.add",
                Enter = () => skinsBefore = wolf?.Skins.Count ?? 0,
                Text = () => UiStrings.FormatTutorial_Sk_Add(menu: UiStrings.Timeline_AddSkinToLayer, copy: UiStrings.Timeline_AddSkinToLayer_Copy),
                TargetArea = () => _timeline.TutorialLayerName(Wolf()),
                // 우클릭 메뉴: '스킨 추가' → 펼치면 '지금 그림 복제…'에 노란 테두리.
                MenuItem = () => TutorialSpotlight.OpenMenuItem(UiStrings.Timeline_AddSkinToLayer, UiStrings.Timeline_AddSkinToLayer_Copy),
                Done = () => Current() && wolf!.Skins.Count > skinsBefore,
                Required = true,
                AutoAdvance = true,
                PassThrough = true,
            },
            new()
            {
                Id = "skins.switch",
                Enter = () => activeBefore = wolf?.ActiveSkin,
                Text = () => UiStrings.Tutorial_Sk_Switch,
                // 칩으로 스킨을 고르면 그림이 통째로 갈아 끼워지는 모습.
                Clips = [new("skins-switch", () => UiStrings.Tutorial_Sk_SwitchClip)],
                TargetArea = () => _timeline.TutorialLayerName(Wolf()),
                Done = () => Current() && !ReferenceEquals(wolf!.ActiveSkin, activeBefore),
                AutoAdvance = true,
                PassThrough = true,
            },
            new()
            {
                Id = "skins.stack",
                Text = () => UiStrings.Tutorial_Sk_Stack,
                // 순서대로 겹쳐 위가 가림 / 스킨 프리셋으로 여러 레이어를 한 번에.
                Clips = [new("skins-stack-order", () => UiStrings.Tutorial_Sk_StackOrderClip), new("skins-stack-preset", () => UiStrings.Tutorial_Sk_StackPresetClip)],
                Target = () => Current() ? _timeline : null,
            },
        ];
    }
}
