using System.Globalization;
using Avalonia.Media.Immutable;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 내보내기 설정 창(Ctrl+E, 파일 > 내보내기…; 파란 "내보내기"는 OK로 닫혀 호출한 쪽이 내보냄). 바꾸면 바로 프로젝트에 적용된다(설정 하나마다 실행취소 1단계).
/// 탭은 [내보내기 | Import Settings](Import Settings = Unity Texture Importer 인스펙터와 같은 항목·이름, .meta를 쓸 때만 보임).
/// 내보내기 탭 = 왼쪽 설정 칸(세로 스크롤) | 끌어서 너비를 바꾸는 나눔줄 | 오른쪽 미리보기(세로 스크롤). 설정 칸은 한 곳에만 둔다 — 위에서 아래로 접을 수 있는 묶음:
/// 탭 줄 오른쪽 "프리셋 ▾"(<see cref="ExportPresetBar"/>), 왼쪽 묶음: 공통(내보낼 결과 + 두 결과가 같이 쓰는 시트 설정 <see cref="ExportSettings.Common"/>) → 파일(저장 시 내보내기·폴더·형식·.meta)
/// → 프레임 시트(이름·Single 프레임·경로) → 부위 아틀라스(이름·원래 위치 유지·Sprite Library·경로) → 고급("시트별 다르게" — <see cref="ExportSettings.PartsOverride"/>, 켜면 시트 설정 칸이 한 벌 더).
/// 미리보기 = 만들어질 결과 파일(① 프레임 시트, ② 부위 아틀라스 — 파일마다 이름 + 칸 너비에 맞춘 시트 + 칸 목록).
/// 창 크기·위치·나눔 비율·접은 묶음은 <see cref="AppSettings"/>에 기억한다.
/// </summary>
public sealed class UnityExportDialog : DialogBase
{
    /// <summary>Import Settings 이름표 너비(Unity 항목 이름).</summary>
    private const int LabelWidth = 160;
    /// <summary>시트 설정·파일 묶음 이름표 너비.</summary>
    private const int SettingLabelWidth = 118;
    private const int DefaultWidth = 1100, DefaultHeight = 760, MinW = 760, MinH = 480;
    private static readonly IImmutableSolidColorBrush CellPenColor = new ImmutableSolidColorBrush(Color.FromArgb(200, 230, 200, 90));
    private static readonly string[] PivotNames = ["Center", "Top Left", "Top", "Top Right", "Left", "Right", "Bottom Left", "Bottom", "Bottom Right", "Custom"];

    private readonly EditorContext _ctx;
    private readonly AppSettings _settings;
    private readonly Action<float>? _ppuChanged;
    /// <summary>프로젝트 → 화면 갱신(설정 칸마다 하나).</summary>
    private readonly List<Action<SpriteProject>> _refresh = new();
    /// <summary>창을 열 때(또는 프로젝트가 바뀔 때) 기억해 둘 값(칸 이름 규칙 경고·예전 설정 안내).</summary>
    private readonly List<Action<SpriteProject>> _atOpen = new();
    /// <summary>글자 칸의 아직 반영하지 않은 값을 반영(창을 닫을 때).</summary>
    private readonly List<Action> _commitTexts = new();
    /// <summary>탭: [내보내기 | Import Settings].</summary>
    private readonly TabView _tabs = new();
    private const int MainPage = 0, ImportPage = 1;
    private readonly Grid _columns;
    private readonly Control _importPage;
    private readonly Expander _advanced;
    private readonly PreviewSection _framesSection, _partsSection;
    private readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly EventHandler _onReplaced, _onCurrent;
    private readonly EventHandler<ProjectChangedEventArgs> _onChanged;
    private bool _updating, _previewDirty = true, _customPivot;

    /// <param name="ctx">편집기 상태(현재 프로젝트).</param>
    /// <param name="settings">창 크기·위치·나눔 비율·접은 묶음을 기억할 앱 설정.</param>
    /// <param name="ppuChanged">사용자가 PPU를 바꿨을 때(마지막 PPU 기억용).</param>
    public UnityExportDialog(EditorContext ctx, AppSettings settings, Action<float>? ppuChanged = null) : base(UiStrings.S_UnityExportDialog_UnityExportDialog_91724FB578)
    {
        _ctx = ctx;
        _settings = settings;
        _ppuChanged = ppuChanged;
        CloseOnly();
        // Enter로 창이 닫히거나 내보내지지 않게 한다(닫기는 단추·Esc만). 이름 칸의 Enter는 그 값 반영.
        CancelButtonControl.IsDefault = false;
        // 닫기 옆 파란 "내보내기": 창을 닫고(입력 중인 값도 반영) 호출한 쪽이 내보낸다.
        var export = AddButton(UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD, DialogResult.OK);
        export.Background = AppTheme.AccentBrush;
        export.Foreground = Brushes.White;
        export.FontWeight = FontWeight.Bold;
        export.HorizontalContentAlignment = HorizontalAlignment.Center;
        export.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_C7A33C7D9E);
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = MinW;
        MinHeight = MinH;
        RestorePlacement();

        // ── 오른쪽: 미리보기 ──
        _framesSection = BuildSection("① ", null);
        // 부위 아틀라스가 따로 설정을 쓰면 그 설정이 있는 곳 안내(설정 칸은 왼쪽 "고급"에만 둔다).
        var overrideLine = Beside(Note(UiStrings.S_UnityExportDialog_UnityExportDialog_97F3AEFCEC),
            Ui.Button(UiStrings.S_UnityExportDialog_UnityExportDialog_777398C2AE, (_, _) => ShowAdvanced()).Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_A76A76E0B6));
        _refresh.Add(p => overrideLine.IsVisible = p.Export.PartsOverride is not null);
        _partsSection = BuildSection("② ", overrideLine);
        var previewHelp = Beside(
            Note(UiStrings.S_UnityExportDialog_UnityExportDialog_D06F53C998),
            Ui.InfoBadge(UiStrings.S_UnityExportDialog_UnityExportDialog_434197E825 +
                 UiStrings.S_UnityExportDialog_UnityExportDialog_68A441DB4C));
        var preview = Ui.ScrollHost(Ui.Stack(previewHelp, _framesSection.Panel, _partsSection.Panel));

        // ── 왼쪽: 설정(위에서 아래로) ──
        var commonTitle = SectionTitle();
        var (files, frames, parts, advanced) = BuildExportSections();
        var framesBox = Section("frames", SectionTitle(UiStrings.S_UnityExportDialog_UnityExportDialog_D2793FD9C8), frames);
        var partsBox = Section("parts", SectionTitle(UiStrings.S_UnityExportDialog_UnityExportDialog_B18F4A0368), parts);
        _advanced = Section("advanced", SectionTitle(UiStrings.S_UnityExportDialog_UnityExportDialog_6A9E6CDC2F), advanced);
        _refresh.Add(p =>
        {
            // 고른 결과의 묶음만 보인다(둘 다 꺼진 예전 설정이면 프레임 시트 묶음).
            framesBox.IsVisible = FramesShown(p);
            partsBox.IsVisible = _advanced.IsVisible = PartsShown(p);
        });
        var settingsColumn = Ui.ScrollHost(Ui.Stack(
            Section("common", commonTitle, BuildCommonBlock(commonTitle)),
            Section("files", SectionTitle(UiStrings.S_UnityExportDialog_UnityExportDialog_F066DF325B), files),
            framesBox,
            partsBox,
            _advanced));

        double split = double.IsFinite(settings.ExportSplit) ? Math.Clamp(settings.ExportSplit, 0.2, 0.8) : 0.46;
        _columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(split, GridUnitType.Star) { MinWidth = 340 },
                new ColumnDefinition(6, GridUnitType.Pixel),
                new ColumnDefinition(1 - split, GridUnitType.Star) { MinWidth = 260 },
            },
        };
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            Background = AppTheme.BorderBrush,
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        }.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_22A1711719);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(preview, 2);
        _columns.Children.Add(settingsColumn);
        _columns.Children.Add(splitter);
        _columns.Children.Add(preview);

        _tabs.AddPage(UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD, _columns);
        _importPage = Ui.ScrollHost(BuildImportTab());
        _tabs.AddPage("Import Settings", _importPage);
        Opened += (_, _) => { if (_previewDirty) RebuildPreview(); };
        // Unity Import Settings는 .meta를 쓸 때만 의미가 있다(숨기면 내보내기 탭으로).
        _refresh.Add(p => _tabs.SetPageVisible(ImportPage, p.Export.WriteMeta != false));
        _tabs.SelectionChanged += (_, _) => { if (_tabs.SelectedIndex == MainPage && _previewDirty) RebuildPreview(); };

        // 대화상자 본문(StackPanel)은 높이를 주지 않으므로 탭을 바깥 DockPanel의 채우기 칸에 바로 둔다.
        // 탭 줄 오른쪽 끝: 프리셋 단추(누르면 저장·불러오기·관리·기본 설정 창).
        var presetBar = new ExportPresetBar(ctx, this);
        var presetFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new Border { Width = 320, Padding = new Thickness(4), Child = presetBar },
        };
        var presetButton = new Button
        {
            Content = Ui.IconLabel(VectorIcon.ChevronDown, UiStrings.S_UnityExportDialog_UnityExportDialog_DF3C668D29, iconAfter: true),
            Padding = new Thickness(10, 3),
            Margin = new Thickness(8, 0, 2, 1),
        }.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_73AC3FD6C5);
        presetButton.Click += (_, _) => PopupMenus.Show(presetButton, presetFlyout);
        _tabs.Strip.Trailing = presetButton;
        var root = (DockPanel)Content!;
        root.Children.Remove(Body);
        root.Children.Add(_tabs);

        _rebuild.Tick += (_, _) => { _rebuild.Stop(); RebuildPreview(); };
        _onReplaced = (_, _) => { _customPivot = false; RememberAtOpen(); RefreshAll(); ScheduleRebuild(); };
        _onChanged = (_, e) =>
        {
            RefreshAll();
            if (!(e.Change == ProjectChange.Pixels && _ctx.Project?.IsStrokeActive == true)) ScheduleRebuild();
        };
        _onCurrent = (_, _) => { RefreshAll(); HighlightCurrent(); };
        ctx.ProjectReplaced += _onReplaced;
        ctx.ProjectChanged += _onChanged;
        ctx.CurrentChanged += _onCurrent;
        RememberAtOpen();
        RefreshAll();
    }

    /// <summary>기억한 창 크기·위치(지금 어느 화면에도 걸치지 않으면 주인 창 가운데, 크기는 화면 작업 영역 안으로).</summary>
    private void RestorePlacement()
    {
        var s = _settings;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        double maxW = screen is null ? 3000 : screen.WorkingArea.Width / screen.Scaling;
        double maxH = screen is null ? 3000 : screen.WorkingArea.Height / screen.Scaling;
        Width = Math.Clamp(s.ExportWidth > 0 ? s.ExportWidth : DefaultWidth, MinW, Math.Max(MinW, maxW));
        Height = Math.Clamp(s.ExportHeight > 0 ? s.ExportHeight : DefaultHeight, MinH, Math.Max(MinH, maxH));
        var bounds = new PixelRect(s.ExportX, s.ExportY, (int)Width, (int)Height);
        if (s.ExportX >= 0 && s.ExportY >= 0 && Screens.All.Any(sc => sc.WorkingArea.Intersects(bounds)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.ExportX, s.ExportY);
        }
    }

    /// <summary>창 크기·위치·나눔 비율을 설정에 적는다(최대화·최소화 중이면 크기·위치는 그대로).</summary>
    private void SavePlacement()
    {
        double left = _columns.ColumnDefinitions[0].ActualWidth, right = _columns.ColumnDefinitions[2].ActualWidth;
        if (left > 0 && right > 0) _settings.ExportSplit = Math.Round(left / (left + right), 3);
        if (WindowState != WindowState.Normal) return;
        _settings.ExportX = Math.Max(0, Position.X);
        _settings.ExportY = Math.Max(0, Position.Y);
        if (ClientSize.Width > 0) _settings.ExportWidth = (int)Math.Round(ClientSize.Width);
        if (ClientSize.Height > 0) _settings.ExportHeight = (int)Math.Round(ClientSize.Height);
    }

    private void RememberAtOpen()
    {
        if (_ctx.Project is not { } p) return;
        foreach (var remember in _atOpen) remember(p);
    }

    /// <summary>고급 묶음을 펼치고 그리로 스크롤한다.</summary>
    private void ShowAdvanced()
    {
        _tabs.SelectedIndex = MainPage;
        _advanced.IsExpanded = true;
        Dispatcher.UIThread.Post(() => _advanced.BringIntoView(), DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // 닫기·Esc·창 닫기 단추로 닫아도 입력 중이던 이름·폴더가 반영되게.
        foreach (var commit in _commitTexts) commit();
        SavePlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        _ctx.ProjectReplaced -= _onReplaced;
        _ctx.ProjectChanged -= _onChanged;
        _ctx.CurrentChanged -= _onCurrent;
        _rebuild.Stop();
        _framesSection.Clear();
        _partsSection.Clear();
        base.OnClosed(e);
    }

    // ───────────── Import Settings ─────────────

    private Control BuildImportTab()
    {
        var mode = Choice(UiStrings.S_UnityExportDialog_BuildImportTab_803A527AC5, [SpriteMode.Single, SpriteMode.Multiple], ["Single", "Multiple"], p => p.Sprite.Mode, (p, v) => p.Sprite.Mode = v);
        var ppu = Ui.Num(1, 10000, 16, 80).Presets(1, 8, 16, 24, 32, 48, 64, 100, 128, 256).RecentKey("unity.ppu");
        ppu.ValueChanged += (_, _) =>
        {
            float v = (float)ppu.NumValue();
            if (EditSettings("PPU", p => Math.Abs(p.Sprite.PixelsPerUnit - v) > 0.0001f, p => p.Sprite.PixelsPerUnit = v)) _ppuChanged?.Invoke(v);
        };
        _refresh.Add(p => ppu.Value = (decimal)Math.Clamp(p.Sprite.PixelsPerUnit, 1, 10000));
        var mesh = Choice(UiStrings.S_UnityExportDialog_BuildImportTab_A9228F6E80, [true, false], ["Full Rect", "Tight"], p => p.Sprite.FullRectMesh, (p, v) => p.Sprite.FullRectMesh = v);
        var extrudeRow = Field("Extrude Edges", UiStrings.S_UnityExportDialog_BuildImportTab_E5A971BA01,
            IntSlider("Extrude Edges", 0, 32, [0, 1, 2, 3, 4, 6, 8, 16, 32], "px", "unity.extrude", p => p.Sprite.ExtrudeEdges, (p, v) => p.Sprite.ExtrudeEdges = v));
        _refresh.Add(p => SetEnabled(extrudeRow, !p.Sprite.FullRectMesh));

        // 피벗(Single 모드만): 프리셋 또는 Custom(X/Y). 피벗 도구(K)와 같은 프로젝트 기본 피벗.
        var pivot = Ui.Combo(220, PivotNames);
        var pivotX = Ui.Num(0, 1, 0.5m, 60, 3, 0.05m).Presets(0, 0.25m, 0.5m, 0.75m, 1).RecentKey("unity.pivot");
        var pivotY = Ui.Num(0, 1, 0.5m, 60, 3, 0.05m).Presets(0, 0.25m, 0.5m, 0.75m, 1).RecentKey("unity.pivot");
        var pivotRow = Field("Pivot", UiStrings.S_UnityExportDialog_BuildImportTab_11ED340A24, pivot);
        var customRow = Field("Custom Pivot", UiStrings.S_UnityExportDialog_BuildImportTab_FD5172C338, Ui.Row(Ui.Label("X"), pivotX, Ui.Label("Y"), pivotY));
        pivot.SelectionChanged += (_, _) =>
        {
            if (_updating || pivot.SelectedIndex < 0) return;
            var preset = (PivotPreset)pivot.SelectedIndex;
            _customPivot = preset == PivotPreset.Custom;
            if (_customPivot)
            {
                customRow.IsVisible = true;
                return;
            }
            var v = Pivot.FromPreset(preset);
            if (!EditSettings(UiStrings.S_UnityExportDialog_BuildImportTab_9DE48FB914, p => p.Sprite.DefaultPivot != v, p => p.Sprite.DefaultPivot = v)) RefreshAll();
        };
        void ApplyCustomPivot()
        {
            var v = new Pivot((float)pivotX.NumValue(), (float)pivotY.NumValue());
            EditSettings(UiStrings.S_UnityExportDialog_BuildImportTab_9DE48FB914, p => p.Sprite.DefaultPivot != v, p => p.Sprite.DefaultPivot = v);
        }
        pivotX.ValueChanged += (_, _) => ApplyCustomPivot();
        pivotY.ValueChanged += (_, _) => ApplyCustomPivot();
        _refresh.Add(p =>
        {
            var v = p.Sprite.DefaultPivot;
            bool single = p.Sprite.Mode == SpriteMode.Single;
            bool custom = _customPivot || v.Preset == PivotPreset.Custom;
            pivot.SelectedIndex = custom ? (int)PivotPreset.Custom : (int)v.Preset;
            pivotX.Value = (decimal)Math.Clamp(v.X, 0, 1);
            pivotY.Value = (decimal)Math.Clamp(v.Y, 0, 1);
            pivotRow.IsVisible = single;
            customRow.IsVisible = single && custom;
        });

        var alphaTransparencyRow = Field("Alpha Is Transparency", UiStrings.S_UnityExportDialog_BuildImportTab_4E85B1FB7D,
            Toggle("Alpha Is Transparency", p => p.Sprite.AlphaIsTransparency, (p, v) => p.Sprite.AlphaIsTransparency = v));
        _refresh.Add(p => SetEnabled(alphaTransparencyRow, p.Sprite.AlphaSource != SpriteAlphaSource.None));
        var advanced = new Expander
        {
            Header = "Advanced",
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 2),
            Content = Ui.Stack(
                Field("sRGB (Color Texture)", UiStrings.S_UnityExportDialog_BuildImportTab_4E7381E325,
                    Toggle("sRGB", p => p.Sprite.SRgb, (p, v) => p.Sprite.SRgb = v)),
                Field("Alpha Source", UiStrings.S_UnityExportDialog_BuildImportTab_5D08A65AE0,
                    Choice("Alpha Source", [SpriteAlphaSource.None, SpriteAlphaSource.InputTextureAlpha, SpriteAlphaSource.FromGrayScale],
                        ["None", "Input Texture Alpha", "From Gray Scale"], p => p.Sprite.AlphaSource, (p, v) => p.Sprite.AlphaSource = v)),
                alphaTransparencyRow,
                Field("Read/Write", UiStrings.S_UnityExportDialog_BuildImportTab_F4B1F5514C,
                    Toggle("Read/Write", p => p.Sprite.Readable, (p, v) => p.Sprite.Readable = v)),
                Field("Generate Mip Maps", UiStrings.S_UnityExportDialog_BuildImportTab_D8CF27C662,
                    Toggle(UiStrings.S_UnityExportDialog_BuildImportTab_8C05EBC81F, p => p.Sprite.GenerateMipMaps, (p, v) => p.Sprite.GenerateMipMaps = v))),
        };

        // 플랫폼(Default) 설정
        int[] maxSizes = [0, .. SpriteSettings.MaxTextureSizes];
        string[] maxSizeNames = ["Auto (fit)", .. SpriteSettings.MaxTextureSizes.Select(s => s.ToString(CultureInfo.InvariantCulture))];
        var qualityRow = Field("Compressor Quality", UiStrings.S_UnityExportDialog_BuildImportTab_DA56805EBD,
            IntSlider("Compressor Quality", 0, 100, [0, 25, 50, 75, 90, 100], "%", "unity.crunch", p => p.Sprite.CompressionQuality, (p, v) => p.Sprite.CompressionQuality = v));
        _refresh.Add(p => SetEnabled(qualityRow, p.Sprite.CrunchCompression));
        var platform = Ui.Group("Default", Ui.Stack(
            Field("Max Size", UiStrings.S_UnityExportDialog_BuildImportTab_293AA29819,
                Choice("Max Size", maxSizes, maxSizeNames, p => p.Sprite.MaxTextureSize, (p, v) => p.Sprite.MaxTextureSize = v)),
            Field("Resize Algorithm", UiStrings.S_UnityExportDialog_BuildImportTab_BC6EA477A1,
                Choice("Resize Algorithm", [TextureResize.Mitchell, TextureResize.Bilinear], ["Mitchell", "Bilinear"],
                    p => p.Sprite.ResizeAlgorithm, (p, v) => p.Sprite.ResizeAlgorithm = v)),
            Field("Format", UiStrings.S_UnityExportDialog_BuildImportTab_3CC18E00F9,
                Choice("Format", [TextureFormatChoice.Automatic, TextureFormatChoice.RGBA32, TextureFormatChoice.RGB24], ["Automatic", "RGBA 32 bit", "RGB 24 bit"],
                    p => p.Sprite.Format, (p, v) => p.Sprite.Format = v)),
            Field("Compression", UiStrings.S_UnityExportDialog_BuildImportTab_CB8D53E11F,
                Choice("Compression", [TextureCompressionChoice.None, TextureCompressionChoice.LowQuality, TextureCompressionChoice.NormalQuality, TextureCompressionChoice.HighQuality],
                    ["None", "Low Quality", "Normal Quality", "High Quality"], p => p.Sprite.Compression, (p, v) => p.Sprite.Compression = v)),
            Field("Use Crunch Compression", UiStrings.S_UnityExportDialog_BuildImportTab_10E5A1675F,
                Toggle(UiStrings.S_UnityExportDialog_BuildImportTab_14113A283A, p => p.Sprite.CrunchCompression, (p, v) => p.Sprite.CrunchCompression = v)),
            qualityRow));

        var note = Ui.Label(UiStrings.S_UnityExportDialog_BuildImportTab_1D025CB8E9);
        note.Foreground = AppTheme.DimTextBrush;
        note.TextWrapping = TextWrapping.Wrap;

        return Ui.Stack(
            note,
            Field("Texture Type", UiStrings.S_UnityExportDialog_BuildImportTab_7223CFD811, ReadOnlyCombo("Sprite (2D and UI)")),
            Field("Texture Shape", UiStrings.S_UnityExportDialog_BuildImportTab_59FAD55BAB, ReadOnlyCombo("2D")),
            Rule(),
            Field("Sprite Mode", UiStrings.S_UnityExportDialog_BuildImportTab_97B943F75D, mode),
            Field("Pixels Per Unit", UiStrings.S_UnityExportDialog_BuildImportTab_732DC45A45, ppu),
            Field("Mesh Type", UiStrings.S_UnityExportDialog_BuildImportTab_A9E5086574, mesh),
            extrudeRow,
            pivotRow,
            customRow,
            Field("Generate Physics Shape", UiStrings.S_UnityExportDialog_BuildImportTab_B36C666A23,
                Toggle("Physics Shape", p => p.Sprite.GeneratePhysicsShape, (p, v) => p.Sprite.GeneratePhysicsShape = v)),
            advanced,
            Rule(),
            Field("Wrap Mode", UiStrings.S_UnityExportDialog_BuildImportTab_E72E67B19F,
                Choice("Wrap Mode", [TextureWrap.Repeat, TextureWrap.Clamp, TextureWrap.Mirror, TextureWrap.MirrorOnce], ["Repeat", "Clamp", "Mirror", "Mirror Once"],
                    p => p.Sprite.WrapMode, (p, v) => p.Sprite.WrapMode = v)),
            Field("Filter Mode", UiStrings.S_UnityExportDialog_BuildImportTab_B2432FCBD9,
                Choice("Filter Mode", [TextureFilter.Point, TextureFilter.Bilinear, TextureFilter.Trilinear], ["Point (no filter)", "Bilinear", "Trilinear"],
                    p => p.Sprite.FilterMode, (p, v) => p.Sprite.FilterMode = v)),
            Field("Aniso Level", UiStrings.S_UnityExportDialog_BuildImportTab_1AD908EBD2,
                IntSlider("Aniso Level", 0, 16, [0, 1, 2, 4, 8, 16], "", "unity.aniso", p => p.Sprite.AnisoLevel, (p, v) => p.Sprite.AnisoLevel = v)),
            platform);
    }

    // ───────────── 내보내기 ─────────────

    /// <summary>
    /// 왼쪽 설정 칸의 묶음 내용: 파일(두 결과 공통 — 저장 시 내보내기·폴더·형식·.meta), 프레임 시트(이름·Single 프레임·경로),
    /// 부위 아틀라스(이름·원래 위치 유지·Sprite Library·경로), 고급(시트별 다르게 + 따로 쓰는 시트 설정).
    /// </summary>
    private (Control Files, Control Frames, Control Parts, Control Advanced) BuildExportSections()
    {
        // ── 파일(두 결과 공통) ──
        var folder = NameText(UiStrings.S_UnityExportDialog_BuildExportSections_526698768E, UiStrings.S_UnityExportDialog_BuildExportSections_28B6043259, 240, p => p.Export.Folder, (p, v) => p.Export.Folder = v);
        var browse = Ui.Button("…", (_, _) => BrowseFolder(folder), 26).Tip(UiStrings.S_UnityExportDialog_BuildExportSections_987E11A4E7);
        var format = Choice(UiStrings.S_UnityExportDialog_BuildExportSections_F80B388117, [ExportImageFormat.Png, ExportImageFormat.Jpg, ExportImageFormat.Tga, ExportImageFormat.Bmp],
            [UiStrings.S_UnityExportDialog_BuildExportSections_2653689DDE, UiStrings.S_UnityExportDialog_BuildExportSections_59AD7EA467, "TGA", "BMP"], p => p.Export.ImageFormat, (p, v) => p.Export.ImageFormat = v, 170);
        var jpegLabel = Ui.Label(UiStrings.S_UnityExportDialog_BuildExportSections_23E8B015EA);
        var jpegQuality = IntNum(UiStrings.S_UnityExportDialog_BuildExportSections_0FBC1AB03F, 1, 100, 54, p => p.Export.JpegQuality, (p, v) => p.Export.JpegQuality = v)
            .Presets(50, 60, 70, 75, 80, 85, 90, 95, 100).Unit("%").RecentKey("export.jpeg");
        jpegQuality.Tip(UiStrings.S_UnityExportDialog_BuildExportSections_801EC51603);
        _refresh.Add(p => jpegLabel.IsVisible = jpegQuality.IsVisible = p.Export.ImageFormat == ExportImageFormat.Jpg);
        var meta = Seg<bool?>(UiStrings.S_UnityExportDialog_BuildExportSections_60EBD042A1, [false, null, true], [UiStrings.S_UnityExportDialog_BuildExportSections_F059194EC7, UiStrings.S_UnityExportDialog_BuildExportSections_65BEF24266, UiStrings.S_UnityExportDialog_BuildExportSections_D424AEA3F1], p => p.Export.WriteMeta, (p, v) => p.Export.WriteMeta = v)
            .SetTip(0, UiStrings.S_UnityExportDialog_BuildExportSections_E38FCCF1CA)
            .SetTip(1, UiStrings.S_UnityExportDialog_BuildExportSections_60ED6B1DA3)
            .SetTip(2, UiStrings.S_UnityExportDialog_BuildExportSections_7981FFA3CA);
        var fileBox = Ui.Stack(
            Setting(UiStrings.S_UnityExportDialog_BuildExportSections_2508675F56, UiStrings.S_UnityExportDialog_BuildExportSections_0E4510EAD7,
                OnOff(UiStrings.S_UnityExportDialog_BuildExportSections_2508675F56, p => p.Export.ExportOnSave, (p, v) => p.Export.ExportOnSave = v)),
            Setting(UiStrings.S_UnityExportDialog_BuildExportSections_6D98D40F29, UiStrings.S_UnityExportDialog_BuildExportSections_5E17111839, Line(folder, browse)),
            Setting(UiStrings.S_UnityExportDialog_BuildExportSections_F80B388117, UiStrings.S_UnityExportDialog_BuildExportSections_A3E683A3EB, Line(format, jpegLabel, jpegQuality)),
            Setting("Unity .meta", UiStrings.S_UnityExportDialog_BuildExportSections_4425590C77, meta));

        // ── 프레임 시트 ──
        var singleFrame = Ui.Num(1, 9999, 1, 54).RecentKey("export.frame");
        singleFrame.ValueChanged += (_, _) =>
        {
            int v = (int)singleFrame.NumValue() - 1;
            EditSettings(UiStrings.S_UnityExportDialog_BuildExportSections_130FEEBB20, p => p.Export.SingleFrameIndex != v, p => p.Export.SingleFrameIndex = v);
        };
        var singleRow = Setting(UiStrings.S_UnityExportDialog_BuildExportSections_130FEEBB20, UiStrings.S_UnityExportDialog_BuildExportSections_8F1FF0667F, singleFrame);
        _refresh.Add(p =>
        {
            singleFrame.Maximum = Math.Max(1, p.Frames.Count);
            singleFrame.Presets(Ui.FrameNumbers(p.Frames.Count));
            singleFrame.Value = Math.Clamp(p.Export.SingleFrameIndex + 1, 1, Math.Max(1, p.Frames.Count));
            singleRow.IsVisible = p.Sprite.Mode == SpriteMode.Single;
        });
        var exportPath = PathLabel();
        var framesBox = Ui.Stack(
            Setting(UiStrings.S_UnityExportDialog_BuildExportSections_BE408B1AB8, UiStrings.S_UnityExportDialog_BuildExportSections_BCF7ED2A02,
                NameText(UiStrings.S_UnityExportDialog_BuildExportSections_83949D2A49, UiStrings.S_UnityExportDialog_BuildExportSections_AE9A23DCD2, 220, p => p.Export.FileName, (p, v) => p.Export.FileName = v)),
            singleRow,
            exportPath);

        // ── 부위 아틀라스 ──
        var keepRow = Setting(UiStrings.S_UnityExportDialog_BuildExportSections_90263CB3CF, UiStrings.S_UnityExportDialog_BuildExportSections_543B1162BD,
            OnOff(UiStrings.S_UnityExportDialog_BuildExportSections_90263CB3CF, p => p.Export.KeepImportedRects, (p, v) => p.Export.KeepImportedRects = v));
        var libraryRow = Setting("Sprite Library", null, Line(
            OnOff("Sprite Library", p => p.Export.WriteSpriteLibrary, (p, v) => p.Export.WriteSpriteLibrary = v),
            Ui.InfoBadge(UiStrings.S_UnityExportDialog_BuildExportSections_44908E5316 +
                 UiStrings.S_UnityExportDialog_BuildExportSections_87AC546C92 +
                 UiStrings.S_UnityExportDialog_BuildExportSections_EF4A7BEB35 +
                 UiStrings.S_UnityExportDialog_BuildExportSections_9119F759EB +
                 UiStrings.S_UnityExportDialog_BuildExportSections_6C1F44F368 +
                 UiStrings.S_UnityExportDialog_BuildExportSections_8C604F71D0)));
        var libraryNote = Setting("", null, Note(UiStrings.S_UnityExportDialog_BuildExportSections_B3B5CB7352));
        var partsPath = PathLabel();
        var libraryPath = PathLabel();

        var overrideRow = Setting(UiStrings.S_UnityExportDialog_BuildExportSections_ADA96235D6, null, Line(
            Seg(UiStrings.S_UnityExportDialog_BuildExportSections_ADA96235D6, [false, true], [UiStrings.S_UnityExportDialog_BuildExportSections_F059194EC7, UiStrings.S_UnityExportDialog_BuildExportSections_810DBA0E4E], p => p.Export.PartsOverride is not null,
                    (p, v) => p.Export.PartsOverride = v ? p.Export.Common.Clone() : null)
                .SetTip(0, UiStrings.S_UnityExportDialog_BuildExportSections_7E8861FA23)
                .SetTip(1, UiStrings.S_UnityExportDialog_BuildExportSections_30370BD8DD),
            Ui.InfoBadge(UiStrings.S_UnityExportDialog_BuildExportSections_2816B7AEDB +
                 UiStrings.S_UnityExportDialog_BuildExportSections_56D4255A96)));
        // 파일을 열 때부터 따로 설정이 있었으면(예전 파일을 옮긴 경우 등) 그 까닭과 되돌리는 단추.
        bool overrideAtOpen = false;
        _atOpen.Add(p => overrideAtOpen = p.Export.PartsOverride is not null);
        var migrated = Beside(
            Note(UiStrings.S_UnityExportDialog_BuildExportSections_C5222F3E88),
            Ui.Button(UiStrings.S_UnityExportDialog_BuildExportSections_E3C647CE07, (_, _) => EditSettings(UiStrings.S_UnityExportDialog_BuildExportSections_E3C647CE07, p => p.Export.PartsOverride is not null, p => p.Export.PartsOverride = null))
                .Tip(UiStrings.S_UnityExportDialog_BuildExportSections_D05205E66E));
        var overrideRows = SheetRows(p => p.Export.PartsOverride, overrideBlock: true);
        overrideRows.Margin = new Thickness(10, 2, 0, 0);
        _refresh.Add(p =>
        {
            migrated.IsVisible = overrideAtOpen && p.Export.PartsOverride is not null;
            overrideRows.IsVisible = p.Export.PartsOverride is not null;
        });
        var partsBox = Ui.Stack(
            Setting(UiStrings.S_UnityExportDialog_BuildExportSections_BE408B1AB8, UiStrings.S_UnityExportDialog_BuildExportSections_B18FB8517E,
                NameText(UiStrings.S_UnityExportDialog_BuildExportSections_924DFE7AE7, UiStrings.S_UnityExportDialog_BuildExportSections_8C3B4A4899, 220, p => p.Export.PartsFileName, (p, v) => p.Export.PartsFileName = v)),
            keepRow,
            libraryRow,
            libraryNote,
            partsPath,
            libraryPath);
        var advancedBox = Ui.Stack(overrideRow, migrated, overrideRows);

        _refresh.Add(p =>
        {
            var parts = p.Export.Parts;
            keepRow.IsVisible = parts.Layout == SheetLayout.Packed;
            SetEnabled(keepRow, !parts.SplitParts);
            libraryNote.IsVisible = p.Export.WriteSpriteLibrary;
            if (p.Path is null)
            {
                exportPath.Text = UiStrings.S_UnityExportDialog_BuildExportSections_DB40109609;
                partsPath.Text = "";
                libraryPath.Text = "";
                return;
            }
            // 셀 내용은 보지 않는다(설정을 바꿀 때마다 불리므로 가볍게) — 그린 것이 없는 부위도 셈.
            var (frames, partPaths) = ProjectExport.GetOutputPaths(p, p.Path, assumePartContent: true);
            exportPath.Text = OutputText(p, frames, UiStrings.S_UnityExportDialog_BuildExportSections_6B06887D1E);
            partsPath.Text = OutputText(p, partPaths, UiStrings.S_UnityExportDialog_BuildExportSections_C0FA3B7B99, partsAtlas: true);
            var libraries = ProjectExport.GetLibraryPaths(p, p.Path, assumePartContent: true);
            libraryPath.IsVisible = p.Export.WriteSpriteLibrary;
            libraryPath.Text = libraries.Count == 0 ? UiStrings.S_UnityExportDialog_BuildExportSections_E5B4608A6B
                : $"→ {libraries[0]}" + (libraries.Count > 1 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_BuildExportSections_46BC96CB04,libraries.Count - 1) : "") + " (+ .meta)" +
                  (parts.Skins == SkinSource.Selected ? UiStrings.S_UnityExportDialog_BuildExportSections_DC750ED123 : "");
        });

        return (fileBox, framesBox, partsBox, advancedBox);
    }

    /// <summary>
    /// "공통" 묶음 내용: 내보낼 결과 + 두 결과가 같이 쓰는 시트 설정(<see cref="ExportSettings.Common"/>).
    /// 제목 <paramref name="title"/>은 지금 이 설정을 어느 결과가 쓰는지 알려 준다(부위 아틀라스가 따로 설정을 쓰면 프레임 시트만).
    /// </summary>
    private Control BuildCommonBlock(TextBlock title)
    {
        var output = Setting(UiStrings.S_UnityExportDialog_BuildCommonBlock_FE8EEAD4AB, null, OutputChoice());
        _refresh.Add(p =>
        {
            output.IsVisible = HasParts(p) || !p.Export.ExportFrames;
            bool parts = PartsShown(p), frames = FramesShown(p), partsCommon = parts && p.Export.PartsOverride is null;
            title.Text = frames && partsCommon ? UiStrings.S_UnityExportDialog_BuildCommonBlock_2AD6FF8A7C
                : frames ? (parts ? UiStrings.S_UnityExportDialog_BuildCommonBlock_273B7FFE21 : UiStrings.S_UnityExportDialog_BuildCommonBlock_C139B30E27)
                : partsCommon ? UiStrings.S_UnityExportDialog_BuildCommonBlock_AB2884C1DF
                : UiStrings.S_UnityExportDialog_BuildCommonBlock_D98FB679E2;
        });
        return Ui.Stack(output, SheetRows(p => p.Export.Common, overrideBlock: false));
    }

    /// <summary>
    /// 내보낼 결과(프레임 시트 / 부위 아틀라스 / 둘 다). 저장 값은 <see cref="ExportSettings.ExportFrames"/>·<see cref="ExportSettings.ExportParts"/> 두 개
    /// (둘 다 꺼진 예전 설정이면 아무것도 고르지 않은 모양).
    /// </summary>
    private TabStrip OutputChoice() => Seg(UiStrings.S_UnityExportDialog_OutputChoice_AB62772949, [0, 1, 2], [UiStrings.S_UnityExportDialog_OutputChoice_F37E64C73E, UiStrings.S_UnityExportDialog_OutputChoice_93F369B5AE, UiStrings.S_UnityExportDialog_OutputChoice_88366455B1],
            p => p.Export.ExportFrames && p.Export.ExportParts ? 2 : p.Export.ExportParts ? 1 : p.Export.ExportFrames ? 0 : -1,
            (p, v) =>
            {
                p.Export.ExportFrames = v != 1;
                p.Export.ExportParts = v != 0;
            })
        .SetTip(0, UiStrings.S_UnityExportDialog_OutputChoice_C4EC679FE3)
        .SetTip(1, UiStrings.S_UnityExportDialog_OutputChoice_08EC141D30)
        .SetTip(2, UiStrings.S_UnityExportDialog_OutputChoice_593A305BCD);

    /// <summary>부위 레이어가 있는지.</summary>
    private static bool HasParts(SpriteProject p) => p.Layers.Any(l => l.IsPart);

    /// <summary>부위 아틀라스를 만드는지(부위가 있고 켜짐).</summary>
    private static bool PartsShown(SpriteProject p) => HasParts(p) && p.Export.ExportParts;

    /// <summary>프레임 시트를 보이는지(켜짐, 또는 부위 아틀라스도 만들지 않는 예전 설정).</summary>
    private static bool FramesShown(SpriteProject p) => p.Export.ExportFrames || !PartsShown(p);

    /// <summary>
    /// 내보낼 경로 줄(파일이 여러 개면 첫 파일 + 개수): .meta를 쓰기로 했으면 이번에 쓰는지도.
    /// <paramref name="partsAtlas"/>면 Sprite Library를 쓸 때 메타를 항상 쓴다(<see cref="ProjectExport.ShouldWritePartsMeta"/>).
    /// </summary>
    private static string OutputText(SpriteProject p, IReadOnlyList<string> paths, string none, bool partsAtlas = false)
    {
        if (paths.Count == 0) return $"→ ({none})";
        var shown = paths.Count == 1
            ? paths[0]
            : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_OutputText_D443DAA729,paths[0],paths.Count - 1,string.Join(", ", paths.Skip(1).Take(3).Select(Path.GetFileName)),(paths.Count > 4 ? ", …" : ""));
        bool writes = partsAtlas ? ProjectExport.ShouldWritePartsMeta(p, paths[0]) : ProjectExport.ShouldWriteMeta(p, paths[0]);
        if (p.Export.WriteMeta == false && !writes) return $"→ {shown}";
        return $"→ {shown}" + (writes ? " (+ .meta)" : UiStrings.S_UnityExportDialog_OutputText_CD47DD0773);
    }

    private void BrowseFolder(TextBox box)
    {
        if (_ctx.Project is not { } p) return;
        string? initial = null;
        if (p.Path is { } projectPath)
        {
            var baseDir = Path.GetDirectoryName(projectPath)!;
            initial = p.Export.Folder is { } f ? Path.GetFullPath(Path.Combine(baseDir, f)) : baseDir;
            if (!Directory.Exists(initial)) initial = baseDir;
        }
        if (Ui.PickFolder(this, UiStrings.S_UnityExportDialog_BrowseFolder_7E04D74D15, initial) is not { } folder) return;
        if (p.Path is { } path)
        {
            var rel = Path.GetRelativePath(Path.GetDirectoryName(path)!, folder);
            if (!rel.StartsWith("..", StringComparison.Ordinal) || rel.Count(c => c == Path.DirectorySeparatorChar) < 3) folder = rel;
        }
        box.Text = folder == "." ? "" : folder;
        var v = NullIfEmpty(box.Text);
        EditSettings(UiStrings.S_UnityExportDialog_BrowseFolder_4ED7A7A3C1, pr => pr.Export.Folder != v, pr => pr.Export.Folder = v);
    }

    // ───────────── 설정 칸 만들기(바꾸면 실행취소 단위 편집, 프로젝트가 바뀌면 다시 읽음) ─────────────

    /// <summary>설정을 바꾸는 실행취소 단위 편집. 값이 같거나 화면 갱신 중이면 하지 않는다.</summary>
    private bool EditSettings(string label, Func<SpriteProject, bool> differs, Action<SpriteProject> apply)
    {
        if (_updating || _ctx.Project is not { } p || !differs(p)) return false;
        _ctx.Structural(() => p.Edit(label, ProjectChange.Settings, () => apply(p)));
        return true;
    }

    // ───────────── 시트 설정 칸(공통 / 부위 아틀라스 따로) ─────────────

    /// <summary>
    /// 시트 설정 칸 한 벌: 스킨(+ 스킨 고르기 창)·스킨 담기·배치·정렬·줄바꿈·칸 수·최대 가로·넣는 순서·따로 파일·칸 이름·간격·잘라내기·2의 거듭제곱.
    /// <paramref name="options"/>가 편집할 설정(공통 = <see cref="ExportSettings.Common"/>, 따로 = <see cref="ExportSettings.PartsOverride"/> — null이면 칸을 숨기므로 공통 값으로 채워 둠).
    /// <paramref name="overrideBlock"/>이면 부위 아틀라스만 쓰는 설정(착장 프리셋 없음, 실행취소 이름에 "부위 아틀라스").
    /// 한쪽 결과에만 의미 있는 칸(부위마다 따로 파일, 정렬의 부위 기준)은 그 결과가 이 설정을 쓸 때만 보인다.
    /// </summary>
    private StackPanel SheetRows(Func<SpriteProject, SheetOptions?> options, bool overrideBlock)
    {
        SheetOptions O(SpriteProject p) => options(p) ?? p.Export.Common;
        bool PartsUse(SpriteProject p) => overrideBlock || (PartsShown(p) && p.Export.PartsOverride is null);
        bool FramesUse(SpriteProject p) => !overrideBlock && FramesShown(p);
        string u = overrideBlock ? UiStrings.S_UnityExportDialog_SheetRows_6930EA0067 : "";

        // ── 스킨 ──
        var skins = Seg(u + UiStrings.S_UnityExportDialog_SheetRows_CC3A3FCB2F, [SkinSource.Current, SkinSource.Selected], [UiStrings.S_UnityExportDialog_SheetRows_1543B3F118, UiStrings.S_UnityExportDialog_SheetRows_BB56BA3E03], p => O(p).Skins, (p, v) => O(p).Skins = v)
            .SetTip(0, UiStrings.S_UnityExportDialog_SheetRows_20795363BC)
            .SetTip(1, overrideBlock
                ? UiStrings.S_UnityExportDialog_SheetRows_F2F5553CF3
                : UiStrings.S_UnityExportDialog_SheetRows_7899007C04);
        var pick = Ui.Button(UiStrings.S_UnityExportDialog_SheetRows_464CB34F8D, (_, _) => PickSkins(options, overrideBlock, PartsUse));
        pick.Tip(UiStrings.S_UnityExportDialog_SheetRows_6FEB1E8831);
        var skinRow = Setting(UiStrings.S_UnityExportDialog_SheetRows_4EAC72F29F, UiStrings.S_UnityExportDialog_SheetRows_2ADE7F7827, Line(skins, pick));
        var skinLabel = RowLabel(skinRow);
        _refresh.Add(p =>
        {
            var o = O(p);
            bool anySkin = PartGroups(p).Count > 0 || (!overrideBlock && p.Outfits.Count > 0) || o.Skins != SkinSource.Current;
            var tops = TopLayers(p);
            int excluded = tops.Count(o.ExcludedLayers.Contains);
            skins.IsVisible = anySkin;
            skinLabel.Text = anySkin ? UiStrings.S_UnityExportDialog_SheetRows_4EAC72F29F : UiStrings.S_UnityExportDialog_SheetRows_ED3CFCD2B9;
            pick.Content = (anySkin ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_SheetRows_4125CFA2B0,SkinPickerWindow.SelectedCount(p, o, countOutfits: !overrideBlock)) : UiStrings.S_UnityExportDialog_SheetRows_5CB8E8AD24)
                           + (excluded > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_SheetRows_B57003BD68,excluded) : "");
            skinRow.IsVisible = anySkin || tops.Count > 1 || excluded > 0;
        });
        var splitSkins = Setting(UiStrings.S_UnityExportDialog_SheetRows_F65F934352, UiStrings.S_UnityExportDialog_SheetRows_EF7328D2A3,
            Seg(u + UiStrings.S_UnityExportDialog_SheetRows_960330D064, [false, true], [UiStrings.S_UnityExportDialog_SheetRows_FDB5482E0D, UiStrings.S_UnityExportDialog_SheetRows_2F498E0DBD], p => O(p).SplitSkins, (p, v) => O(p).SplitSkins = v)
                .SetTip(0, UiStrings.S_UnityExportDialog_SheetRows_633C0A1A7E)
                .SetTip(1, UiStrings.S_UnityExportDialog_SheetRows_C243699911));
        _refresh.Add(p => splitSkins.IsVisible = O(p).Skins == SkinSource.Selected);

        // ── 배치 ──
        var layout = Setting(UiStrings.S_UnityExportDialog_SheetRows_9F7B4168D5, null,
            Seg(u + UiStrings.S_UnityExportDialog_SheetRows_7595356F17, [SheetLayout.Grid, SheetLayout.Packed], [UiStrings.S_UnityExportDialog_SheetRows_FAA35616D9, UiStrings.S_UnityExportDialog_SheetRows_51579C6814], p => O(p).Layout, (p, v) => O(p).Layout = v)
                .SetTip(0, UiStrings.S_UnityExportDialog_SheetRows_D5A4FBD978)
                .SetTip(1, UiStrings.S_UnityExportDialog_SheetRows_45F33B13CA));
        var order = OrderRow(O, PartsUse, u);
        var gridMode = Seg(u + UiStrings.S_UnityExportDialog_SheetRows_C060144CBD, [SheetGrid.Auto, SheetGrid.Columns, SheetGrid.Rows], [UiStrings.S_UnityExportDialog_SheetRows_B4F569F8B2, UiStrings.S_UnityExportDialog_SheetRows_265CCB0B30, UiStrings.S_UnityExportDialog_SheetRows_E451F865FA], p => O(p).Grid, (p, v) => O(p).Grid = v)
            .SetTip(0, UiStrings.S_UnityExportDialog_SheetRows_5D0E463631)
            .SetTip(1, UiStrings.S_UnityExportDialog_SheetRows_4D7C715814)
            .SetTip(2, UiStrings.S_UnityExportDialog_SheetRows_7C6D9F2388);
        var gridCount = IntNum(u + UiStrings.S_UnityExportDialog_SheetRows_C060144CBD, 1, SheetOptions.MaxGridCount, 60, p => O(p).GridCount, (p, v) => O(p).GridCount = v)
            .Presets(1, 2, 3, 4, 5, 6, 8, 10, 12, 16).Unit(UiStrings.S_UnityExportDialog_SheetRows_1EF56C7D67).RecentKey("export.grid.count");
        var gridRow = Setting(UiStrings.S_UnityExportDialog_SheetRows_C060144CBD, UiStrings.S_UnityExportDialog_SheetRows_C46F34C2EF, Line(gridMode, gridCount));
        var maxWidthRow = Setting(UiStrings.S_UnityExportDialog_SheetRows_D0C0CFBF0A, UiStrings.S_UnityExportDialog_SheetRows_DF2E272C7F,
            Line(IntNum(u + UiStrings.S_UnityExportDialog_SheetRows_D0C0CFBF0A, 0, PixelImage.MaxSize, 64, p => O(p).MaxWidth, (p, v) => O(p).MaxWidth = v)
                .Presets([0, 128, 256, 512, 1024, 2048, 4096], [], v => v == 0 ? UiStrings.S_UnityExportDialog_SheetRows_E7664692A5 : $"{v:0} px").RecentKey("export.maxwidth"), Ui.Label(UiStrings.S_UnityExportDialog_SheetRows_779AC4B31C)));
        var packOrder = Setting(UiStrings.S_UnityExportDialog_SheetRows_EDAD9AFF8D, null,
            Seg(u + UiStrings.S_UnityExportDialog_SheetRows_EDAD9AFF8D, [AtlasPackOrder.KeepOrder, AtlasPackOrder.LargestFirst], [UiStrings.S_UnityExportDialog_SheetRows_83CAF1B991, UiStrings.S_UnityExportDialog_SheetRows_E287743C3B], p => O(p).PackOrder, (p, v) => O(p).PackOrder = v)
                .SetTip(0, UiStrings.S_UnityExportDialog_SheetRows_47D5F16C63)
                .SetTip(1, UiStrings.S_UnityExportDialog_SheetRows_2B1F42EE49));
        _refresh.Add(p =>
        {
            var o = O(p);
            bool grid = o.Layout == SheetLayout.Grid;
            order.IsVisible = grid || o.PackOrder == AtlasPackOrder.KeepOrder;
            gridRow.IsVisible = grid;
            gridCount.IsVisible = o.Grid != SheetGrid.Auto;
            maxWidthRow.IsVisible = !grid;
            packOrder.IsVisible = !grid;
        });

        // ── 따로 파일(태그·부위 — 스킨은 위 "스킨 담기") ──
        var tagsSplit = Line(Ui.Label(UiStrings.S_UnityExportDialog_SheetRows_6FF563E16C), OnOff(u + UiStrings.S_UnityExportDialog_SheetRows_B9C89F5570, p => O(p).SplitTags, (p, v) => O(p).SplitTags = v))
            .Tip(UiStrings.S_UnityExportDialog_SheetRows_39B41ACDA5);
        tagsSplit.Margin = new Thickness(0, 0, 12, 0);
        var partsSplit = Line(Ui.Label(UiStrings.S_UnityExportDialog_SheetRows_2FD0706089), OnOff(u + UiStrings.S_UnityExportDialog_SheetRows_D02E3A264C, p => O(p).SplitParts, (p, v) => O(p).SplitParts = v))
            .Tip(UiStrings.S_UnityExportDialog_SheetRows_87AFE7E2DF);
        var split = Setting(UiStrings.S_UnityExportDialog_SheetRows_2F498E0DBD, UiStrings.S_UnityExportDialog_SheetRows_62FC29D723,
            Line(tagsSplit, partsSplit));
        _refresh.Add(p => partsSplit.IsVisible = PartsUse(p));

        // ── 칸 이름 ──
        var naming = Setting(UiStrings.S_UnityExportDialog_SheetRows_76CF081665, UiStrings.S_UnityExportDialog_SheetRows_92FFEFEAFD,
            Seg(u + UiStrings.S_UnityExportDialog_SheetRows_67ADFFC0B2, [SpriteNaming.FrameNumber, SpriteNaming.ByTag], [UiStrings.S_UnityExportDialog_SheetRows_B9859A455B, UiStrings.S_UnityExportDialog_SheetRows_28A0B72DEC], p => O(p).Naming, (p, v) => O(p).Naming = v)
                .SetTip(0, UiStrings.S_UnityExportDialog_SheetRows_6080E49509)
                .SetTip(1, UiStrings.S_UnityExportDialog_SheetRows_30EC589AAC));
        var namingWarning = Warning(UiStrings.S_UnityExportDialog_SheetRows_0A17492980 +
                                    UiStrings.S_UnityExportDialog_SheetRows_AD38083975);
        SpriteNaming? namingAtOpen = null;
        _atOpen.Add(p => namingAtOpen = O(p).Naming);
        _refresh.Add(p => namingWarning.IsVisible = namingAtOpen is { } n && O(p).Naming != n);

        var spacing = Setting(UiStrings.S_UnityExportDialog_SheetRows_B3349939E8, UiStrings.S_UnityExportDialog_SheetRows_D967A479A6,
            Line(IntNum(u + UiStrings.S_UnityExportDialog_SheetRows_E0A76444ED, 0, 64, 54, p => O(p).Spacing, (p, v) => O(p).Spacing = v)
                .Presets(0, 1, 2, 3, 4, 8, 16).Unit("px").RecentKey("export.spacing"), Ui.Label("px")));
        var trim = Setting(UiStrings.S_UnityExportDialog_SheetRows_0032A9731C, UiStrings.S_UnityExportDialog_SheetRows_07C4949BB7,
            OnOff(u + UiStrings.S_UnityExportDialog_SheetRows_0032A9731C, p => O(p).Trim, (p, v) => O(p).Trim = v));
        var pot = Setting(UiStrings.S_UnityExportDialog_SheetRows_8BCD5F0C5E, UiStrings.S_UnityExportDialog_SheetRows_B34DD4CFD6,
            OnOff(u + UiStrings.S_UnityExportDialog_SheetRows_8BCD5F0C5E, p => O(p).PowerOfTwo, (p, v) => O(p).PowerOfTwo = v));

        // 프레임 시트만 이 설정을 쓰는데 Single 모드면 배치 설정은 쓰이지 않는다(프레임 하나를 캔버스 크기 그대로).
        var layoutRows = Ui.Stack(layout, order, gridRow, maxWidthRow, packOrder, split, naming, namingWarning, spacing, trim, pot);
        layoutRows.Margin = new Thickness(0);
        var singleNote = Note(UiStrings.S_UnityExportDialog_SheetRows_E25EA4BF34);
        _refresh.Add(p =>
        {
            bool applies = PartsUse(p) || (FramesUse(p) && p.Sprite.Mode == SpriteMode.Multiple);
            SetEnabled(layoutRows, applies);
            singleNote.IsVisible = !applies && FramesUse(p);
        });
        var rows = Ui.Stack(skinRow, splitSkins, singleNote, layoutRows);
        rows.Margin = new Thickness(0);
        return rows;
    }

    /// <summary>스킨 고르기 창을 열고, 확인하면 고른 것을 한 번에 반영(실행취소 1단계).</summary>
    private void PickSkins(Func<SpriteProject, SheetOptions?> options, bool overrideBlock, Func<SpriteProject, bool> partsUse)
    {
        if (_ctx.Project is not { } p || options(p) is not { } o) return;
        bool bake = partsUse(p) && (HasSkins(p) || o.BakeInheritedSkins);
        var picker = new SkinPickerWindow(p, o, partsOnly: overrideBlock, showBake: bake, _ctx.CurrentFrame);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        EditSettings(overrideBlock ? UiStrings.S_UnityExportDialog_PickSkins_4F2869B2C4 : UiStrings.S_UnityExportDialog_PickSkins_3F9745EFAF,
            pr => options(pr) is { } x && picker.Differs(x), pr => picker.ApplyTo(options(pr)!));
    }

    /// <summary>
    /// 정렬 줄: 기준 세로 목록(<see cref="SortOrderList"/> — 끌어 놓기·▲▼·Alt+↑/↓, 태그 끔/켬, 줄 사이 "여기서 새 줄"로 줄바꿈 단위).
    /// 바꿀 때마다 실행취소 1단계. 그 설정을 프레임 시트만 쓰면 부위 기준은 흐리게, 자동 배치면 줄바꿈 칸을 숨긴다.
    /// </summary>
    private Grid OrderRow(Func<SpriteProject, SheetOptions> o, Func<SpriteProject, bool> partsUse, string u)
    {
        var list = new SortOrderList();
        list.Changed += (label, order) => EditSettings(u + label, p => o(p).Order != order, p => o(p).Order = order);
        _refresh.Add(p => list.Show(o(p).Order, partsUse(p), o(p).Layout == SheetLayout.Grid));
        return Setting(UiStrings.S_UnityExportDialog_OrderRow_4741236FF2, UiStrings.S_UnityExportDialog_OrderRow_7C550C3038 +
                               UiStrings.S_UnityExportDialog_OrderRow_BE3BD0187B +
                               UiStrings.S_UnityExportDialog_OrderRow_8372486881, list);
    }

    /// <summary>스킨이 둘 이상인(부위 전용 스킨 포함) 그룹이 있는지.</summary>
    private static bool HasSkins(SpriteProject p) => p.Layers.Any(l => l.IsGroup && l.AllSkins.Count > 1);

    /// <summary>합성에서 켜고 끌 수 있는 최상위 레이어(그룹 = 스킨 칸째, 일반 레이어), 타임라인처럼 위→아래.</summary>
    private static List<Layer> TopLayers(SpriteProject p) => p.Layers.Where(l => !l.IsPart).Reverse().ToList();

    /// <summary>부위 아틀라스에 들어가는(부위가 있는) 그룹, 위→아래.</summary>
    private static List<Layer> PartGroups(SpriteProject p) => p.Layers.Where(l => l.IsGroup && p.ChildCount(l) > 0).Reverse().ToList();

    /// <summary>작은 선택(2~4개) 단추 묶음(<see cref="Choice"/>의 <see cref="TabStripKind.Segmented"/> 꼴). 값이 목록에 없으면 아무것도 고르지 않은 모양.</summary>
    private TabStrip Seg<T>(string undo, T[] values, string[] labels, Func<SpriteProject, T> get, Action<SpriteProject, T> set)
    {
        var s = new TabStrip(TabStripKind.Segmented, labels);
        s.SelectionChanged += (_, _) =>
        {
            if (s.SelectedIndex < 0) return;
            var v = values[s.SelectedIndex];
            EditSettings(undo, p => !EqualityComparer<T>.Default.Equals(get(p), v), p => set(p, v));
        };
        _refresh.Add(p => s.SelectedIndex = Array.IndexOf(values, get(p)));
        return s;
    }

    /// <summary>끔 / 켬.</summary>
    private TabStrip OnOff(string undo, Func<SpriteProject, bool> get, Action<SpriteProject, bool> set)
        => Seg(undo, [false, true], [UiStrings.S_UnityExportDialog_OnOff_10E314E54F, UiStrings.S_UnityExportDialog_OnOff_9809816CE8], get, set);

    /// <summary>값 목록 중 하나를 고르는 콤보.</summary>
    private ComboBox Choice<T>(string undo, T[] values, string[] labels, Func<SpriteProject, T> get, Action<SpriteProject, T> set, int width = 220)
    {
        var c = Ui.Combo(width, labels);
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex < 0) return;
            var v = values[c.SelectedIndex];
            EditSettings(undo, p => !EqualityComparer<T>.Default.Equals(get(p), v), p => set(p, v));
        };
        _refresh.Add(p => c.SelectedIndex = Array.IndexOf(values, get(p)));
        return c;
    }

    /// <summary>켜고 끄는 체크 상자(<paramref name="text"/>가 비면 왼쪽 이름표만 씀).</summary>
    private CheckBox Toggle(string undo, Func<SpriteProject, bool> get, Action<SpriteProject, bool> set, string text = "")
    {
        var c = Ui.Check(text);
        c.IsCheckedChanged += (_, _) => { bool v = c.On(); EditSettings(undo, p => get(p) != v, p => set(p, v)); };
        _refresh.Add(p => c.IsChecked = get(p));
        return c;
    }

    private NumericUpDown IntNum(string undo, int min, int max, int width, Func<SpriteProject, int> get, Action<SpriteProject, int> set)
    {
        var n = Ui.Num(min, max, min, width);
        n.ValueChanged += (_, _) => { int v = (int)n.NumValue(); EditSettings(undo, p => get(p) != v, p => set(p, v)); };
        _refresh.Add(p => n.Value = Math.Clamp(get(p), min, max));
        return n;
    }

    /// <summary>
    /// Unity처럼 슬라이더 + 숫자 칸. 끄는 동안에는 숫자만 따라가고 놓을 때 한 번 반영한다(실행취소 1단계).
    /// </summary>
    private Control IntSlider(string undo, int min, int max, decimal[] presets, string unit, string recentKey, Func<SpriteProject, int> get, Action<SpriteProject, int> set)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = Math.Max(1, (max - min) / 8),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0),
        };
        var num = Ui.Num(min, max, min, 40).Presets(presets).Unit(unit).RecentKey(recentKey);
        bool syncing = false, pressed = false;
        void Commit(int v) => EditSettings(undo, p => get(p) != v, p => set(p, v));
        num.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            int v = (int)num.NumValue();
            syncing = true;
            slider.Value = v;
            syncing = false;
            Commit(v);
        };
        slider.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            int v = (int)Math.Round(slider.Value);
            syncing = true;
            num.Value = v;
            syncing = false;
            if (!pressed) Commit(v);
        };
        void Release()
        {
            if (!pressed) return;
            pressed = false;
            Commit((int)Math.Round(slider.Value));
        }
        slider.AddHandler(PointerPressedEvent, (_, _) => pressed = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(PointerReleasedEvent, (_, _) => Release(), RoutingStrategies.Bubble, handledEventsToo: true);
        slider.AddHandler(PointerCaptureLostEvent, (_, _) => Release(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        _refresh.Add(p =>
        {
            int v = Math.Clamp(get(p), min, max);
            syncing = true;
            slider.Value = v;
            num.Value = v;
            syncing = false;
        });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(num, 1);
        g.Children.Add(slider);
        g.Children.Add(num);
        return g;
    }

    /// <summary>이름·폴더 칸: 포커스를 잃을 때(또는 창을 닫을 때) 반영, 비우면 기본값(null).</summary>
    private TextBox NameText(string undo, string watermark, int width, Func<SpriteProject, string?> get, Action<SpriteProject, string?> set)
    {
        var t = Ui.Text("", width);
        t.Watermark = watermark;
        void Commit() { var v = NullIfEmpty(t.Text); EditSettings(undo, p => get(p) != v, p => set(p, v)); }
        t.LostFocus += (_, _) => Commit();
        t.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };
        _commitTexts.Add(Commit);
        _refresh.Add(p => { if (!t.IsFocused) t.Text = get(p) ?? ""; });
        return t;
    }

    /// <summary>
    /// 왼쪽 이름표(고정 너비 <paramref name="labelWidth"/>, 길면 줄바꿈) + 오른쪽 편집 칸 한 줄(표처럼). <paramref name="tip"/>은 이름표·편집 칸 도움말.
    /// 기본 너비는 Import Settings(Unity 항목 이름) — 시트 설정·내보내기 탭은 <see cref="Setting"/>.
    /// </summary>
    private static Grid Field(string label, string? tip, Control editor, int labelWidth = LabelWidth)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"), Margin = new Thickness(0, 1) };
        var l = Ui.Label(label);
        l.TextWrapping = TextWrapping.Wrap;
        editor.VerticalAlignment = VerticalAlignment.Center;
        if (editor is not Grid) editor.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(editor, 1);
        g.Children.Add(l);
        g.Children.Add(editor);
        if (tip is not null)
        {
            l.Tip(tip);
            editor.Tip(tip);
        }
        return g;
    }

    /// <summary>시트 설정·내보내기 탭의 한 줄(이름표 너비 <see cref="SettingLabelWidth"/>).</summary>
    private static Grid Setting(string label, string? tip, Control editor) => Field(label, tip, editor, SettingLabelWidth);

    /// <summary><see cref="Field"/> 줄의 이름표.</summary>
    private static TextBlock RowLabel(Grid row) => (TextBlock)row.Children[0];

    /// <summary>바꿀 수 없는 값(Unity에서는 드롭다운).</summary>
    private static ComboBox ReadOnlyCombo(string value)
    {
        var c = Ui.Combo(220, value);
        c.IsEnabled = false;
        return c;
    }

    private static Border Rule() => new() { Height = 1, Background = AppTheme.BorderBrush, Margin = new Thickness(0, 6) };

    /// <summary>쓰지 않는 줄은 흐리게(이름표까지).</summary>
    private static void SetEnabled(Control row, bool on)
    {
        row.IsEnabled = on;
        row.Opacity = on ? 1 : 0.5;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static TextBlock PathLabel()
    {
        var t = Ui.Label("");
        t.Foreground = AppTheme.DimTextBrush;
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    /// <summary>편집 칸 여러 개를 한 줄에(좁으면 다음 줄로 넘김).</summary>
    private static WrapPanel Line(params Control[] controls)
    {
        var w = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in controls)
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            w.Children.Add(c);
        }
        return w;
    }

    /// <summary>줄바꿈되는 글(왼쪽, 남은 너비) + 오른쪽에 붙는 단추·ⓘ.</summary>
    private static Grid Beside(Control main, params Control[] right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(right.Select(_ => ",Auto"))) };
        g.Children.Add(main);
        for (int i = 0; i < right.Length; i++)
        {
            right[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right[i], i + 1);
            g.Children.Add(right[i]);
        }
        return g;
    }

    /// <summary>흐린 안내 글(줄바꿈).</summary>
    private static TextBlock Note(string text)
    {
        var t = Ui.Label(text);
        t.Foreground = AppTheme.DimTextBrush;
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    /// <summary>경고 글(줄바꿈).</summary>
    private static TextBlock Warning(string text)
    {
        var t = Note(text);
        t.Foreground = AppTheme.WarningBrush;
        return t;
    }

    /// <summary>묶음 제목(바꿀 수 있는 글).</summary>
    private static TextBlock SectionTitle(string text = "")
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 2) };
        return t;
    }

    /// <summary>
    /// 접을 수 있는 설정 묶음(제목 줄 + 내용). 접은 묶음은 <see cref="AppSettings.ExportCollapsed"/>에 <paramref name="id"/>로 기억한다.
    /// </summary>
    private Expander Section(string id, TextBlock title, Control content)
    {
        content.Margin = new Thickness(0, 2, 0, 0);
        var e = new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = !_settings.ExportCollapsed.Contains(id),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(2, 3, 2, 3),
        };
        e.PropertyChanged += (_, change) =>
        {
            if (change.Property != Expander.IsExpandedProperty) return;
            _settings.ExportCollapsed.Remove(id);
            if (!e.IsExpanded) _settings.ExportCollapsed.Add(id);
        };
        return e;
    }

    // ───────────── 프로젝트 → 화면 ─────────────

    private void RefreshAll()
    {
        _updating = true;
        try
        {
            // 프로젝트가 없으면 페이지만 끈다(탭 줄 오른쪽 프리셋 단추의 "관리"는 프로젝트 없이도 쓴다).
            _columns.IsEnabled = _importPage.IsEnabled = _ctx.HasProject;
            if (_ctx.Project is not { } p) return;
            foreach (var refresh in _refresh) refresh(p);
            _framesSection.Panel.IsVisible = FramesShown(p);
            _partsSection.Panel.IsVisible = PartsShown(p);
        }
        finally
        {
            _updating = false;
        }
    }

    // ───────────── 시트 미리보기 ─────────────

    /// <summary>오른쪽 미리보기의 한 부분(① 프레임 시트 / ② 부위 아틀라스): 제목, 안내 줄, 결과 파일마다 이름 + 시트 + 칸 목록.</summary>
    private sealed class PreviewSection(string number, TextBlock title, StackPanel files, Control panel)
    {
        /// <summary>제목 앞 번호("① ").</summary>
        public string Number { get; } = number;
        public TextBlock Title { get; } = title;
        public StackPanel Files { get; } = files;
        public Control Panel { get; } = panel;
        /// <summary>지금 보이는 파일별 시트(강조 표시용).</summary>
        public List<SheetPreview> Sheets { get; } = new();

        public void Clear()
        {
            foreach (var s in Sheets) s.SetSheet(null);
            Sheets.Clear();
            Files.Children.Clear();
        }
    }

    /// <summary>제목 + <paramref name="extra"/>(안내 줄, 없으면 null) + 결과 파일들이 들어갈 자리.</summary>
    private static PreviewSection BuildSection(string number, Control? extra)
    {
        var title = Ui.Label("", bold: true);
        title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(3, 8, 3, 2);
        var files = new StackPanel { Spacing = 2 };
        var panel = Ui.Stack(title);
        if (extra is not null) panel.Children.Add(extra);
        panel.Children.Add(files);
        return new PreviewSection(number, title, files, panel);
    }

    /// <summary>
    /// 파일마다 이름(프레임 시트 스킨 항목이면 그 항목도) + 칸 너비에 맞춘 시트 미리보기 + 칸 목록(고르면 <paramref name="select"/>)을 쌓는다.
    /// </summary>
    private static void FillSection(PreviewSection section, IReadOnlyList<SheetFile> files, string ext, bool showNames, string numberHeader, Action<SheetEntry> select)
    {
        section.Clear();
        bool many = files.Count > 1;
        foreach (var file in files)
        {
            var name = Ui.Label(file.Name + ext + (file.Variant is { IsCurrent: false } v ? $" — {v.Label}" : ""), bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            name.Margin = new Thickness(3, 8, 3, 0);
            section.Files.Children.Add(name);
            var sheet = new SheetPreview { Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch };
            var header = EntryRow(UiStrings.S_UnityExportDialog_FillSection_A33ACD4239, numberHeader, UiStrings.S_UnityExportDialog_FillSection_9F6AD2B46B);
            foreach (var t in header.Children.OfType<TextBlock>()) t.Foreground = AppTheme.DimTextBrush;
            header.Margin = new Thickness(6, 3, 6, 0);
            var entries = new ListBox
            {
                Height = many ? 140 : 200,
                Background = AppTheme.InputBrush,
                Foreground = AppTheme.TextBrush,
                BorderThickness = new Thickness(0),
                SelectionMode = SelectionMode.Single,
                Margin = new Thickness(3, 0, 3, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            entries.SelectionChanged += (_, _) =>
            {
                if (entries.SelectedItem is ListBoxItem { Tag: SheetEntry e })
                {
                    sheet.Highlight = e.Rect;
                    select(e);
                }
            };
            FillPreview(sheet, entries, file.Sheet, showNames);
            section.Files.Children.Add(sheet);
            section.Files.Children.Add(header);
            section.Files.Children.Add(entries);
            section.Sheets.Add(sheet);
        }
    }

    /// <summary>제목에 쓸 결과 파일 이름(여러 개면 개수 + 앞의 몇 개).</summary>
    private static string FileNames(IReadOnlyList<SheetFile> files, string ext)
        => files.Count == 1
            ? files[0].Name + ext
            : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_FileNames_83F9DDF23F,files.Count) + string.Join(", ", files.Take(4).Select(f => f.Name + ext)) + (files.Count > 4 ? ", …" : "");

    /// <summary>부위 아틀라스 칸 → 그 부위 레이어·프레임을 현재로(다른 스킨 칸이면 그 그룹의 스킨도 그 스킨으로).</summary>
    private void SelectPartEntry(SheetEntry e)
    {
        if (_ctx.Project is not { } p) return;
        int layer = PartLayerOf(p, e);
        if (layer >= 0 && p.Layers[layer] is { Parent: { IsGroup: true } group } part)
        {
            // 부위 전용 스킨 칸이거나 부위를 따로 고른 중이면 그 부위만, 아니면 그룹 착장을 그 칸의 스킨으로
            var skin = e.Skin ?? group.BaseSkin!;
            if (skin.IsPartOnly || part.SkinOverride is not null) _ctx.SetPartSkin(part, skin);
            else _ctx.SetActiveSkin(group, skin);
        }
        if (layer >= 0) _ctx.SetCurrent(layer, e.FrameIndex);
        else _ctx.SetCurrentFrame(e.FrameIndex);
    }

    /// <summary>부위 아틀라스 칸의 부위 레이어 번호(없으면 -1).</summary>
    private static int PartLayerOf(SpriteProject p, SheetEntry e) => e.Part is { } part ? p.IndexOf(part) : -1;

    /// <summary>칸이 그 부위의 지금 보이는 스킨 것인지(첫 스킨 칸은 <see cref="SheetEntry.Skin"/>이 null).</summary>
    private static bool IsActiveSkinEntry(SheetEntry e)
        => e.Part is not { Parent: { IsGroup: true } g } part || ReferenceEquals(e.Skin ?? g.BaseSkin, part.ShownSkin);

    /// <summary>칸 목록의 한 줄(이름 · 번호 60 · 위치 130).</summary>
    private static Grid EntryRow(string name, string number, string rect)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,60,130") };
        string[] texts = [name, number, rect];
        for (int i = 0; i < 3; i++)
        {
            var t = new TextBlock { Text = texts[i], TextTrimming = TextTrimming.CharacterEllipsis, Foreground = AppTheme.TextBrush, FontSize = AppTheme.SmallFontSize };
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    /// <summary>내보내기 탭(미리보기)을 보고 있으면 잠시 뒤 다시 만들고, 아니면 그 탭으로 돌아올 때 만든다.</summary>
    private void ScheduleRebuild()
    {
        _previewDirty = true;
        if (_tabs.SelectedIndex != MainPage) return;
        _rebuild.Stop();
        _rebuild.Start();
    }

    private void RebuildPreview()
    {
        _rebuild.Stop();
        _previewDirty = false;
        if (_ctx.Project is not { } p)
        {
            _framesSection.Clear();
            _partsSection.Clear();
            return;
        }
        var baseName = p.Path is null ? p.BaseName : Path.GetFileNameWithoutExtension(ProjectExport.GetExportImagePath(p));
        // 제목: 결과 파일 이름 + 저장할 때 실제로 내보내는지.
        string ext = Path.GetExtension(p.Path is null ? ".png" : ProjectExport.GetExportImagePath(p));
        string Off(bool on) => !p.Export.ExportOnSave ? UiStrings.S_UnityExportDialog_RebuildPreview_B223C99C7E : on ? "" : UiStrings.S_UnityExportDialog_RebuildPreview_27C575FC02;
        // 최종 결과만 보인다: 고른 결과(프레임 시트 / 부위 아틀라스 / 둘 다)의 미리보기. 둘 다 꺼져 있으면 프레임 시트(꺼짐 표시).
        bool hasParts = p.Layers.Any(l => l.IsPart);
        bool showParts = hasParts && p.Export.ExportParts;
        bool showFrames = p.Export.ExportFrames || !showParts;
        _framesSection.Panel.IsVisible = showFrames;
        _partsSection.Panel.IsVisible = showParts;
        if (showFrames)
        {
            var frameFiles = ProjectExport.BuildFrameSheets(p, baseName);
            string splitNote = !p.Export.Frames.SplitTags || frameFiles.Count > 1 ? ""
                : p.Sprite.Mode == SpriteMode.Single ? UiStrings.S_UnityExportDialog_RebuildPreview_B622FBC7F1 : p.Tags.Count == 0 ? UiStrings.S_UnityExportDialog_RebuildPreview_DEE196AED0 : "";
            _framesSection.Title.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_RebuildPreview_9735FBD681,_framesSection.Number,FileNames(frameFiles, ext),splitNote,Off(p.Export.ExportFrames));
            FillSection(_framesSection, frameFiles, ext, showNames: false, UiStrings.S_UnityExportDialog_RebuildPreview_49980BDB2F, e => _ctx.SetCurrentFrame(e.FrameIndex));
        }
        else _framesSection.Clear();
        if (showParts)
        {
            var partsName = p.Path is null ? p.Export.PartsFileName ?? baseName + "_parts" : Path.GetFileNameWithoutExtension(ProjectExport.GetPartsImagePath(p));
            var partFiles = ProjectExport.BuildPartsSheets(p, partsName);
            _partsSection.Title.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_RebuildPreview_FFABCA970A,_partsSection.Number,FileNames(partFiles, ext),Off(p.Export.ExportParts));
            FillSection(_partsSection, partFiles, ext, showNames: true, UiStrings.S_UnityExportDialog_RebuildPreview_84CC456F81, SelectPartEntry);
        }
        else _partsSection.Clear();
        HighlightCurrent();
    }

    private static void FillPreview(SheetPreview preview, ListBox list, SheetResult sheet, bool showNames)
    {
        preview.SetSheet(sheet, showNames);
        list.Items.Clear();
        foreach (var e in sheet.Entries)
            list.Items.Add(new ListBoxItem
            {
                Content = EntryRow(e.Name, (e.FrameIndex + 1).ToString(CultureInfo.InvariantCulture), $"{e.Rect.X},{e.Rect.Y} {e.Rect.Width}×{e.Rect.Height}"),
                Tag = e,
                Padding = new Thickness(3, 1),
                MinHeight = 0,
            });
    }

    /// <summary>미리보기에서 현재 프레임 칸(부위 아틀라스는 현재 부위·프레임 칸)을 강조한다(파일마다).</summary>
    private void HighlightCurrent()
    {
        int frame = _ctx.CurrentFrame;
        foreach (var preview in _framesSection.Sheets)
        {
            // 스킨 항목이 한 이미지에 여럿이면 지금 보이는 착장 항목 칸
            var entries = preview.Sheet?.Entries.Where(e => e.FrameIndex == frame).ToList() ?? [];
            preview.Highlight = (entries.FirstOrDefault(IsShownVariant) ?? entries.FirstOrDefault())?.Rect;
        }
        if (_ctx.Project is not { } p) return;
        int layer = _ctx.CurrentLayer;
        foreach (var preview in _partsSection.Sheets)
        {
            // 지금 스킨 칸, 물려받는 셀이면 그 셀이 가리키는 첫 스킨 칸
            var entries = _ctx.CurrentPart is null ? [] : preview.Sheet?.Entries.Where(e => e.FrameIndex == frame && PartLayerOf(p, e) == layer).ToList() ?? [];
            preview.Highlight = (entries.FirstOrDefault(IsActiveSkinEntry) ?? entries.FirstOrDefault(e => e.Skin is null))?.Rect;
        }
    }

    /// <summary>프레임 시트 칸이 지금 보이는 착장의 것인지(지금 착장 항목, 또는 그 그룹이 지금 그 스킨인 항목).</summary>
    private static bool IsShownVariant(SheetEntry e)
        => e.Variant is not { } v || v.IsCurrent || (v.Group is { } g && ReferenceEquals(g.ActiveSkin, v.Skin));

    /// <summary>만들어질 시트와 칸 테두리(부위 아틀라스면 칸 이름도)를 보여 준다. 높이는 받은 너비에 시트 비율을 맞춘다(최소 120, 최대 720).</summary>
    private sealed class SheetPreview : Control
    {
        private static readonly IImmutableSolidColorBrush LabelBack = new ImmutableSolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        private static readonly ImmutablePen CellPen = new(CellPenColor, 1);
        private static readonly ImmutablePen HighlightPen = AppTheme.Pen(AppTheme.AccentColor, 2);
        private WriteableBitmap? _bmp;
        private SheetResult? _sheet;
        private Rectangle? _highlight;
        private bool _showNames;

        public SheetResult? Sheet => _sheet;

        public Rectangle? Highlight { get => _highlight; set { _highlight = value; InvalidateVisual(); } }

        public void SetSheet(SheetResult? sheet, bool showNames = false)
        {
            _bmp?.Dispose();
            _sheet = sheet;
            _showNames = showNames;
            _bmp = sheet is null ? null : ImageUtil.ToBitmap(sheet.Image);
            _highlight = null;
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
        {
            double w = double.IsFinite(availableSize.Width) ? availableSize.Width : 400;
            if (_bmp is null) return new Avalonia.Size(w, 120);
            int bw = Math.Max(1, _bmp.PixelSize.Width), bh = Math.Max(1, _bmp.PixelSize.Height);
            return new Avalonia.Size(w, Math.Clamp(Math.Max(0, w - 8) * bh / bw + 28, 120, 720));
        }

        public override void Render(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.InputBrush, bounds);
            if (_bmp is null || _sheet is null) return;
            int bw = _bmp.PixelSize.Width, bh = _bmp.PixelSize.Height;
            var area = new Rect(4, 14, Math.Max(0, bounds.Width - 8), Math.Max(0, bounds.Height - 28));
            var dest = ImageUtil.DrawFitted(g, _bmp, new Rectangle(0, 0, bw, bh), area, checker: true);
            double scale = dest.Width / bw;
            double small = AppTheme.SmallFontSize;
            foreach (var entry in _sheet.Entries)
            {
                var r = entry.Rect;
                bool hl = entry.Rect == _highlight;
                var sr = new Rect(dest.X + r.X * scale, dest.Y + r.Y * scale, r.Width * scale, r.Height * scale);
                g.DrawRectangle(null, hl ? HighlightPen : CellPen, new Rect(Math.Floor(sr.X) + 0.5, Math.Floor(sr.Y) + 0.5, Math.Max(0, Math.Floor(sr.Width)), Math.Max(0, Math.Floor(sr.Height))));
                if (!_showNames) continue;
                var size = AppTheme.MeasureText(entry.Name, small);
                var lr = new Rect(Math.Floor(sr.X) + 1, Math.Floor(sr.Y) + 1, Math.Min(Math.Ceiling(size.Width), Math.Max(12, Math.Floor(sr.Width) - 2)), Math.Ceiling(size.Height));
                g.FillRectangle(LabelBack, lr);
                AppTheme.DrawTextIn(g, entry.Name, lr, hl ? AppTheme.AccentBrush : Brushes.White, small);
            }
            AppTheme.DrawTextIn(g, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityExportDialog_Render_16815EEA0C,bw,bh,_sheet.Entries.Count), new Rect(2, bounds.Height - 14, Math.Max(1, bounds.Width - 4), 14), AppTheme.DimTextBrush, small);
        }
    }
}
