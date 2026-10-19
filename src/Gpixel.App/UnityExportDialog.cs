using System.Globalization;
using Avalonia.Media.Immutable;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 내보내기 설정 창(Ctrl+E, 파일 > 내보내기…; 파란 "내보내기"는 OK로 닫혀 호출한 쪽이 내보냄). 바꾸면 바로 프로젝트에 적용된다(설정 하나마다 실행취소 1단계).
/// 탭은 [내보내기 | Import Settings](Import Settings = Unity Texture Importer 인스펙터와 같은 항목·이름, .meta를 쓸 때만 보임).
/// 내보내기 탭 = 왼쪽 설정 칸(세로 스크롤) | 끌어서 너비를 바꾸는 나눔줄 | 오른쪽 미리보기(세로 스크롤). 설정 칸은 한 곳에만 둔다 — 위에서 아래로 접을 수 있는 묶음:
/// 설정 프리셋(<see cref="ExportPresetBar"/>) → 공통(내보낼 결과 + 두 결과가 같이 쓰는 시트 설정 <see cref="ExportSettings.Common"/>) → 파일(저장 시 내보내기·폴더·형식·.meta)
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
    private readonly TabControl _tabs;
    private readonly TabItem _mainTab, _importTab;
    private readonly Grid _columns;
    private readonly Expander _advanced;
    private readonly PreviewSection _framesSection, _partsSection;
    private readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly EventHandler _onReplaced, _onCurrent;
    private readonly EventHandler<ProjectChangedEventArgs> _onChanged;
    private bool _updating, _previewDirty = true, _customPivot;

    /// <param name="ctx">편집기 상태(현재 프로젝트).</param>
    /// <param name="settings">창 크기·위치·나눔 비율·접은 묶음을 기억할 앱 설정.</param>
    /// <param name="ppuChanged">사용자가 PPU를 바꿨을 때(마지막 PPU 기억용).</param>
    public UnityExportDialog(EditorContext ctx, AppSettings settings, Action<float>? ppuChanged = null) : base("내보내기 설정")
    {
        _ctx = ctx;
        _settings = settings;
        _ppuChanged = ppuChanged;
        CloseOnly();
        // 닫기 옆 파란 "내보내기": 창을 닫고(입력 중인 값도 반영) 호출한 쪽이 내보낸다.
        var export = AddButton("내보내기", DialogResult.OK);
        export.Background = AppTheme.AccentBrush;
        export.Foreground = Brushes.White;
        export.FontWeight = FontWeight.Bold;
        export.HorizontalContentAlignment = HorizontalAlignment.Center;
        export.Tip("지금 설정대로 이미지(+ .meta)를 내보내기");
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = MinW;
        MinHeight = MinH;
        RestorePlacement();

        // ── 오른쪽: 미리보기 ──
        _framesSection = BuildSection("① ", null);
        // 부위 아틀라스가 따로 설정을 쓰면 그 설정이 있는 곳 안내(설정 칸은 왼쪽 "고급"에만 둔다).
        var overrideLine = Beside(Note("부위 아틀라스는 따로 설정을 씁니다(고급 > 시트별 다르게 — 공통 시트 설정은 프레임 시트만)."),
            Ui.Button("설정 보기", (_, _) => ShowAdvanced()).Tip("왼쪽 고급 묶음의 부위 아틀라스 설정으로"));
        _refresh.Add(p => overrideLine.IsVisible = p.Export.PartsOverride is not null);
        _partsSection = BuildSection("② ", overrideLine);
        var previewHelp = Beside(
            Note("저장(Ctrl+S)·내보내기를 하면 아래 결과 파일이 만들어집니다. 칸 목록의 이름이 Unity .meta의 스프라이트 이름입니다."),
            Info("프레임 시트: 그룹·부위를 합성한 프레임마다 한 칸(애니메이션용). 부위 아틀라스: 부위 × 프레임마다 한 칸(부위별 스프라이트, 부위가 있을 때만). " +
                 "칸을 고르면 편집기에서 그 프레임(부위 아틀라스는 그 부위·스킨)으로 갑니다."));
        var preview = Ui.ScrollHost(Ui.Stack(previewHelp, _framesSection.Panel, _partsSection.Panel));

        // ── 왼쪽: 설정(위에서 아래로) ──
        var commonTitle = SectionTitle();
        var (files, frames, parts, advanced) = BuildExportSections();
        var framesBox = Section("frames", SectionTitle("프레임 시트 (애니메이션)"), frames);
        var partsBox = Section("parts", SectionTitle("부위 아틀라스 (부위 × 프레임마다 한 칸, 항상 Multiple)"), parts);
        _advanced = Section("advanced", SectionTitle("고급 — 부위 아틀라스만 다른 시트 설정"), advanced);
        _refresh.Add(p =>
        {
            // 고른 결과의 묶음만 보인다(둘 다 꺼진 예전 설정이면 프레임 시트 묶음).
            framesBox.IsVisible = FramesShown(p);
            partsBox.IsVisible = _advanced.IsVisible = PartsShown(p);
        });
        var settingsColumn = Ui.ScrollHost(Ui.Stack(
            Section("presets", SectionTitle("설정 프리셋"), new ExportPresetBar(ctx, this)),
            Section("common", commonTitle, BuildCommonBlock(commonTitle)),
            Section("files", SectionTitle("파일 (두 결과 공통)"), files),
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
        }.Tip("끌어서 설정·미리보기 너비 바꾸기");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(preview, 2);
        _columns.Children.Add(settingsColumn);
        _columns.Children.Add(splitter);
        _columns.Children.Add(preview);

        _mainTab = Tab("내보내기", _columns);
        _importTab = Tab("Import Settings", Ui.ScrollHost(BuildImportTab()));
        _tabs = new TabControl { Padding = new Thickness(0, 4, 0, 0) };
        _tabs.Items.Add(_mainTab);
        _tabs.Items.Add(_importTab);
        _tabs.SelectedItem = _mainTab;
        Opened += (_, _) => { if (_previewDirty) RebuildPreview(); };
        // Unity Import Settings는 .meta를 쓸 때만 의미가 있다.
        _refresh.Add(p =>
        {
            bool meta = p.Export.WriteMeta != false;
            _importTab.IsVisible = meta;
            if (!meta && _tabs.SelectedItem == _importTab) _tabs.SelectedItem = _mainTab;
        });
        _tabs.SelectionChanged += (_, e) =>
        {
            // 안쪽 목록·콤보의 SelectionChanged도 여기로 올라오므로 탭 자신의 것만 본다.
            if (e.Source == _tabs && _tabs.SelectedItem == _mainTab && _previewDirty) RebuildPreview();
        };

        // 대화상자 본문(StackPanel)은 높이를 주지 않으므로 탭을 바깥 DockPanel의 채우기 칸에 바로 둔다.
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
        _tabs.SelectedItem = _mainTab;
        _advanced.IsExpanded = true;
        Dispatcher.UIThread.Post(() => _advanced.BringIntoView(), DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // Enter(닫기 단추)로 닫아도 입력 중이던 이름·폴더가 반영되게.
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
        var mode = Choice("스프라이트 모드", [SpriteMode.Single, SpriteMode.Multiple], ["Single", "Multiple"], p => p.Sprite.Mode, (p, v) => p.Sprite.Mode = v);
        var ppu = Ui.Num(1, 10000, 16, 80);
        ppu.ValueChanged += (_, _) =>
        {
            float v = (float)ppu.NumValue();
            if (EditSettings("PPU", p => Math.Abs(p.Sprite.PixelsPerUnit - v) > 0.0001f, p => p.Sprite.PixelsPerUnit = v)) _ppuChanged?.Invoke(v);
        };
        _refresh.Add(p => ppu.Value = (decimal)Math.Clamp(p.Sprite.PixelsPerUnit, 1, 10000));
        var mesh = Choice("메시 형식", [true, false], ["Full Rect", "Tight"], p => p.Sprite.FullRectMesh, (p, v) => p.Sprite.FullRectMesh = v);
        var extrudeRow = Field("Extrude Edges", "Tight 메시를 외곽선에서 바깥으로 넓힐 픽셀 수(0~32). Mesh Type이 Tight일 때만 쓰입니다.",
            IntSlider("Extrude Edges", 0, 32, p => p.Sprite.ExtrudeEdges, (p, v) => p.Sprite.ExtrudeEdges = v));
        _refresh.Add(p => SetEnabled(extrudeRow, !p.Sprite.FullRectMesh));

        // 피벗(Single 모드만): 프리셋 또는 Custom(X/Y). 피벗 도구(K)와 같은 프로젝트 기본 피벗.
        var pivot = Ui.Combo(220, PivotNames);
        var pivotX = Ui.Num(0, 1, 0.5m, 60, 3, 0.05m);
        var pivotY = Ui.Num(0, 1, 0.5m, 60, 3, 0.05m);
        var pivotRow = Field("Pivot", "Single 모드 스프라이트의 피벗 = 프로젝트 기본 피벗(피벗 도구(K)와 같은 값, 프레임별 피벗이 있으면 그 프레임 값이 쓰임).", pivot);
        var customRow = Field("Custom Pivot", "피벗 위치(0~1, 왼쪽 아래가 0,0 · 오른쪽 위가 1,1).", Ui.Row(Ui.Label("X"), pivotX, Ui.Label("Y"), pivotY));
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
            if (!EditSettings("기본 피벗", p => p.Sprite.DefaultPivot != v, p => p.Sprite.DefaultPivot = v)) RefreshAll();
        };
        void ApplyCustomPivot()
        {
            var v = new Pivot((float)pivotX.NumValue(), (float)pivotY.NumValue());
            EditSettings("기본 피벗", p => p.Sprite.DefaultPivot != v, p => p.Sprite.DefaultPivot = v);
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

        var alphaTransparencyRow = Field("Alpha Is Transparency", "알파를 투명도로 보고 투명한 가장자리의 색 번짐을 줄입니다. Alpha Source가 None이면 쓰이지 않습니다.",
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
                Field("sRGB (Color Texture)", "색 텍스처(감마 공간)로 읽습니다. 보통 켭니다.",
                    Toggle("sRGB", p => p.Sprite.SRgb, (p, v) => p.Sprite.SRgb = v)),
                Field("Alpha Source", "알파 채널을 어디서 가져올지: 없음 / 입력 텍스처의 알파 / 회색조에서 만들기.",
                    Choice("Alpha Source", [SpriteAlphaSource.None, SpriteAlphaSource.InputTextureAlpha, SpriteAlphaSource.FromGrayScale],
                        ["None", "Input Texture Alpha", "From Gray Scale"], p => p.Sprite.AlphaSource, (p, v) => p.Sprite.AlphaSource = v)),
                alphaTransparencyRow,
                Field("Read/Write", "스크립트에서 픽셀을 읽고 쓸 수 있게 합니다(텍스처 메모리 2배).",
                    Toggle("Read/Write", p => p.Sprite.Readable, (p, v) => p.Sprite.Readable = v)),
                Field("Generate Mip Maps", "밉맵을 만듭니다. 픽셀 아트는 보통 끕니다.",
                    Toggle("밉맵", p => p.Sprite.GenerateMipMaps, (p, v) => p.Sprite.GenerateMipMaps = v))),
        };

        // 플랫폼(Default) 설정
        int[] maxSizes = [0, .. SpriteSettings.MaxTextureSizes];
        string[] maxSizeNames = ["Auto (fit)", .. SpriteSettings.MaxTextureSizes.Select(s => s.ToString(CultureInfo.InvariantCulture))];
        var qualityRow = Field("Compressor Quality", "Crunch 압축 품질(0~100). 높을수록 좋고 느립니다. Use Crunch Compression을 켰을 때만 쓰입니다.",
            IntSlider("Compressor Quality", 0, 100, p => p.Sprite.CompressionQuality, (p, v) => p.Sprite.CompressionQuality = v));
        _refresh.Add(p => SetEnabled(qualityRow, p.Sprite.CrunchCompression));
        var platform = Ui.Group("Default", Ui.Stack(
            Field("Max Size", "텍스처 최대 크기. Auto (fit) = 이미지가 들어가는 가장 작은 2의 거듭제곱. 시트가 더 크면 Unity가 줄입니다.",
                Choice("Max Size", maxSizes, maxSizeNames, p => p.Sprite.MaxTextureSize, (p, v) => p.Sprite.MaxTextureSize = v)),
            Field("Resize Algorithm", "Max Size보다 클 때 줄이는 방법.",
                Choice("Resize Algorithm", [TextureResize.Mitchell, TextureResize.Bilinear], ["Mitchell", "Bilinear"],
                    p => p.Sprite.ResizeAlgorithm, (p, v) => p.Sprite.ResizeAlgorithm = v)),
            Field("Format", "텍스처 형식. Automatic = 플랫폼에 맞게, RGBA 32 bit = 무손실(알파 있음), RGB 24 bit = 무손실(알파 없음).",
                Choice("Format", [TextureFormatChoice.Automatic, TextureFormatChoice.RGBA32, TextureFormatChoice.RGB24], ["Automatic", "RGBA 32 bit", "RGB 24 bit"],
                    p => p.Sprite.Format, (p, v) => p.Sprite.Format = v)),
            Field("Compression", "압축 품질. 픽셀 아트는 보통 None(압축 없음).",
                Choice("Compression", [TextureCompressionChoice.None, TextureCompressionChoice.LowQuality, TextureCompressionChoice.NormalQuality, TextureCompressionChoice.HighQuality],
                    ["None", "Low Quality", "Normal Quality", "High Quality"], p => p.Sprite.Compression, (p, v) => p.Sprite.Compression = v)),
            Field("Use Crunch Compression", "Crunch로 더 작게 압축합니다(손실). Compression이 None이 아닐 때 쓰입니다.",
                Toggle("Crunch 압축", p => p.Sprite.CrunchCompression, (p, v) => p.Sprite.CrunchCompression = v)),
            qualityRow));

        var note = Ui.Label("프레임 시트·부위 아틀라스의 .meta에 쓰는 Unity Texture Importer 설정입니다(부위 아틀라스는 Sprite Mode가 항상 Multiple).");
        note.Foreground = AppTheme.DimTextBrush;
        note.TextWrapping = TextWrapping.Wrap;

        return Ui.Stack(
            note,
            Field("Texture Type", "Gpixel은 항상 스프라이트 텍스처로 내보냅니다.", ReadOnlyCombo("Sprite (2D and UI)")),
            Field("Texture Shape", "2D 텍스처(고정).", ReadOnlyCombo("2D")),
            Rule(),
            Field("Sprite Mode", "Single = 이미지 전체가 스프라이트 1개, Multiple = 칸마다 스프라이트. 프레임 시트에만 적용(부위 아틀라스는 항상 Multiple).", mode),
            Field("Pixels Per Unit", "월드 1유닛에 들어가는 픽셀 수.", ppu),
            Field("Mesh Type", "Full Rect = 사각형 메시, Tight = 투명한 곳을 뺀 외곽선 메시.", mesh),
            extrudeRow,
            pivotRow,
            customRow,
            Field("Generate Physics Shape", "Physics Shape가 따로 없으면 외곽선으로 기본 충돌 모양을 만듭니다.",
                Toggle("Physics Shape", p => p.Sprite.GeneratePhysicsShape, (p, v) => p.Sprite.GeneratePhysicsShape = v)),
            advanced,
            Rule(),
            Field("Wrap Mode", "UV가 0~1을 벗어날 때: 반복 / 가장자리 늘이기 / 거울 / 한 번만 거울.",
                Choice("Wrap Mode", [TextureWrap.Repeat, TextureWrap.Clamp, TextureWrap.Mirror, TextureWrap.MirrorOnce], ["Repeat", "Clamp", "Mirror", "Mirror Once"],
                    p => p.Sprite.WrapMode, (p, v) => p.Sprite.WrapMode = v)),
            Field("Filter Mode", "확대·축소할 때의 보간. 픽셀 아트는 Point (no filter).",
                Choice("Filter Mode", [TextureFilter.Point, TextureFilter.Bilinear, TextureFilter.Trilinear], ["Point (no filter)", "Bilinear", "Trilinear"],
                    p => p.Sprite.FilterMode, (p, v) => p.Sprite.FilterMode = v)),
            Field("Aniso Level", "비스듬히 볼 때의 텍스처 품질(0~16). 밉맵이 있을 때 쓰입니다.",
                IntSlider("Aniso Level", 0, 16, p => p.Sprite.AnisoLevel, (p, v) => p.Sprite.AnisoLevel = v)),
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
        var folder = NameText("내보낼 폴더", "(프로젝트 폴더)", 240, p => p.Export.Folder, (p, v) => p.Export.Folder = v);
        var browse = Ui.Button("…", (_, _) => BrowseFolder(folder), 26).Tip("폴더 고르기");
        var format = Choice("파일 형식", [ExportImageFormat.Png, ExportImageFormat.Jpg, ExportImageFormat.Tga, ExportImageFormat.Bmp],
            ["PNG(무손실·투명)", "JPG(투명 없음)", "TGA", "BMP"], p => p.Export.ImageFormat, (p, v) => p.Export.ImageFormat = v, 170);
        var jpegLabel = Ui.Label("품질");
        var jpegQuality = IntNum("JPG 품질", 1, 100, 54, p => p.Export.JpegQuality, (p, v) => p.Export.JpegQuality = v);
        jpegQuality.Tip("JPG 품질 1~100. 높을수록 깨끗하고 파일이 큽니다.");
        _refresh.Add(p => jpegLabel.IsVisible = jpegQuality.IsVisible = p.Export.ImageFormat == ExportImageFormat.Jpg);
        var meta = Seg<bool?>(".meta 쓰기", [false, null, true], ["끔", "자동", "항상"], p => p.Export.WriteMeta, (p, v) => p.Export.WriteMeta = v)
            .SetOptionTip(0, "이미지만 내보냅니다(Unity를 쓰지 않을 때). 부위 아틀라스는 Sprite Library를 쓰면 .meta를 항상 씁니다.")
            .SetOptionTip(1, "Unity 프로젝트 Assets 안이거나 이미 .meta가 있을 때만 씁니다.")
            .SetOptionTip(2, "언제나 .meta를 같이 씁니다.");
        var fileBox = Ui.Stack(
            Setting("저장 시 내보내기", "프로젝트를 저장(Ctrl+S)할 때 이미지도 내보냅니다. 끄면 내보내기 단추로만 내보냅니다.",
                OnOff("저장 시 내보내기", p => p.Export.ExportOnSave, (p, v) => p.Export.ExportOnSave = v)),
            Setting("폴더", "비우면 프로젝트 파일과 같은 폴더(가까운 폴더는 프로젝트 기준 상대 경로로 저장).", Line(folder, browse)),
            Setting("파일 형식", "프레임 시트·부위 아틀라스 공통.", Line(format, jpegLabel, jpegQuality)),
            Setting("Unity .meta", "Unity가 읽는 스프라이트 설정(.meta)을 이미지 옆에 씁니다. 항목은 Import Settings 탭에 있습니다.", meta));

        // ── 프레임 시트 ──
        var singleFrame = Ui.Num(1, 9999, 1, 54);
        singleFrame.ValueChanged += (_, _) =>
        {
            int v = (int)singleFrame.NumValue() - 1;
            EditSettings("Single 프레임", p => p.Export.SingleFrameIndex != v, p => p.Export.SingleFrameIndex = v);
        };
        var singleRow = Setting("Single 프레임", "Sprite Mode가 Single일 때 내보낼 프레임(이미지 하나 = 스프라이트 하나, 캔버스 크기 그대로).", singleFrame);
        _refresh.Add(p =>
        {
            singleFrame.Maximum = Math.Max(1, p.Frames.Count);
            singleFrame.Value = Math.Clamp(p.Export.SingleFrameIndex + 1, 1, Math.Max(1, p.Frames.Count));
            singleRow.IsVisible = p.Sprite.Mode == SpriteMode.Single;
        });
        var exportPath = PathLabel();
        var framesBox = Ui.Stack(
            Setting("이름", "비우면 프로젝트 이름. 나누면 파일 이름 앞부분이 되고, 칸 이름 {이름}_{프레임}에도 씁니다.",
                NameText("내보낼 이름", "(프로젝트 이름)", 220, p => p.Export.FileName, (p, v) => p.Export.FileName = v)),
            singleRow,
            exportPath);

        // ── 부위 아틀라스 ──
        var keepRow = Setting("원래 위치 유지", "자동 배치일 때: 가져온 PNG의 칸 안에 아직 들어가는 부위는 원래 자리에 두고, 새로 생겼거나 커진 것만 빈 곳에 배치합니다(부위마다 따로 파일이면 쓰지 않음).",
            OnOff("원래 위치 유지", p => p.Export.KeepImportedRects, (p, v) => p.Export.KeepImportedRects = v));
        var libraryRow = Setting("Sprite Library", null, Line(
            OnOff("Sprite Library", p => p.Export.WriteSpriteLibrary, (p, v) => p.Export.WriteSpriteLibrary = v),
            Info("부위 아틀라스 옆에 Unity 2D Animation 패키지의 Sprite Library Asset을 그룹 × 스킨마다 하나씩 씁니다: {부위 아틀라스}_{그룹}@{스킨}.spriteLib(+ .meta). " +
                 "Category = 부위, Label = 프레임(프레임 번호 또는 {태그}_{번호}), 스프라이트 = 그 스킨의 부위 스프라이트(물려받는 칸은 첫 스킨 스프라이트, \"물려받은 칸도 이 스킨 스프라이트로\"면 그 스킨 것). " +
                 "\"고른 스킨\"이면 체크한 스킨만 .spriteLib을 씁니다. 부위 전용(개별) 스킨도 .spriteLib 하나({그룹}@{스킨}): 그 부위 Category만 그 스킨 스프라이트, 나머지 Category는 기본 스킨 스프라이트 — " +
                 "Category·Label이 다른 라이브러리와 같아 바꿔 끼우면 그 부위만 바뀝니다(\"기본 + 모자\"). " +
                 "같은 그룹의 라이브러리는 Category·Label이 같으므로 Sprite Resolver 키프레임은 그대로 두고 그룹의 Sprite Library 컴포넌트에 넣는 에셋만 바꾸면 스킨이 바뀝니다. " +
                 "스프라이트를 가리키려고 부위 아틀라스 .meta를 항상 씁니다(com.unity.2d.animation 패키지 필요).")));
        var libraryNote = Setting("", null, Note("그룹 × 스킨마다 .spriteLib — 그룹의 Sprite Library 에셋만 바꿔 끼우면 키프레임 그대로 스킨이 바뀝니다(부위 아틀라스 .meta 항상 씀)."));
        var partsPath = PathLabel();
        var libraryPath = PathLabel();

        var overrideRow = Setting("시트별 다르게", null, Line(
            Seg("시트별 다르게", [false, true], ["끔", "켬"], p => p.Export.PartsOverride is not null,
                    (p, v) => p.Export.PartsOverride = v ? p.Export.Common.Clone() : null)
                .SetOptionTip(0, "부위 아틀라스도 공통 시트 설정을 씁니다.")
                .SetOptionTip(1, "부위 아틀라스만 따로 설정합니다(켜는 순간의 공통 설정을 복사해 시작 — 아래에 설정 칸이 한 벌 더 생김)."),
            Info("공통 시트 설정(스킨·배치·정렬·칸 수·따로 파일·칸 이름·간격·잘라내기·2의 거듭제곱)을 부위 아틀라스만 다르게 쓰려면 켭니다. " +
                 "끄면 따로 둔 설정을 버리고 공통 설정을 씁니다(실행취소로 되돌릴 수 있음).")));
        // 파일을 열 때부터 따로 설정이 있었으면(예전 파일을 옮긴 경우 등) 그 까닭과 되돌리는 단추.
        bool overrideAtOpen = false;
        _atOpen.Add(p => overrideAtOpen = p.Export.PartsOverride is not null);
        var migrated = Beside(
            Note("이 파일은 부위 아틀라스가 따로 시트 설정을 씁니다 — 예전 파일이면 결과를 그대로 두려고 부위 아틀라스를 따로 두었습니다."),
            Ui.Button("공통으로 맞추기", (_, _) => EditSettings("공통으로 맞추기", p => p.Export.PartsOverride is not null, p => p.Export.PartsOverride = null))
                .Tip("부위 아틀라스도 공통 시트 설정을 쓰게 합니다(부위 아틀라스 결과가 바뀔 수 있음, 실행취소 가능)"));
        var overrideRows = SheetRows(p => p.Export.PartsOverride, overrideBlock: true);
        overrideRows.Margin = new Thickness(10, 2, 0, 0);
        _refresh.Add(p =>
        {
            migrated.IsVisible = overrideAtOpen && p.Export.PartsOverride is not null;
            overrideRows.IsVisible = p.Export.PartsOverride is not null;
        });
        var partsBox = Ui.Stack(
            Setting("이름", "비우면 {프레임 시트 이름}_parts. 나누면 파일 이름 앞부분이 됩니다.",
                NameText("부위 아틀라스 이름", "(시트 이름_parts)", 220, p => p.Export.PartsFileName, (p, v) => p.Export.PartsFileName = v)),
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
                exportPath.Text = "저장하면 프로젝트 옆에 시트를 내보냅니다.";
                partsPath.Text = "";
                libraryPath.Text = "";
                return;
            }
            // 셀 내용은 보지 않는다(설정을 바꿀 때마다 불리므로 가볍게) — 그린 것이 없는 부위도 셈.
            var (frames, partPaths) = ProjectExport.GetOutputPaths(p, p.Path, assumePartContent: true);
            exportPath.Text = OutputText(p, frames, "프레임 시트 내보내기 꺼짐");
            partsPath.Text = OutputText(p, partPaths, "부위 아틀라스 내보내기 꺼짐", partsAtlas: true);
            var libraries = ProjectExport.GetLibraryPaths(p, p.Path, assumePartContent: true);
            libraryPath.IsVisible = p.Export.WriteSpriteLibrary;
            libraryPath.Text = libraries.Count == 0 ? "→ (부위가 있는 그룹·내보낼 스킨 없음)"
                : $"→ {libraries[0]}" + (libraries.Count > 1 ? $" 외 {libraries.Count - 1}개" : "") + " (+ .meta)" +
                  (parts.Skins == SkinSource.Selected ? " — 체크한 스킨만" : "");
        });

        return (fileBox, framesBox, partsBox, advancedBox);
    }

    /// <summary>
    /// "공통" 묶음 내용: 내보낼 결과 + 두 결과가 같이 쓰는 시트 설정(<see cref="ExportSettings.Common"/>).
    /// 제목 <paramref name="title"/>은 지금 이 설정을 어느 결과가 쓰는지 알려 준다(부위 아틀라스가 따로 설정을 쓰면 프레임 시트만).
    /// </summary>
    private Control BuildCommonBlock(TextBlock title)
    {
        var output = Setting("내보낼 결과", null, OutputChoice());
        _refresh.Add(p =>
        {
            output.IsVisible = HasParts(p) || !p.Export.ExportFrames;
            bool parts = PartsShown(p), frames = FramesShown(p), partsCommon = parts && p.Export.PartsOverride is null;
            title.Text = frames && partsCommon ? "공통 — 프레임 시트·부위 아틀라스에 같이 적용"
                : frames ? (parts ? "공통 — 프레임 시트만(부위 아틀라스는 고급에서 따로 설정)" : "공통 — 프레임 시트")
                : partsCommon ? "공통 — 부위 아틀라스"
                : "공통 — 지금은 쓰이지 않음(부위 아틀라스는 고급에서 따로 설정)";
        });
        return Ui.Stack(output, SheetRows(p => p.Export.Common, overrideBlock: false));
    }

    /// <summary>
    /// 내보낼 결과(프레임 시트 / 부위 아틀라스 / 둘 다). 저장 값은 <see cref="ExportSettings.ExportFrames"/>·<see cref="ExportSettings.ExportParts"/> 두 개
    /// (둘 다 꺼진 예전 설정이면 아무것도 고르지 않은 모양).
    /// </summary>
    private SegmentedToggle OutputChoice() => Seg("내보낼 결과", [0, 1, 2], ["프레임 시트", "부위 아틀라스", "둘 다"],
            p => p.Export.ExportFrames && p.Export.ExportParts ? 2 : p.Export.ExportParts ? 1 : p.Export.ExportFrames ? 0 : -1,
            (p, v) =>
            {
                p.Export.ExportFrames = v != 1;
                p.Export.ExportParts = v != 0;
            })
        .SetOptionTip(0, "그룹·부위를 합성한 프레임마다 한 칸(애니메이션용)")
        .SetOptionTip(1, "부위 × 프레임마다 한 칸(부위별 스프라이트, 항상 Multiple)")
        .SetOptionTip(2, "두 결과를 따로 파일로");

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
            : $"{paths[0]} 외 {paths.Count - 1}개 ({string.Join(", ", paths.Skip(1).Take(3).Select(Path.GetFileName))}{(paths.Count > 4 ? ", …" : "")})";
        bool writes = partsAtlas ? ProjectExport.ShouldWritePartsMeta(p, paths[0]) : ProjectExport.ShouldWriteMeta(p, paths[0]);
        if (p.Export.WriteMeta == false && !writes) return $"→ {shown}";
        return $"→ {shown}" + (writes ? " (+ .meta)" : " (메타 없음: Unity Assets 밖)");
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
        if (Ui.PickFolder(this, "시트 이미지를 내보낼 폴더", initial) is not { } folder) return;
        if (p.Path is { } path)
        {
            var rel = Path.GetRelativePath(Path.GetDirectoryName(path)!, folder);
            if (!rel.StartsWith("..", StringComparison.Ordinal) || rel.Count(c => c == Path.DirectorySeparatorChar) < 3) folder = rel;
        }
        box.Text = folder == "." ? "" : folder;
        var v = NullIfEmpty(box.Text);
        EditSettings("내보낼 폴더", pr => pr.Export.Folder != v, pr => pr.Export.Folder = v);
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
        string u = overrideBlock ? "부위 아틀라스 " : "";

        // ── 스킨 ──
        var skins = Seg(u + "내보낼 스킨", [SkinSource.Current, SkinSource.Selected], ["지금 착장", "고른 스킨"], p => O(p).Skins, (p, v) => O(p).Skins = v)
            .SetOptionTip(0, "프레임 시트: 지금 착장 합성 한 벌. 부위 아틀라스: 부위마다 지금 보이는 스킨만.")
            .SetOptionTip(1, overrideBlock
                ? "체크한 스킨의 부위 스프라이트(첫 스킨이 아닌 스킨이 직접 그린 칸은 {이름}@{스킨})."
                : "체크한 스킨(과 착장 프리셋)마다: 프레임 시트는 그 그룹만 그 스킨으로 바꾼 합성 한 벌씩(나머지 그룹은 지금 착장), 부위 아틀라스는 체크한 스킨의 부위 스프라이트.");
        var pick = Ui.Button("스킨 고르기…", (_, _) => PickSkins(options, overrideBlock, PartsUse));
        pick.Tip("스킨·착장 프리셋·합성에 넣을 레이어 체크(확인하면 실행취소 1단계)");
        var skinRow = Setting("스킨", "내보낼 스킨과 합성에 넣을 레이어.", Line(skins, pick));
        var skinLabel = RowLabel(skinRow);
        _refresh.Add(p =>
        {
            var o = O(p);
            bool anySkin = PartGroups(p).Count > 0 || (!overrideBlock && p.Outfits.Count > 0) || o.Skins != SkinSource.Current;
            var tops = TopLayers(p);
            int excluded = tops.Count(o.ExcludedLayers.Contains);
            skins.IsVisible = anySkin;
            skinLabel.Text = anySkin ? "스킨" : "넣을 레이어";
            pick.Content = (anySkin ? $"스킨 고르기… ({SkinPickerWindow.SelectedCount(p, o, countOutfits: !overrideBlock)}개)" : "레이어 고르기…")
                           + (excluded > 0 ? $" · 레이어 {excluded}개 뺌" : "");
            skinRow.IsVisible = anySkin || tops.Count > 1 || excluded > 0;
        });
        var splitSkins = Setting("스킨 담기", "고른 스킨 항목을 파일에 담는 방법.",
            Seg(u + "스킨마다 따로 파일", [false, true], ["한 이미지", "따로 파일"], p => O(p).SplitSkins, (p, v) => O(p).SplitSkins = v)
                .SetOptionTip(0, "모든 스킨 항목을 한 이미지에(프레임 시트 칸 이름 {이름}@{스킨}).")
                .SetOptionTip(1, "스킨 항목마다 {시트}@{스킨} 파일 하나(+ .meta, 첫 항목은 원래 이름). 칸 이름은 파일마다 같아 Unity에서 시트만 바꿔 끼우면 됩니다."));
        _refresh.Add(p => splitSkins.IsVisible = O(p).Skins == SkinSource.Selected);

        // ── 배치 ──
        var layout = Setting("배치", null,
            Seg(u + "시트 배치", [SheetLayout.Grid, SheetLayout.Packed], ["격자", "자동 배치"], p => O(p).Layout, (p, v) => O(p).Layout = v)
                .SetOptionTip(0, "정렬 기준·줄바꿈·칸 수대로 줄과 열에 놓습니다(같은 프레임은 같은 열, 빈 칸은 비워 둠).")
                .SetOptionTip(1, "크기가 다른 칸을 빈틈이 적게 채워 넣습니다(최대 가로·넣는 순서)."));
        var order = OrderRow(O, PartsUse, u);
        var gridMode = Seg(u + "칸 수", [SheetGrid.Auto, SheetGrid.Columns, SheetGrid.Rows], ["자동", "가로 N칸", "세로 N칸"], p => O(p).Grid, (p, v) => O(p).Grid = v)
            .SetOptionTip(0, "시트가 정사각형에 가깝게(긴 변이 가장 작게 → 빈 칸이 적게 → 가로로 넓게).")
            .SetOptionTip(1, "한 줄에 N칸씩(넘치면 다음 줄).")
            .SetOptionTip(2, "전체 줄 수가 N 이하가 되게.");
        var gridCount = IntNum(u + "칸 수", 1, SheetOptions.MaxGridCount, 60, p => O(p).GridCount, (p, v) => O(p).GridCount = v);
        var gridRow = Setting("칸 수", "줄바꿈 단위마다 새 줄에서 시작하고, 그 안에서 칸 수로 줄을 바꿉니다.", Line(gridMode, gridCount));
        var maxWidthRow = Setting("최대 가로", "자동 배치 시트의 최대 가로. 0 = 자동(전체 넓이의 제곱근 정도, 가장 넓은 칸 이상).",
            Line(IntNum(u + "최대 가로", 0, PixelImage.MaxSize, 64, p => O(p).MaxWidth, (p, v) => O(p).MaxWidth = v), Ui.Label("px (0 = 자동)")));
        var packOrder = Setting("넣는 순서", null,
            Seg(u + "넣는 순서", [AtlasPackOrder.KeepOrder, AtlasPackOrder.LargestFirst], ["정렬 순서대로", "큰 것부터"], p => O(p).PackOrder, (p, v) => O(p).PackOrder = v)
                .SetOptionTip(0, "위 정렬 기준 순서대로 채워 넣습니다.")
                .SetOptionTip(1, "넓이가 큰 칸부터 넣습니다(빈틈이 더 적음)."));
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
        var tagsSplit = Line(Ui.Label("태그마다"), OnOff(u + "태그마다 따로 파일", p => O(p).SplitTags, (p, v) => O(p).SplitTags = v))
            .Tip("태그마다 {시트}_{태그} 이미지 하나(+ .meta), 태그 없는 프레임은 {시트}_untagged. 칸 이름은 한 장일 때와 같습니다. 태그가 없거나 프레임 시트가 Single 모드면 한 장.");
        tagsSplit.Margin = new Thickness(0, 0, 12, 0);
        var partsSplit = Line(Ui.Label("부위마다"), OnOff(u + "부위마다 따로 파일", p => O(p).SplitParts, (p, v) => O(p).SplitParts = v))
            .Tip("부위 아틀라스만: 부위마다 {부위 아틀라스}_{부위} 이미지 하나(+ .meta). 칸 이름은 한 장일 때와 같습니다(원래 위치 유지는 쓰지 않음).");
        var split = Setting("따로 파일", "한 이미지 대신 나눠 내보내기. 함께 켜면 겹쳐 나눕니다({시트}_{부위}_{태그}@{스킨}). 스킨은 위 \"스킨 담기\".",
            Line(tagsSplit, partsSplit));
        _refresh.Add(p => partsSplit.IsVisible = PartsUse(p));

        // ── 칸 이름 ──
        var naming = Setting("칸 이름", "Unity .meta에 쓰는 스프라이트 이름 규칙. 바꾸면 이미 Unity에서 쓰던 스프라이트 참조가 끊길 수 있습니다.",
            Seg(u + "칸 이름 규칙", [SpriteNaming.FrameNumber, SpriteNaming.ByTag], ["프레임 번호", "태그 기준"], p => O(p).Naming, (p, v) => O(p).Naming = v)
                .SetOptionTip(0, "프레임 시트 {시트}_{프레임}, 부위 아틀라스 {부위}(프레임 0)·{부위}_{프레임} — 프레임을 늘려도 이름이 그대로.")
                .SetOptionTip(1, "프레임 시트 {태그}_{태그 안 번호}, 부위 아틀라스 {부위}_{태그}_{번호}. 태그 없는 프레임은 프레임 번호 규칙."));
        var namingWarning = Warning("칸 이름 규칙을 바꿨습니다 — Unity는 스프라이트를 이름으로 이어받으므로, 이미 쓰던 곳(프리팹·애니메이션 등)의 참조가 끊길 수 있습니다. " +
                                    "되돌리려면 실행취소(Ctrl+Z).");
        SpriteNaming? namingAtOpen = null;
        _atOpen.Add(p => namingAtOpen = O(p).Naming);
        _refresh.Add(p => namingWarning.IsVisible = namingAtOpen is { } n && O(p).Naming != n);

        var spacing = Setting("칸 사이 간격", "옆 칸 색이 Unity에서 번지지 않게 칸 사이를 띄웁니다(기본 1).",
            Line(IntNum(u + "칸 간격", 0, 64, 54, p => O(p).Spacing, (p, v) => O(p).Spacing = v), Ui.Label("px")));
        var trim = Setting("테두리 잘라내기", "칸마다 투명한 테두리를 잘라냅니다(피벗은 같은 지점을, 9-slice 테두리는 같은 선을 가리키도록 보정).",
            OnOff(u + "테두리 잘라내기", p => O(p).Trim, (p, v) => O(p).Trim = v));
        var pot = Setting("2의 거듭제곱", "시트 가로·세로를 2의 거듭제곱(…, 256, 512, 1024 …)으로 맞춥니다.",
            OnOff(u + "2의 거듭제곱", p => O(p).PowerOfTwo, (p, v) => O(p).PowerOfTwo = v));

        // 프레임 시트만 이 설정을 쓰는데 Single 모드면 배치 설정은 쓰이지 않는다(프레임 하나를 캔버스 크기 그대로).
        var layoutRows = Ui.Stack(layout, order, gridRow, maxWidthRow, packOrder, split, naming, namingWarning, spacing, trim, pot);
        layoutRows.Margin = new Thickness(0);
        var singleNote = Note("프레임 시트가 Single 모드(Import Settings)라 프레임 하나를 캔버스 크기 그대로 내보냅니다 — 아래 배치 설정은 쓰이지 않습니다.");
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
        EditSettings(overrideBlock ? "부위 아틀라스 스킨 고르기" : "내보낼 스킨 고르기",
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
        return Setting("정렬", "칸을 놓는 순서: 위 기준부터(같은 값끼리 모임). 위치만 바뀌고 칸 이름은 그대로입니다. " +
                               "프레임 시트에는 부위 축이 없어 건너뜁니다. 격자면 줄 사이 \"여기서 새 줄\"을 골라 앞쪽 기준이 같은 칸끼리 한 줄로 묶고, " +
                               "자동 배치면 \"정렬 순서대로\" 넣을 때 씁니다.", list);
    }

    /// <summary>스킨이 둘 이상인(부위 전용 스킨 포함) 그룹이 있는지.</summary>
    private static bool HasSkins(SpriteProject p) => p.Layers.Any(l => l.IsGroup && l.AllSkins.Count > 1);

    /// <summary>합성에서 켜고 끌 수 있는 최상위 레이어(그룹 = 스킨 칸째, 일반 레이어), 타임라인처럼 위→아래.</summary>
    private static List<Layer> TopLayers(SpriteProject p) => p.Layers.Where(l => !l.IsPart).Reverse().ToList();

    /// <summary>부위 아틀라스에 들어가는(부위가 있는) 그룹, 위→아래.</summary>
    private static List<Layer> PartGroups(SpriteProject p) => p.Layers.Where(l => l.IsGroup && p.ChildCount(l) > 0).Reverse().ToList();

    /// <summary>작은 선택(2~4개) 단추 묶음(<see cref="Choice"/>의 <see cref="SegmentedToggle"/> 꼴). 값이 목록에 없으면 아무것도 고르지 않은 모양.</summary>
    private SegmentedToggle Seg<T>(string undo, T[] values, string[] labels, Func<SpriteProject, T> get, Action<SpriteProject, T> set)
    {
        var s = new SegmentedToggle(labels);
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
    private SegmentedToggle OnOff(string undo, Func<SpriteProject, bool> get, Action<SpriteProject, bool> set)
        => Seg(undo, [false, true], ["끔", "켬"], get, set);

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
    private Control IntSlider(string undo, int min, int max, Func<SpriteProject, int> get, Action<SpriteProject, int> set)
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
        var num = Ui.Num(min, max, min, 40);
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

    private static TabItem Tab(string header, Control content) => new()
    {
        Header = new TextBlock { Text = header, FontSize = 13 },
        Content = content,
        MinHeight = 0,
        Padding = new Thickness(10, 4),
    };

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

    /// <summary>긴 도움말을 담는 ⓘ 표시(올리면 풍선).</summary>
    private static TextBlock Info(string tip)
    {
        var t = new TextBlock
        {
            Text = "ⓘ",
            Foreground = AppTheme.DimTextBrush,
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 0),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Help),
        };
        return t.Tip(tip);
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
            _tabs.IsEnabled = _ctx.HasProject;
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
            var header = EntryRow("칸 이름(.meta 스프라이트 이름)", numberHeader, "시트 위치");
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
            : $"따로 파일 {files.Count}개 — " + string.Join(", ", files.Take(4).Select(f => f.Name + ext)) + (files.Count > 4 ? ", …" : "");

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
        if (_tabs.SelectedItem != _mainTab) return;
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
        string Off(bool on) => !p.Export.ExportOnSave ? " — 저장할 때 내보내기 꺼짐(내보내기 단추로만)" : on ? "" : " — 꺼짐: 만들지 않음";
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
                : p.Sprite.Mode == SpriteMode.Single ? " (Single 모드라 한 장)" : p.Tags.Count == 0 ? " (태그가 없어 한 장)" : "";
            _framesSection.Title.Text = $"{_framesSection.Number}프레임 시트: {FileNames(frameFiles, ext)}{splitNote}{Off(p.Export.ExportFrames)}";
            FillSection(_framesSection, frameFiles, ext, showNames: false, "번호", e => _ctx.SetCurrentFrame(e.FrameIndex));
        }
        else _framesSection.Clear();
        if (showParts)
        {
            var partsName = p.Path is null ? p.Export.PartsFileName ?? baseName + "_parts" : Path.GetFileNameWithoutExtension(ProjectExport.GetPartsImagePath(p));
            var partFiles = ProjectExport.BuildPartsSheets(p, partsName);
            _partsSection.Title.Text = $"{_partsSection.Number}부위 아틀라스: {FileNames(partFiles, ext)}{Off(p.Export.ExportParts)}";
            FillSection(_partsSection, partFiles, ext, showNames: true, "프레임", SelectPartEntry);
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
            AppTheme.DrawTextIn(g, $"{bw}×{bh} · 칸 {_sheet.Entries.Count}개", new Rect(2, bounds.Height - 14, Math.Max(1, bounds.Width - 4), 14), AppTheme.DimTextBrush, small);
        }
    }
}
