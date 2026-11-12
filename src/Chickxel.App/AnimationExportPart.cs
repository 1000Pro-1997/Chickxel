using System.Globalization;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 내보내기 창(<see cref="ExportDialog"/>)의 애니메이션 형식 부분 — GIF 애니메이션 · 움직이는 PNG(APNG) · 프레임마다 PNG(폴더로).
/// 왼쪽 묶음(<see cref="Groups"/>): 범위(현재 태그 / 모든 프레임 / 고른 태그 — 태그 방향(역방향·왕복) 그대로), 배율(1~10×, 최근접), 속도(<see cref="GifTimingField"/>:
/// 원래 속도 / 배속 / 같은 FPS), 반복(무한 / N번), 배경(투명 / 색). 프레임마다 PNG는 속도·반복이 없다.
/// 오른쪽(<see cref="PreviewColumn"/>): 그 배율·속도로 재생되는 미리보기 + 결과 정보 + 내보낸 결과(크기·용량).
/// 그림은 프레임 시트와 똑같이 합성한다(<see cref="GifWriter.ProjectFrames"/>: 레이어 효과·지금 착장, 숨긴·내보내기에서 뺀 레이어 제외 — 처음 보일 때 합성).
/// GIF는 투명이 알파 50% 문턱·프레임마다 256색이고, APNG·PNG는 색 수 제한 없이 알파를 그대로 쓴다(배경색을 고르면 그 위에 합성).
/// 프로젝트는 바꾸지 않는다. 속도·반복·배경은 앱 설정(<see cref="AppSettings"/>)에 기억한다.
/// </summary>
public sealed class AnimationExportPart
{
    private const int MaxScale = 10;
    /// <summary>APNG 한 프레임의 가장 짧은 길이(ms) — 미리보기·GIF와 같은 최소(브라우저가 더 짧은 지연을 느리게 바꿈).</summary>
    private const int MinFrameMs = GifWriter.MinDelayCs * 10;
    /// <summary>대상 도움말(F1) 가이드 쪽("GIF · APNG · 프레임 PNG로 내보내기").</summary>
    public const string GuidePage = "screen-gif";

    private readonly Window _owner;
    private readonly SpriteProject _project;
    private readonly AppSettings _settings;
    private readonly Action<bool> _canExportChanged;
    private readonly AnimationTag? _currentTag;
    private readonly RadioButton _rangeCurrent, _rangeAll, _rangeTag, _loopForever, _loopCount, _bgTransparent, _bgColor;
    private readonly ComboBox _tagCombo;
    private readonly NumericUpDown _scale, _plays;
    private readonly ColorField _color;
    private readonly GifTimingField _timing;
    private readonly Border _timingGroup, _loopGroup;
    private readonly Preview _preview;
    private readonly TextBlock _info, _result;
    private readonly DispatcherTimer _timer = new();
    /// <summary>파일을 썼음(경로들, 상태 표시줄 문구).</summary>
    private readonly Action<IReadOnlyList<string>, string> _exported;
    /// <summary>합성한 범위의 프레임(재생 순서). null = 아직 합성하지 않음(범위가 바뀌면 다시).</summary>
    private List<GifFrame>? _frames;
    private int _index;
    private ExportFormat _format = ExportFormat.Gif;
    private bool _shown;

    /// <param name="owner">이 부분을 담은 창(저장·폴더 고르기 창과 진행 덮개의 주인).</param>
    /// <param name="canExportChanged">내보낼 프레임이 있는지가 정해짐(내보내기 단추 켜고 끄기).</param>
    /// <param name="exported">파일을 썼음(경로들, 상태 표시줄 문구).</param>
    public AnimationExportPart(EditorContext ctx, AppSettings settings, Window owner, Action<bool> canExportChanged, Action<IReadOnlyList<string>, string> exported)
    {
        _owner = owner;
        _project = ctx.Project ?? throw new InvalidOperationException(UiStrings.S_GifExportDialog_GifExportDialog_E1EFCD56F3);
        _settings = settings;
        _canExportChanged = canExportChanged;
        _exported = exported;
        _currentTag = _project.TagOfFrame(ctx.CurrentFrame);

        _rangeCurrent = Radio("range", _currentTag is { } ct ? string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_GifExportDialog_9970DF5446, ct.Name, DirectionText(ct)) : UiStrings.S_GifExportDialog_GifExportDialog_5F18243314);
        _rangeCurrent.IsEnabled = _currentTag is not null;
        _rangeAll = Radio("range", string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_GifExportDialog_B4BA578A27, _project.Frames.Count));
        _rangeTag = Radio("range", UiStrings.S_GifExportDialog_GifExportDialog_99C0D7A91D);
        _tagCombo = Ui.Combo(165, _project.Tags.Select(t => $"{t.Name}  [{t.From}–{t.To}]{DirectionText(t)}").ToArray());
        _rangeTag.IsEnabled = _tagCombo.IsEnabled = _project.Tags.Count > 0;
        if (_currentTag is not null) _tagCombo.SelectedIndex = _project.Tags.ToList().IndexOf(_currentTag);
        (_currentTag is not null ? _rangeCurrent : _rangeAll).IsChecked = true;

        int side = Math.Max(_project.Width, _project.Height);
        _scale = Ui.Num(1, MaxScale, Math.Clamp((int)Math.Round(256.0 / side), 1, MaxScale), width: 60)
            .Presets(1, 2, 3, 4, 5, 6, 8, 10).Unit("×").RecentKey("gif.scale");
        _loopForever = Radio("loop", UiStrings.S_GifExportDialog_GifExportDialog_4ED3569BED);
        _loopCount = Radio("loop", UiStrings.S_GifExportDialog_GifExportDialog_E8CE6F1D02);
        _plays = Ui.Num(1, 100, Math.Max(1, settings.GifPlayCount), width: 60).Presets(1, 2, 3, 4, 5, 10, 20).Unit(UiStrings.S_GifExportDialog_GifExportDialog_D748D56877).RecentKey("gif.plays");
        (settings.GifPlayCount <= 0 ? _loopForever : _loopCount).IsChecked = true;

        _bgTransparent = Radio("bg", UiStrings.S_GifExportDialog_GifExportDialog_D4D8F12988);
        _bgColor = Radio("bg", UiStrings.S_GifExportDialog_GifExportDialog_E03CFDC4D0);
        _color = new ColorField(Color32.TryParseHex(settings.GifBackgroundColor, out var bg) ? bg.WithAlpha(255) : Color32.White);
        (settings.GifUseBackground ? _bgColor : _bgTransparent).IsChecked = true;
        _timing = new GifTimingField(settings);

        _preview = new Preview { MinWidth = 384, MinHeight = 402 };
        _info = new TextBlock { Foreground = AppTheme.TextBrush, Margin = new Thickness(2, 4, 2, 0), TextWrapping = TextWrapping.Wrap };
        _result = new TextBlock { Foreground = AppTheme.AccentBrush, Margin = new Thickness(2, 2, 2, 0), TextWrapping = TextWrapping.Wrap, IsVisible = false };

        // 대상 도움말(F1): 묶음마다 설정 목록의 그 항목(①~⑤), 태그 목록 ⑧ 그림, 미리보기 ⑥·정보 ⑦ 문단. 묶음 상자는 빈 곳도 눌리게 투명 배경을 준다.
        static Border Section(string title, Control content, string? anchor)
        {
            var group = Ui.Group(title, content).HelpAt(GuidePage, anchor: anchor);
            group.Background = Brushes.Transparent;
            return group;
        }
        _tagCombo.HelpAt(GuidePage, shot: "screen-gif-2");
        _preview.HelpAt(GuidePage, anchor: "The preview on the right plays");
        _info.HelpAt(GuidePage, anchor: "⑦");
        _timingGroup = Section(UiStrings.GifTiming_Label, _timing, "Speed multiplier");
        _loopGroup = Section(UiStrings.S_GifExportDialog_GifExportDialog_C51C583CC7, Ui.Stack(_loopForever, Ui.Row(_loopCount, _plays, Ui.Label(UiStrings.S_GifExportDialog_GifExportDialog_D748D56877))), "Loop");
        Groups = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Section(UiStrings.S_GifExportDialog_GifExportDialog_463F80B168, Ui.Stack(_rangeCurrent, _rangeAll, Ui.Row(_rangeTag, _tagCombo)), "Range"),
                Section(UiStrings.S_GifExportDialog_GifExportDialog_012E0A74BF, Ui.Row(_scale, Ui.Label(UiStrings.S_GifExportDialog_GifExportDialog_5AE6A9747B)), "Zoom"),
                _timingGroup,
                _loopGroup,
                Section(UiStrings.S_GifExportDialog_GifExportDialog_7934F88ACA, Ui.Stack(_bgTransparent, Ui.Row(_bgColor, _color)), "Background"),
            },
        };
        // 미리보기는 남는 자리를 채우고, 정보·결과는 그 아래.
        DockPanel.SetDock(_result, Dock.Bottom);
        DockPanel.SetDock(_info, Dock.Bottom);
        PreviewColumn = new DockPanel { Margin = new Thickness(10, 0, 0, 0), LastChildFill = true, Children = { _result, _info, _preview } };

        foreach (var r in new[] { _rangeCurrent, _rangeAll, _rangeTag })
            r.IsCheckedChanged += (_, _) => { if (r.On()) Dispatcher.UIThread.Post(RebuildFrames); };
        _tagCombo.SelectionChanged += (_, _) =>
        {
            if (_tagCombo.SelectedIndex < 0) return;
            if (!_rangeTag.On()) _rangeTag.IsChecked = true; // 바뀜 처리기가 다시 만든다
            else RebuildFrames();
        };
        foreach (var r in new[] { _bgTransparent, _bgColor })
            r.IsCheckedChanged += (_, _) => { if (r.On()) Dispatcher.UIThread.Post(RebuildPreview); };
        _color.ColorChanged += (_, _) =>
        {
            if (!_bgColor.On()) _bgColor.IsChecked = true;
            else RebuildPreview();
        };
        _scale.ValueChanged += (_, _) => { _preview.Scale = Scale; UpdateInfo(); };
        _plays.ValueChanged += (_, _) => { if (!_loopCount.On()) _loopCount.IsChecked = true; };
        _timing.Changed += () =>
        {
            Restart();
            UpdateInfo();
        };
        _timer.Tick += (_, _) => Advance();
        _preview.Scale = Scale;
    }

    /// <summary>왼쪽 설정 묶음들(범위·배율·속도·반복·배경).</summary>
    public Control Groups { get; }
    /// <summary>오른쪽: 재생 미리보기 + 결과 정보 + 내보낸 결과.</summary>
    public Control PreviewColumn { get; }

    public int Scale => (int)_scale.NumValue();
    public int PlayCount => _loopForever.On() ? 0 : (int)_plays.NumValue();
    public Color32? BackgroundColor => _bgColor.On() ? _color.Color.WithAlpha(255) : null;

    /// <summary>고른 범위의 태그(모든 프레임이면 null).</summary>
    public AnimationTag? RangeTag
        => _rangeCurrent.On() ? _currentTag
            : _rangeTag.On() && _tagCombo.SelectedIndex >= 0 && _tagCombo.SelectedIndex < _project.Tags.Count ? _project.Tags[_tagCombo.SelectedIndex]
            : null;

    /// <summary>기본 이름(확장자 없음): 태그면 <c>{프로젝트}_{태그}</c>, 모든 프레임이면 <c>{프로젝트}</c>.</summary>
    private string BaseName
    {
        get
        {
            var name = _project.Path is { } p ? Path.GetFileNameWithoutExtension(p) : ProjectExport.CleanFileName(_project.DisplayName);
            return RangeTag is { } t ? $"{name}_{ProjectExport.CleanFileName(t.Name)}" : name;
        }
    }

    /// <summary>
    /// <paramref name="format"/>(GIF·APNG·프레임마다 PNG)으로 보인다: 쓰지 않는 묶음을 숨기고, 처음이면 프레임을 합성하고, 재생을 시작한다.
    /// </summary>
    public void Show(ExportFormat format)
    {
        bool formatChanged = _format != format;
        _format = format;
        _shown = true;
        bool frames = format == ExportFormat.Frames;
        _timingGroup.IsVisible = _loopGroup.IsVisible = !frames;
        _bgTransparent.Content = format == ExportFormat.Gif ? UiStrings.S_GifExportDialog_GifExportDialog_D4D8F12988 : UiStrings.Export_BgKeepAlpha;
        if (formatChanged) _result.IsVisible = false;
        if (_frames is null) RebuildFrames();
        else RebuildPreview();
    }

    /// <summary>다른 형식으로 바뀜: 재생을 멈춘다.</summary>
    public void Hide()
    {
        _shown = false;
        _timer.Stop();
    }

    /// <summary>창이 닫힘: 재생을 멈추고 미리보기 그림을 버리고 설정을 기억한다.</summary>
    public void Close()
    {
        Hide();
        _preview.SetFrames([], null);
        SaveSettings();
    }

    private static string DirectionText(AnimationTag t) => t.Direction switch
    {
        TagDirection.Reverse => UiStrings.S_GifExportDialog_DirectionText_FE009F0D02,
        TagDirection.PingPong => UiStrings.S_GifExportDialog_DirectionText_46319C3ED7,
        _ => "",
    };

    /// <summary>범위가 바뀜: 보이는 중이면 다시 합성하고, 아니면 다음에 보일 때.</summary>
    private void RebuildFrames()
    {
        if (!_shown)
        {
            _frames = null;
            return;
        }
        var tag = RangeTag;
        _frames = _project.Frames.Count == 0 ? [] : WorkOverlay.Run(_owner, UiStrings.Busy_GifFrames, progress => GifWriter.ProjectFrames(_project, tag, progress));
        _index = 0;
        RebuildPreview();
    }

    /// <summary>프레임 <paramref name="image"/>가 이 형식으로 쓰일 그림(배율 전): GIF = 알파 문턱·배경 합성, APNG·PNG = 배경색이 있을 때만 합성.</summary>
    private PixelImage Prepared(PixelImage image, Color32? background)
        => _format == ExportFormat.Gif || background is not null ? GifWriter.Prepare(image, background) : image;

    /// <summary>배경·범위·형식이 바뀌면 미리보기 그림(내보낼 색)을 다시 만들고 처음부터 재생.</summary>
    private void RebuildPreview()
    {
        if (!_shown || _frames is null) return;
        var bg = BackgroundColor;
        var bitmaps = new Dictionary<PixelImage, WriteableBitmap>(ReferenceEqualityComparer.Instance);
        _preview.SetFrames(_frames.Select(f =>
        {
            if (!bitmaps.TryGetValue(f.Image, out var bmp)) bitmaps[f.Image] = bmp = ImageUtil.ToBitmap(Prepared(f.Image, bg));
            return bmp;
        }).ToList(), bg);
        Restart();
        UpdateInfo();
    }

    /// <summary>미리보기를 첫 프레임부터 다시 재생.</summary>
    private void Restart()
    {
        _index = 0;
        _preview.Index = 0;
        _timer.Stop();
        if (_shown && _frames is { Count: > 1 })
        {
            _timer.Interval = TimeSpan.FromMilliseconds(FrameMs(_frames[0]));
            _timer.Start();
        }
    }

    /// <summary>
    /// 프레임이 보이는 시간(ms): 고른 속도로 바꾼 길이(최소 <see cref="MinFrameMs"/>). 프레임마다 PNG는 속도가 없으니 원래 길이.
    /// </summary>
    private int FrameMs(GifFrame frame)
        => Math.Max(MinFrameMs, (_format == ExportFormat.Frames ? GifTiming.Original : _timing.Timing).DurationMs(frame.DurationMs));

    private void Advance()
    {
        if (_frames is not { Count: > 0 } frames) return;
        _index = (_index + 1) % frames.Count;
        _preview.Index = _index;
        _timer.Interval = TimeSpan.FromMilliseconds(FrameMs(frames[_index]));
    }

    private void UpdateInfo()
    {
        if (_frames is not { Count: > 0 } frames)
        {
            _info.Text = UiStrings.S_GifExportDialog_UpdateInfo_6C5E244357;
            _canExportChanged(false);
            return;
        }
        _canExportChanged(true);
        int width = _project.Width * Scale, height = _project.Height * Scale;
        switch (_format)
        {
            case ExportFormat.Gif:
            {
                var timing = _timing.Timing;
                int totalCs = GifWriter.DelaysCs(frames.Select(f => timing.DurationMs(f.DurationMs))).Sum();
                var bg = BackgroundColor;
                var colors = new HashSet<uint>();
                foreach (var img in frames.Select(f => f.Image).Distinct(ReferenceEqualityComparer.Instance).Cast<PixelImage>())
                {
                    foreach (var c in GifWriter.Prepare(img, bg).Pixels)
                        if (c.A != 0) colors.Add(c.Argb);
                    if (colors.Count > 256) break;
                }
                var colorText = colors.Count <= 255 ? string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_UpdateInfo_54CA1099E7, colors.Count) : UiStrings.S_GifExportDialog_UpdateInfo_4F67ADEB79;
                _info.Text = string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_UpdateInfo_93DC3D6433, width, height, frames.Count, totalCs / 100.0, colorText)
                             + UiStrings.S_GifExportDialog_UpdateInfo_A51D1C2D3A;
                break;
            }
            case ExportFormat.Apng:
                _info.Text = UiStrings.FormatExport_ApngInfo(width: width, height: height, count: frames.Count, seconds: Seconds(frames.Sum(f => FrameMs(f))));
                break;
            default:
                _info.Text = UiStrings.FormatExport_FramesInfo(width: width, height: height, count: frames.Count, first: FrameFileName(BaseName, 0));
                break;
        }
    }

    /// <summary>초(소수 둘째 자리까지, 지금 언어 형식).</summary>
    private static string Seconds(int ms) => (ms / 1000.0).ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>프레임마다 PNG의 파일 이름: <c>{이름}_{번호:000}.png</c>(번호는 재생 순서로 1부터).</summary>
    private static string FrameFileName(string baseName, int index) => $"{baseName}_{index + 1:000}.png";

    /// <summary>내보내기…: 형식에 맞는 저장 창(프레임마다 PNG는 폴더 고르기)을 띄워 지금 설정으로 쓰고 결과를 보인다.</summary>
    public void Export()
    {
        if (_frames is not { Count: > 0 } frames) return;
        var dir = _project.Path is { } p ? Path.GetDirectoryName(p) : _settings.LastFolder;
        switch (_format)
        {
            case ExportFormat.Gif:
            {
                var title = UiStrings.S_GifExportDialog_ChooseAndExport_AC2A7BE712;
                if (Ui.SaveFile(_owner, title, dir, BaseName + ".gif", ".gif", new FileFilter(title, ["*.gif"])) is { } path) ExportGif(frames, path);
                break;
            }
            case ExportFormat.Apng:
            {
                var title = UiStrings.Export_FormatApng;
                if (Ui.SaveFile(_owner, title, dir, BaseName + ".png", ".png", new FileFilter(title, ["*.png"])) is { } path) ExportApng(frames, path);
                break;
            }
            default:
                if (Ui.PickFolder(_owner, UiStrings.Export_FramesFolder, dir) is { } folder) ExportFrames(frames, folder);
                break;
        }
    }

    private void ExportGif(List<GifFrame> frames, string path)
    {
        var options = new GifOptions { Scale = Scale, PlayCount = PlayCount, Background = BackgroundColor, Timing = _timing.Timing };
        GifInfo? written = null;
        if (!TryWrite(() => written = WorkOverlay.Run(_owner, UiStrings.Busy_GifSaving, progress => GifWriter.Save(frames, path, options, progress))) || written is not { } info) return;
        long bytes = new FileInfo(path).Length;
        var loops = LoopText();
        Exported([path], string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_ExportTo_5A7D0115A3, Path.GetFileName(path), info.Width, info.Height, info.FrameCount, info.TotalMs / 1000.0, loops, FormatSize(bytes)),
            string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_ExportTo_9D8DE615C3, Path.GetFileName(path), FormatSize(bytes), info.Width, info.Height, info.FrameCount, info.TotalMs / 1000.0, loops)
            + (info.ExactPalette ? string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_ExportTo_BB910D7364, info.Colors) : string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_ExportTo_F8D1B6ECFE, info.Colors)));
    }

    private void ExportApng(List<GifFrame> frames, string path)
    {
        var images = FinalImages(frames);
        var pngFrames = frames.Select(f => new PngFrame(images[f.Image], FrameMs(f))).ToList();
        int plays = PlayCount;
        if (!TryWrite(() => WorkOverlay.Run(_owner, UiStrings.Busy_PngSaving, progress =>
            {
                using var stage = progress.Begin(pngFrames.Count);
                PngIO.SaveAnimated(pngFrames, path, plays, stage);
            }))) return;
        long bytes = new FileInfo(path).Length;
        int width = pngFrames[0].Image.Width, height = pngFrames[0].Image.Height, totalMs = pngFrames.Sum(f => f.DurationMs);
        var loops = LoopText();
        Exported([path],
            UiStrings.FormatExport_ApngDone(file: Path.GetFileName(path), width: width, height: height, count: pngFrames.Count, seconds: Seconds(totalMs), loops: loops, size: FormatSize(bytes)),
            string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_ExportTo_9D8DE615C3, Path.GetFileName(path), FormatSize(bytes), width, height, pngFrames.Count, totalMs / 1000.0, loops));
    }

    private void ExportFrames(List<GifFrame> frames, string folder)
    {
        var baseName = BaseName;
        var paths = Enumerable.Range(0, frames.Count).Select(i => Path.Combine(folder, FrameFileName(baseName, i))).ToList();
        var existing = paths.Where(File.Exists).ToList();
        if (existing.Count > 0 && Ui.Ask(_owner, UiStrings.FormatExport_Overwrite(count: existing.Count, first: Path.GetFileName(existing[0])),
                UiStrings.Export_FormatFrames, MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        var images = FinalImages(frames);
        if (!TryWrite(() => WorkOverlay.Run(_owner, UiStrings.Busy_PngSaving, progress =>
            {
                using var stage = progress.Begin(frames.Count);
                for (int i = 0; i < frames.Count; i++)
                {
                    PngIO.Save(images[frames[i].Image], paths[i]);
                    stage.Step(1);
                }
            }))) return;
        long bytes = paths.Sum(p => new FileInfo(p).Length);
        var first = images[frames[0].Image];
        Exported(paths,
            UiStrings.FormatExport_FramesDone(folder: folder, count: paths.Count, width: first.Width, height: first.Height, size: FormatSize(bytes)),
            UiStrings.FormatExport_FramesSaved(folder: folder, first: Path.GetFileName(paths[0]), last: Path.GetFileName(paths[^1]), count: paths.Count, width: first.Width, height: first.Height, size: FormatSize(bytes)));
    }

    /// <summary>프레임 그림마다(같은 그림은 한 번) 쓸 그림: 배경 합성(고른 경우) 후 배율만큼 최근접으로 키움.</summary>
    private Dictionary<PixelImage, PixelImage> FinalImages(List<GifFrame> frames)
    {
        var bg = BackgroundColor;
        int scale = Scale;
        var images = new Dictionary<PixelImage, PixelImage>(ReferenceEqualityComparer.Instance);
        foreach (var f in frames)
        {
            if (images.ContainsKey(f.Image)) continue;
            var img = Prepared(f.Image, bg);
            images[f.Image] = scale == 1 ? img : ImageTransform.ScaleNearest(img, img.Width * scale, img.Height * scale);
        }
        return images;
    }

    private string LoopText()
        => PlayCount == 0 ? UiStrings.S_GifExportDialog_ExportTo_D1F78B3F5D : string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_ExportTo_721F431829, PlayCount);

    /// <summary>파일 쓰기: 실패하면 알리고 false.</summary>
    private bool TryWrite(Action write)
    {
        try
        {
            write();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Ui.Error(_owner, UiStrings.FormatExport_WriteFailed(message: ex.Message), UiStrings.Export_Failed);
            return false;
        }
    }

    /// <summary>내보냄: 결과를 창에 보이고 설정을 기억한 뒤 알린다.</summary>
    private void Exported(IReadOnlyList<string> paths, string message, string result)
    {
        _result.Text = result;
        _result.IsVisible = true;
        SaveSettings();
        _exported(paths, message);
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024):0.##} MB";

    private void SaveSettings()
    {
        _settings.GifPlayCount = PlayCount;
        _settings.GifUseBackground = _bgColor.On();
        _settings.GifBackgroundColor = _color.Color.WithAlpha(255).ToHex();
        _timing.Save(_settings);
    }

    private static RadioButton Radio(string group, string text)
        => new() { GroupName = group, Content = text, Foreground = AppTheme.TextBrush };

    /// <summary>재생 미리보기: 고른 배율(칸보다 크면 정수로 줄임, 그래도 크면 맞춤)로 체커 또는 배경색 위에.</summary>
    private sealed class Preview : Control
    {
        private IReadOnlyList<WriteableBitmap> _frames = [];
        private Color32? _background;
        private int _index, _scale = 1;

        public int Scale
        {
            get => _scale;
            set { _scale = Math.Max(1, value); InvalidateVisual(); }
        }

        public int Index
        {
            get => _index;
            set { _index = value; InvalidateVisual(); }
        }

        public void SetFrames(IReadOnlyList<WriteableBitmap> frames, Color32? background)
        {
            foreach (var old in _frames.Distinct()) if (!frames.Contains(old)) old.Dispose();
            _frames = frames;
            _background = background;
            _index = 0;
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, bounds);
            var area = new Rect(0, 18, bounds.Width, Math.Max(0, bounds.Height - 18));
            if (_frames.Count == 0 || _index >= _frames.Count)
            {
                AppTheme.DrawTextIn(g, UiStrings.S_GifExportDialog_Render_15D5CAFA3B, new Rect(4, 0, bounds.Width - 8, 18), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
                return;
            }
            var bmp = _frames[_index];
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            double s = _scale;
            while (s > 1 && (w * s > area.Width || h * s > area.Height)) s--;
            if (w * s > area.Width || h * s > area.Height) s = Math.Min(area.Width / w, area.Height / h);
            var caption = s == _scale ? string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_Render_2D68770584, _scale) : string.Format(CultureInfo.CurrentCulture, UiStrings.S_GifExportDialog_Render_BABF280D4D, s, _scale);
            AppTheme.DrawTextIn(g, $"{caption} · {_index + 1}/{_frames.Count}", new Rect(4, 0, bounds.Width - 8, 18), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
            var dest = new Rect(area.X + Math.Floor((area.Width - w * s) / 2), area.Y + Math.Floor((area.Height - h * s) / 2), w * s, h * s);
            if (_background is { } bg) g.FillRectangle(new SolidColorBrush(bg.ToColor()), dest);
            else AppTheme.DrawChecker(g, dest, 8);
            using (ImageUtil.Pixelated(g))
                g.DrawImage(bmp, new Rect(0, 0, w, h), dest);
            g.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), dest.Inflate(0.5));
        }
    }
}
