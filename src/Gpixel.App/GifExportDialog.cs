using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// GIF 애니메이션으로 내보내기: 범위(현재 태그 / 모든 프레임 / 고른 태그 — 태그 방향(역방향·왕복) 그대로), 배율(1~10×), 반복(무한 / N번),
/// 배경(투명 / 색)을 고르고, 그 배율로 실제 프레임 길이대로 재생되는 미리보기를 본다. 그림은 프레임 시트와 똑같이 합성한다
/// (<see cref="GifWriter.ProjectFrames"/>: 레이어 효과·지금 착장, 숨긴·내보내기에서 뺀 레이어 제외). 프로젝트는 바꾸지 않는다.
/// 반복·배경은 앱 설정(<see cref="AppSettings"/>)에 기억한다. 내보낸 뒤에도 창은 열려 있고 결과(크기·용량)를 보인다.
/// </summary>
public sealed class GifExportDialog : DialogBase
{
    private const string Label = "GIF 애니메이션으로 내보내기";
    private const int MaxScale = 10;
    private const double PreviewBox = 384;

    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    private readonly AppSettings _settings;
    private readonly AnimationTag? _currentTag;
    private readonly RadioButton _rangeCurrent, _rangeAll, _rangeTag, _loopForever, _loopCount, _bgTransparent, _bgColor;
    private readonly ComboBox _tagCombo;
    private readonly NumericUpDown _scale, _plays;
    private readonly ColorField _color;
    private readonly Preview _preview;
    private readonly TextBlock _info, _result;
    private readonly DispatcherTimer _timer = new();
    private List<GifFrame> _frames = [];
    private int _index;

    /// <summary>마지막으로 내보낸 파일(없으면 null).</summary>
    public string? ExportedPath { get; private set; }
    /// <summary>마지막 내보내기 결과 문구(상태 표시줄용).</summary>
    public string? ResultMessage { get; private set; }

    public GifExportDialog(EditorContext ctx, AppSettings settings) : base(Label, "내보내기…")
    {
        _ctx = ctx;
        _project = ctx.Project ?? throw new InvalidOperationException("프로젝트가 없습니다.");
        _settings = settings;
        _currentTag = _project.TagOfFrame(ctx.CurrentFrame);

        _rangeCurrent = Radio("range", _currentTag is { } ct ? $"현재 태그 \"{ct.Name}\"{DirectionText(ct)}" : "현재 태그(현재 프레임이 태그 안에 없음)");
        _rangeCurrent.IsEnabled = _currentTag is not null;
        _rangeAll = Radio("range", $"모든 프레임({_project.Frames.Count}개)");
        _rangeTag = Radio("range", "태그 고르기");
        _tagCombo = Ui.Combo(165, _project.Tags.Select(t => $"{t.Name}  [{t.From}–{t.To}]{DirectionText(t)}").ToArray());
        _rangeTag.IsEnabled = _tagCombo.IsEnabled = _project.Tags.Count > 0;
        if (_currentTag is not null) _tagCombo.SelectedIndex = _project.Tags.ToList().IndexOf(_currentTag);
        (_currentTag is not null ? _rangeCurrent : _rangeAll).IsChecked = true;

        int side = Math.Max(_project.Width, _project.Height);
        _scale = Ui.Num(1, MaxScale, Math.Clamp((int)Math.Round(256.0 / side), 1, MaxScale), width: 60)
            .Presets(1, 2, 3, 4, 5, 6, 8, 10).Unit("×").RecentKey("gif.scale");
        _loopForever = Radio("loop", "무한 반복");
        _loopCount = Radio("loop", "재생 횟수");
        _plays = Ui.Num(1, 100, Math.Max(1, settings.GifPlayCount), width: 60).Presets(1, 2, 3, 4, 5, 10, 20).Unit("번").RecentKey("gif.plays");
        (settings.GifPlayCount <= 0 ? _loopForever : _loopCount).IsChecked = true;

        _bgTransparent = Radio("bg", "투명(알파 50% 미만은 투명, 나머지는 불투명)");
        _bgColor = Radio("bg", "배경색 위에 합성");
        _color = new ColorField(Color32.TryParseHex(settings.GifBackgroundColor, out var bg) ? bg.WithAlpha(255) : Color32.White);
        (settings.GifUseBackground ? _bgColor : _bgTransparent).IsChecked = true;

        _preview = new Preview();
        _info = new TextBlock { Foreground = AppTheme.TextBrush, Margin = new Thickness(2, 4, 2, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
        _result = new TextBlock { Foreground = AppTheme.AccentBrush, Margin = new Thickness(2, 2, 2, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 400, IsVisible = false };

        var settingsColumn = new StackPanel
        {
            Spacing = 2,
            Width = 300,
            Children =
            {
                Ui.Group("범위", Ui.Stack(_rangeCurrent, _rangeAll, Ui.Row(_rangeTag, _tagCombo))),
                Ui.Group("배율", Ui.Row(_scale, Ui.Label("×  (최근접 — 픽셀 그대로 키움)"))),
                Ui.Group("반복", Ui.Stack(_loopForever, Ui.Row(_loopCount, _plays, Ui.Label("번")))),
                Ui.Group("배경", Ui.Stack(_bgTransparent, Ui.Row(_bgColor, _color))),
            },
        };
        var previewColumn = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 0, 0), Children = { _preview, _info, _result } };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { settingsColumn, previewColumn } });

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

        _timer.Tick += (_, _) => Advance();
        OkButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.None; // 내보낸 뒤에도 창을 열어 둔다(결과 보기)
            Dispatcher.UIThread.Post(ChooseAndExport);
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            SaveSettings();
        };

        _preview.Scale = Scale;
        RebuildFrames();
    }

    /// <summary>창을 띄운다. 내보냈으면 상태 표시줄 문구와 파일 경로, 아니면 null.</summary>
    public static (string Message, string Path)? Run(EditorContext ctx, AppSettings settings, Visual? owner)
    {
        if (!ctx.HasProject) return null;
        var dlg = new GifExportDialog(ctx, settings);
        dlg.ShowDialog(owner);
        return dlg.ExportedPath is { } path ? (dlg.ResultMessage!, path) : null;
    }

    public int Scale => (int)_scale.NumValue();
    public int PlayCount => _loopForever.On() ? 0 : (int)_plays.NumValue();
    public Color32? BackgroundColor => _bgColor.On() ? _color.Color.WithAlpha(255) : null;

    /// <summary>고른 범위의 태그(모든 프레임이면 null).</summary>
    public AnimationTag? RangeTag
        => _rangeCurrent.On() ? _currentTag
            : _rangeTag.On() && _tagCombo.SelectedIndex >= 0 && _tagCombo.SelectedIndex < _project.Tags.Count ? _project.Tags[_tagCombo.SelectedIndex]
            : null;

    /// <summary>기본 파일 이름: 태그면 <c>{프로젝트}_{태그}.gif</c>, 모든 프레임이면 <c>{프로젝트}.gif</c>.</summary>
    public string SuggestedFileName
    {
        get
        {
            var name = _project.Path is { } p ? Path.GetFileNameWithoutExtension(p) : ProjectExport.CleanFileName(_project.DisplayName);
            return (RangeTag is { } t ? $"{name}_{ProjectExport.CleanFileName(t.Name)}" : name) + ".gif";
        }
    }

    private static string DirectionText(AnimationTag t) => t.Direction switch
    {
        TagDirection.Reverse => " · 역방향",
        TagDirection.PingPong => " · 왕복",
        _ => "",
    };

    private void RebuildFrames()
    {
        _frames = _project.Frames.Count == 0 ? [] : GifWriter.ProjectFrames(_project, RangeTag);
        _index = 0;
        RebuildPreview();
    }

    /// <summary>배경·범위가 바뀌면 미리보기 그림(GIF에 쓰일 색)을 다시 만들고 처음부터 재생.</summary>
    private void RebuildPreview()
    {
        var bg = BackgroundColor;
        var bitmaps = new Dictionary<PixelImage, WriteableBitmap>(ReferenceEqualityComparer.Instance);
        _preview.SetFrames(_frames.Select(f =>
        {
            if (!bitmaps.TryGetValue(f.Image, out var bmp)) bitmaps[f.Image] = bmp = ImageUtil.ToBitmap(GifWriter.Prepare(f.Image, bg));
            return bmp;
        }).ToList(), bg);
        _index = 0;
        _preview.Index = 0;
        _timer.Stop();
        if (_frames.Count > 1)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].DurationMs);
            _timer.Start();
        }
        UpdateInfo();
    }

    private void Advance()
    {
        if (_frames.Count == 0) return;
        _index = (_index + 1) % _frames.Count;
        _preview.Index = _index;
        _timer.Interval = TimeSpan.FromMilliseconds(_frames[_index].DurationMs);
    }

    private void UpdateInfo()
    {
        if (_frames.Count == 0)
        {
            _info.Text = "내보낼 프레임이 없습니다.";
            OkButton.IsEnabled = false;
            return;
        }
        OkButton.IsEnabled = true;
        int totalCs = GifWriter.DelaysCs(_frames.Select(f => f.DurationMs)).Sum();
        var bg = BackgroundColor;
        var colors = new HashSet<uint>();
        foreach (var img in _frames.Select(f => f.Image).Distinct(ReferenceEqualityComparer.Instance).Cast<PixelImage>())
        {
            foreach (var c in GifWriter.Prepare(img, bg).Pixels)
                if (c.A != 0) colors.Add(c.Argb);
            if (colors.Count > 256) break;
        }
        var colorText = colors.Count <= 255 ? $"색 {colors.Count}개(무손실)" : "색이 많아 프레임마다 256색 이하로 줄임";
        _info.Text = $"결과: {_project.Width * Scale}×{_project.Height * Scale}px · 프레임 {_frames.Count}개 · {totalCs / 100.0:0.##}초 · {colorText}\n"
                     + "프레임 시트와 같은 합성(레이어 효과·지금 착장, 숨긴·내보내기에서 뺀 레이어 제외). GIF 지연은 1/100초 단위(최소 0.02초)입니다.";
    }

    private void ChooseAndExport()
    {
        var dir = _project.Path is { } p ? Path.GetDirectoryName(p) : _settings.LastFolder;
        var path = Ui.SaveFile(this, Label, dir, SuggestedFileName, ".gif", new FileFilter("GIF 애니메이션", ["*.gif"]));
        if (path is null) return;
        ExportTo(path);
    }

    /// <summary>지금 설정으로 <paramref name="path"/>에 쓰고 결과를 창에 보인다. 실패하면 알리고 null.</summary>
    public GifInfo? ExportTo(string path)
    {
        try
        {
            var info = GifWriter.Save(_frames, path, new GifOptions { Scale = Scale, PlayCount = PlayCount, Background = BackgroundColor });
            long bytes = new FileInfo(path).Length;
            ExportedPath = path;
            var loops = PlayCount == 0 ? "무한 반복" : $"{PlayCount}번 재생";
            ResultMessage = $"GIF 내보내기: {Path.GetFileName(path)} ({info.Width}×{info.Height}, 프레임 {info.FrameCount}개, {info.TotalMs / 1000.0:0.##}초, {loops}, {FormatSize(bytes)})";
            _result.Text = $"저장함: {Path.GetFileName(path)}\n{FormatSize(bytes)} · {info.Width}×{info.Height}px · 프레임 {info.FrameCount}개 · {info.TotalMs / 1000.0:0.##}초 · {loops}"
                           + (info.ExactPalette ? $" · 색 {info.Colors}개(무손실)" : $" · 프레임마다 최대 {info.Colors}색");
            _result.IsVisible = true;
            CancelButtonControl.Content = "닫기";
            SaveSettings();
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Ui.Error(this, $"GIF를 쓰지 못했습니다.\n{ex.Message}", "GIF 내보내기 실패");
            return null;
        }
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024):0.##} MB";

    private void SaveSettings()
    {
        _settings.GifPlayCount = PlayCount;
        _settings.GifUseBackground = _bgColor.On();
        _settings.GifBackgroundColor = _color.Color.WithAlpha(255).ToHex();
    }

    private static RadioButton Radio(string group, string text)
        => new() { GroupName = group, Content = text, Foreground = AppTheme.TextBrush };

    /// <summary>재생 미리보기: 고른 배율(칸보다 크면 정수로 줄임, 그래도 크면 맞춤)로 체커 또는 배경색 위에.</summary>
    private sealed class Preview : Control
    {
        private IReadOnlyList<WriteableBitmap> _frames = [];
        private Color32? _background;
        private int _index, _scale = 1;

        public Preview()
        {
            Width = PreviewBox;
            Height = PreviewBox + 18;
        }

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
            var area = new Rect(0, 18, bounds.Width, bounds.Height - 18);
            if (_frames.Count == 0 || _index >= _frames.Count)
            {
                AppTheme.DrawTextIn(g, "미리보기", new Rect(4, 0, bounds.Width - 8, 18), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
                return;
            }
            var bmp = _frames[_index];
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            double s = _scale;
            while (s > 1 && (w * s > area.Width || h * s > area.Height)) s--;
            if (w * s > area.Width || h * s > area.Height) s = Math.Min(area.Width / w, area.Height / h);
            var caption = s == _scale ? $"미리보기 — {_scale}× (실제 크기)" : $"미리보기 — {s:0.##}×로 줄여 보임(내보내기 {_scale}×)";
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
