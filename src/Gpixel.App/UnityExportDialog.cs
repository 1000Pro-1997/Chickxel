using System.Globalization;
using Avalonia.Media.Immutable;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 내보내기 설정 창(Ctrl+E, 파일 > 내보내기…; 파란 "내보내기"는 OK로 닫혀 호출한 쪽이 내보냄). 바꾸면 바로 프로젝트에 적용된다(설정 하나마다 실행취소 1단계).
/// "내보내기" 탭은 저장 시 내보내기·파일 형식·Unity .meta 쓰기·폴더와 프레임 시트·부위 아틀라스 설정,
/// "Import Settings" 탭은 Unity Texture Importer 인스펙터와 같은 항목·이름(.meta를 쓸 때만 보임, 프레임 시트·부위 아틀라스 공통),
/// "미리보기" 탭은 만들어질 시트와 칸 목록(읽기 전용).
/// </summary>
public sealed class UnityExportDialog : DialogBase
{
    private const int LabelWidth = 160;
    private static readonly IImmutableSolidColorBrush CellPenColor = new ImmutableSolidColorBrush(Color.FromArgb(200, 230, 200, 90));
    private static readonly string[] PivotNames = ["Center", "Top Left", "Top", "Top Right", "Left", "Right", "Bottom Left", "Bottom", "Bottom Right", "Custom"];

    private readonly EditorContext _ctx;
    private readonly Action<float>? _ppuChanged;
    /// <summary>프로젝트 → 화면 갱신(설정 칸마다 하나).</summary>
    private readonly List<Action<SpriteProject>> _refresh = new();
    /// <summary>글자 칸의 아직 반영하지 않은 값을 반영(창을 닫을 때).</summary>
    private readonly List<Action> _commitTexts = new();
    private readonly TabControl _tabs;
    private readonly TabItem _exportTab, _importTab, _previewTab;
    private readonly SheetPreview _sheet, _partsSheet;
    private readonly ListBox _entries, _partEntries;
    private readonly Control _partsPreview;
    private readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly EventHandler _onReplaced, _onCurrent;
    private readonly EventHandler<ProjectChangedEventArgs> _onChanged;
    private bool _updating, _previewDirty = true, _customPivot;

    /// <param name="ctx">편집기 상태(현재 프로젝트).</param>
    /// <param name="ppuChanged">사용자가 PPU를 바꿨을 때(마지막 PPU 기억용).</param>
    public UnityExportDialog(EditorContext ctx, Action<float>? ppuChanged = null) : base("내보내기 설정")
    {
        _ctx = ctx;
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
        Width = 560;
        Height = 720;
        MinWidth = 460;
        MinHeight = 420;

        (_sheet, _entries, var framesPreview) = BuildPreview("만들어질 프레임 시트(읽기 전용)", "번호", e => _ctx.SetCurrentFrame(e.FrameIndex));
        (_partsSheet, _partEntries, _partsPreview) = BuildPreview("만들어질 부위 아틀라스(읽기 전용)", "프레임", SelectPartEntry);
        _previewTab = Tab("미리보기", Ui.ScrollHost(Ui.Stack(framesPreview, _partsPreview)));

        _exportTab = Tab("내보내기", Ui.ScrollHost(BuildExportTab()));
        _importTab = Tab("Import Settings", Ui.ScrollHost(BuildImportTab()));
        _tabs = new TabControl { Padding = new Thickness(0, 4, 0, 0) };
        _tabs.Items.Add(_exportTab);
        _tabs.Items.Add(_importTab);
        _tabs.Items.Add(_previewTab);
        _tabs.SelectedIndex = 0;
        // Unity Import Settings는 .meta를 쓸 때만 의미가 있다.
        _refresh.Add(p =>
        {
            bool meta = p.Export.WriteMeta != false;
            _importTab.IsVisible = meta;
            if (!meta && _tabs.SelectedItem == _importTab) _tabs.SelectedItem = _exportTab;
        });
        _tabs.SelectionChanged += (_, e) =>
        {
            // 안쪽 목록·콤보의 SelectionChanged도 여기로 올라오므로 탭 자신의 것만 본다.
            if (e.Source == _tabs && _tabs.SelectedItem == _previewTab && _previewDirty) RebuildPreview();
        };

        // 대화상자 본문(StackPanel)은 높이를 주지 않으므로 탭을 바깥 DockPanel의 채우기 칸에 바로 둔다.
        var root = (DockPanel)Content!;
        root.Children.Remove(Body);
        root.Children.Add(_tabs);

        _rebuild.Tick += (_, _) => { _rebuild.Stop(); RebuildPreview(); };
        _onReplaced = (_, _) => { _customPivot = false; RefreshAll(); ScheduleRebuild(); };
        _onChanged = (_, e) =>
        {
            RefreshAll();
            if (!(e.Change == ProjectChange.Pixels && _ctx.Project?.IsStrokeActive == true)) ScheduleRebuild();
        };
        _onCurrent = (_, _) => { RefreshAll(); HighlightCurrent(); };
        ctx.ProjectReplaced += _onReplaced;
        ctx.ProjectChanged += _onChanged;
        ctx.CurrentChanged += _onCurrent;
        RefreshAll();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // Enter(닫기 단추)로 닫아도 입력 중이던 이름·폴더가 반영되게.
        foreach (var commit in _commitTexts) commit();
    }

    protected override void OnClosed(EventArgs e)
    {
        _ctx.ProjectReplaced -= _onReplaced;
        _ctx.ProjectChanged -= _onChanged;
        _ctx.CurrentChanged -= _onCurrent;
        _rebuild.Stop();
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

    private Control BuildExportTab()
    {
        var folder = NameText("내보낼 폴더", "(프로젝트 폴더)", 260, p => p.Export.Folder, (p, v) => p.Export.Folder = v);
        var browse = Ui.Button("…", (_, _) => BrowseFolder(folder), 26);

        var singleFrame = Ui.Num(1, 9999, 1, 54);
        singleFrame.Tip("Sprite Mode가 Single일 때 내보낼 프레임");
        singleFrame.ValueChanged += (_, _) =>
        {
            int v = (int)singleFrame.NumValue() - 1;
            EditSettings("Single 프레임", p => p.Export.SingleFrameIndex != v, p => p.Export.SingleFrameIndex = v);
        };
        _refresh.Add(p =>
        {
            singleFrame.Maximum = Math.Max(1, p.Frames.Count);
            singleFrame.Value = Math.Clamp(p.Export.SingleFrameIndex + 1, 1, Math.Max(1, p.Frames.Count));
            singleFrame.IsEnabled = p.Sprite.Mode == SpriteMode.Single;
        });
        var columns = IntNum("열 수", 0, 1024, 54, p => p.Export.Columns, (p, v) => p.Export.Columns = v);
        columns.Tip("0 = 한 줄");
        var exportPath = PathLabel();
        var framesBox = Ui.Group("프레임 시트(애니메이션)", Ui.Stack(
            Toggle("프레임 시트 내보내기", p => p.Export.ExportFrames, (p, v) => p.Export.ExportFrames = v, "프레임 시트 내보내기"),
            Field("배치", null, Choice("시트 배치", [SheetLayout.Columns, SheetLayout.RowPerTag], ["가로 N칸(프레임 순)", "태그마다 한 줄"],
                p => p.Export.Layout, (p, v) => p.Export.Layout = v)),
            Field("열 수", null, columns),
            Toggle("테두리 잘라내기", p => p.Export.Trim, (p, v) => p.Export.Trim = v, "빈 테두리 잘라내기(피벗 보정)"),
            Field("Single 프레임", null, singleFrame),
            Field("이름", null, NameText("내보낼 이름", "(프로젝트 이름)", 220, p => p.Export.FileName, (p, v) => p.Export.FileName = v)),
            exportPath));

        var atlasMaxWidth = IntNum("아틀라스 최대 가로", 0, PixelImage.MaxSize, 64, p => p.Export.AtlasMaxWidth, (p, v) => p.Export.AtlasMaxWidth = v);
        atlasMaxWidth.Tip("0 = 자동(전체 넓이의 제곱근 정도, 가장 넓은 부위 이상)");
        var keepRects = Toggle("원래 위치 유지", p => p.Export.KeepImportedRects, (p, v) => p.Export.KeepImportedRects = v, "원래 위치 유지(가져온 칸 그대로)");
        keepRects.Tip("가져온 PNG의 칸 안에 아직 들어가는 부위는 원래 자리에 두고, 새로 생겼거나 커진 것만 빈 곳에 배치");
        var partsPath = PathLabel();
        var partsBox = Ui.Group("부위 아틀라스(부위마다 칸 하나, 항상 Multiple)", Ui.Stack(
            Toggle("부위 아틀라스 내보내기", p => p.Export.ExportParts, (p, v) => p.Export.ExportParts = v, "부위 아틀라스 내보내기"),
            Field("최대 가로(px)", null, atlasMaxWidth),
            Field("순서", null, Choice("아틀라스 순서", [AtlasPackOrder.KeepOrder, AtlasPackOrder.LargestFirst], ["목록 순서대로", "큰 것부터"],
                p => p.Export.AtlasOrder, (p, v) => p.Export.AtlasOrder = v)),
            Toggle("2의 거듭제곱", p => p.Export.AtlasPowerOfTwo, (p, v) => p.Export.AtlasPowerOfTwo = v, "2의 거듭제곱 크기"),
            keepRects,
            Field("이름", null, NameText("부위 아틀라스 이름", "(시트 이름_parts)", 220, p => p.Export.PartsFileName, (p, v) => p.Export.PartsFileName = v)),
            partsPath));

        _refresh.Add(p =>
        {
            partsBox.IsVisible = p.Layers.Any(l => l.IsPart);
            if (p.Path is null)
            {
                exportPath.Text = "저장하면 프로젝트 옆에 시트를 내보냅니다.";
                partsPath.Text = "";
                return;
            }
            exportPath.Text = OutputText(p, ProjectExport.GetExportImagePath(p));
            partsPath.Text = OutputText(p, ProjectExport.GetPartsImagePath(p));
        });

        // .meta: 켜면 "언제"(자동/항상), 끄면 이미지만.
        var meta = Ui.Check("Unity .meta 같이 쓰기");
        meta.Tip("Unity가 읽는 스프라이트 설정(.meta)을 이미지 옆에 씁니다. Unity를 쓰지 않으면 끄세요(이미지만 내보냄).");
        meta.IsCheckedChanged += (_, _) =>
        {
            bool on = meta.On();
            EditSettings(".meta 쓰기", p => (p.Export.WriteMeta != false) != on, p => p.Export.WriteMeta = on ? null : false);
        };
        _refresh.Add(p => meta.IsChecked = p.Export.WriteMeta != false);
        var metaWhen = Field("언제", null, Choice<bool?>("메타 쓰기", [null, true], ["자동(Unity Assets 안이거나 이미 .meta가 있을 때)", "항상"],
            p => p.Export.WriteMeta, (p, v) => p.Export.WriteMeta = v, 300));
        metaWhen.Margin = new Thickness(24, 1, 0, 1);
        _refresh.Add(p => metaWhen.IsVisible = p.Export.WriteMeta != false);

        var jpegQuality = Field("JPG 품질", "1~100. 높을수록 깨끗하고 파일이 큽니다.",
            IntNum("JPG 품질", 1, 100, 54, p => p.Export.JpegQuality, (p, v) => p.Export.JpegQuality = v));
        _refresh.Add(p => jpegQuality.IsVisible = p.Export.ImageFormat == ExportImageFormat.Jpg);

        return Ui.Stack(
            Toggle("저장 시 내보내기", p => p.Export.ExportOnSave, (p, v) => p.Export.ExportOnSave = v, "저장할 때 이미지 내보내기"),
            Field("파일 형식", "프레임 시트·부위 아틀라스 공통.",
                Choice("파일 형식", [ExportImageFormat.Png, ExportImageFormat.Jpg, ExportImageFormat.Tga, ExportImageFormat.Bmp],
                    ["PNG(무손실·투명)", "JPG(투명 없음)", "TGA", "BMP"], p => p.Export.ImageFormat, (p, v) => p.Export.ImageFormat = v)),
            jpegQuality,
            meta,
            metaWhen,
            Field("칸 간격", null, IntNum("칸 간격", 0, 64, 54, p => p.Export.Spacing, (p, v) => p.Export.Spacing = v)),
            Field("폴더", null, Ui.Row(folder, browse)),
            framesBox,
            partsBox);
    }

    /// <summary>내보낼 경로 줄: .meta를 쓰기로 했으면 이번에 쓰는지도.</summary>
    private static string OutputText(SpriteProject p, string path)
    {
        if (p.Export.WriteMeta == false) return $"→ {path}";
        return $"→ {path}" + (ProjectExport.ShouldWriteMeta(p, path) ? " (+ .meta)" : " (메타 없음: Unity Assets 밖)");
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

    /// <summary>Unity 인스펙터처럼 왼쪽 이름표(<see cref="LabelWidth"/>) + 오른쪽 편집 칸 한 줄.</summary>
    private static Grid Field(string label, string? tip, Control editor)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*"), Margin = new Thickness(0, 1) };
        var l = Ui.Label(label);
        l.TextTrimming = TextTrimming.CharacterEllipsis;
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

    // ───────────── 프로젝트 → 화면 ─────────────

    private void RefreshAll()
    {
        _updating = true;
        try
        {
            _tabs.IsEnabled = _ctx.HasProject;
            if (_ctx.Project is not { } p) return;
            foreach (var refresh in _refresh) refresh(p);
            _partsPreview.IsVisible = p.Layers.Any(l => l.IsPart);
        }
        finally
        {
            _updating = false;
        }
    }

    // ───────────── 시트 미리보기 ─────────────

    /// <summary>시트 미리보기 + 칸 목록(고르면 <paramref name="select"/>).</summary>
    private static (SheetPreview Sheet, ListBox Entries, Control Panel) BuildPreview(string title, string numberHeader, Action<SheetEntry> select)
    {
        var sheet = new SheetPreview { Height = 240, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch };
        var header = EntryRow("칸 이름", numberHeader, "시트 위치");
        foreach (var t in header.Children.OfType<TextBlock>()) t.Foreground = AppTheme.DimTextBrush;
        header.Margin = new Thickness(6, 3, 6, 0);
        var entries = new ListBox
        {
            Height = 180,
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
        return (sheet, entries, Ui.Stack(Ui.Label(title, bold: true), sheet, header, entries));
    }

    /// <summary>부위 아틀라스 칸 → 그 부위 레이어·프레임을 현재로.</summary>
    private void SelectPartEntry(SheetEntry e)
    {
        if (_ctx.Project is not { } p) return;
        int layer = PartLayerOf(p, e);
        if (layer >= 0) _ctx.SetCurrent(layer, e.FrameIndex);
        else _ctx.SetCurrentFrame(e.FrameIndex);
    }

    /// <summary>부위 아틀라스 칸의 부위 레이어 번호(없으면 -1).</summary>
    private static int PartLayerOf(SpriteProject p, SheetEntry e) => e.Part is { } part ? p.IndexOf(part) : -1;

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

    /// <summary>미리보기 탭을 보고 있으면 잠시 뒤 다시 만들고, 아니면 탭을 열 때 만든다.</summary>
    private void ScheduleRebuild()
    {
        _previewDirty = true;
        if (_tabs.SelectedItem != _previewTab) return;
        _rebuild.Stop();
        _rebuild.Start();
    }

    private void RebuildPreview()
    {
        _rebuild.Stop();
        _previewDirty = false;
        if (_ctx.Project is not { } p)
        {
            _sheet.SetSheet(null);
            _entries.Items.Clear();
            _partsSheet.SetSheet(null);
            _partEntries.Items.Clear();
            return;
        }
        var baseName = p.Path is null ? p.BaseName : Path.GetFileNameWithoutExtension(ProjectExport.GetExportImagePath(p));
        FillPreview(_sheet, _entries, ProjectExport.BuildSheet(p, baseName), showNames: false);
        if (p.Layers.Any(l => l.IsPart))
        {
            var partsName = p.Path is null ? p.Export.PartsFileName ?? baseName + "_parts" : Path.GetFileNameWithoutExtension(ProjectExport.GetPartsImagePath(p));
            FillPreview(_partsSheet, _partEntries, ProjectExport.BuildPartsSheet(p, partsName), showNames: true);
        }
        else
        {
            _partsSheet.SetSheet(null);
            _partEntries.Items.Clear();
        }
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

    /// <summary>미리보기에서 현재 프레임 칸(부위 아틀라스는 현재 부위·프레임 칸)을 강조한다.</summary>
    private void HighlightCurrent()
    {
        int frame = _ctx.CurrentFrame;
        if (_sheet.Sheet is { } sheet)
            _sheet.Highlight = sheet.Entries.FirstOrDefault(e => e.FrameIndex == frame)?.Rect;
        if (_partsSheet.Sheet is { } parts && _ctx.Project is { } p)
        {
            int layer = _ctx.CurrentLayer;
            _partsSheet.Highlight = _ctx.CurrentPart is null ? null
                : parts.Entries.FirstOrDefault(e => e.FrameIndex == frame && PartLayerOf(p, e) == layer)?.Rect;
        }
    }

    /// <summary>만들어질 시트와 칸 테두리(부위 아틀라스면 칸 이름도)를 보여 준다.</summary>
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
            InvalidateVisual();
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
