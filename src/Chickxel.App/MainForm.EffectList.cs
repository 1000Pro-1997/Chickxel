using Chickxel.App.Localization;

namespace Chickxel.App;

// 효과 목록 패널: 도구 막대의 효과 단추를 누르면 맨 왼쪽 칸(파일 브라우저 자리)에 효과 목록(EffectListPanel)이 열린다.
// 파일 브라우저와 함께 켜면 위(파일)·아래(효과)로 나누고 사이 막대로 높이를 조절한다. 효과를 타임라인 칸으로 끌어 놓으면 효과 창 없이 넣는다.
public sealed partial class MainForm
{
    private EffectListPanel _effectList = null!;
    private ToggleButton _tbEffects = null!;
    private bool _effectListShown;
    /// <summary>맨 왼쪽 칸: 파일 브라우저(위) · 나눔 막대 · 효과 목록(아래).</summary>
    private Grid _leftDock = null!;
    private GridSplitter _leftDockSplitter = null!;
    /// <summary>둘 다 보일 때의 위·아래 높이(하나를 숨겼다 다시 켜면 되살림).</summary>
    private (GridLength Files, GridLength Effects) _leftDockRows = (new(1, GridUnitType.Star), new(1, GridUnitType.Star));

    /// <summary>도구 막대 위쪽 패널 단추 줄의 효과 목록 단추(파일 브라우저 단추 아래).</summary>
    private ToggleButton BuildEffectListButton()
    {
        _tbEffects = ToolBarButton(Icons.Effects, UiStrings.EffectList_ToolbarTip);
        _tbEffects.Click += (_, _) => SetLeftPanes(_filesShown, !_effectListShown, animate: true);
        return _tbEffects;
    }

    /// <summary>맨 왼쪽 칸(파일 브라우저·효과 목록을 위아래로 담음). 보이는 것은 <see cref="ArrangeLeftDock"/>가 정한다.</summary>
    private Grid BuildLeftDock()
    {
        _effectList = new EffectListPanel(_timeline) { IsVisible = false };
        _leftDockSplitter = Splitter(GridResizeDirection.Rows);
        _leftDockSplitter.IsVisible = false;
        _leftDock = new Grid { Background = AppTheme.PanelBrush };
        _leftDock.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star) { MinHeight = MinPanelSize });
        _leftDock.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _leftDock.RowDefinitions.Add(new RowDefinition(0, GridUnitType.Pixel));
        Grid.SetRow(_browser, 0);
        Grid.SetRow(_leftDockSplitter, 1);
        Grid.SetRow(_effectList, 2);
        _leftDock.Children.Add(_browser);
        _leftDock.Children.Add(_leftDockSplitter);
        _leftDock.Children.Add(_effectList);
        return _leftDock;
    }

    /// <summary>위아래 나눔을 바꾸는 움직임(움직이는 중이 아니면 null).</summary>
    private DispatcherTimer? _leftDockAnim;

    /// <summary>
    /// 맨 왼쪽 칸 안: 켠 것만 보이고, 둘 다면 위아래로 나눈다(나눈 높이는 기억해 다시 켤 때 되살림). 하나뿐이면 그것이 칸을 채운다.
    /// <paramref name="animate"/>면(칸이 이미 열려 있을 때) 파일 칸 아래 끝이 새 자리까지 부드럽게 움직인다(약 0.18초, 칸 폭 접기와 같은 빠르기 곡선) —
    /// 도중에 다시 누르면 지금 자리에서 새 자리로 간다.
    /// </summary>
    private void ArrangeLeftDock(bool files, bool effects, bool animate = false)
    {
        var rows = _leftDock.RowDefinitions;
        if (_leftDockAnim is null && _browser.IsVisible && _effectList.IsVisible) _leftDockRows = (rows[0].Height, rows[2].Height);
        bool moving = _leftDockAnim is not null;
        _leftDockAnim?.Stop();
        _leftDockAnim = null;
        double total = _leftDock.Bounds.Height;
        bool same = !moving && _browser.IsVisible == files && _effectList.IsVisible == effects;
        if (!animate || same || total <= 0)
        {
            Settle();
            return;
        }
        // 파일 칸 높이(px)만 움직이고 효과 칸은 나머지를 채운다. 움직이는 동안은 둘 다 보이고 최소 높이를 풀어 0까지 줄 수 있게 한다.
        double from = _browser.IsVisible ? rows[0].ActualHeight : 0;
        double share = _leftDockRows.Files.Value + _leftDockRows.Effects.Value is var sum && sum > 0 ? _leftDockRows.Files.Value / sum : 0.5;
        double target = !files ? 0 : !effects ? total : (total - SplitterSize) * share;
        _browser.IsVisible = _effectList.IsVisible = _leftDockSplitter.IsVisible = true;
        _browser.ClipToBounds = _effectList.ClipToBounds = true;
        rows[0].MinHeight = rows[2].MinHeight = 0;
        rows[0].Height = new GridLength(from);
        rows[2].Height = new GridLength(1, GridUnitType.Star);
        var start = DateTime.UtcNow;
        const double DurationMs = 180;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / DurationMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            rows[0].Height = new GridLength(Math.Max(0, from + (target - from) * eased));
            if (t < 1) return;
            timer.Stop();
            if (ReferenceEquals(_leftDockAnim, timer)) _leftDockAnim = null;
            Settle();
        };
        _leftDockAnim = timer;
        timer.Start();

        // 끝난 자리: 켠 것만 보이고 높이는 별 비율(둘 다면 기억한 나눔)
        void Settle()
        {
            _browser.IsVisible = files;
            _effectList.IsVisible = effects;
            _leftDockSplitter.IsVisible = files && effects;
            var fill = new GridLength(1, GridUnitType.Star);
            rows[0].Height = files ? (effects ? _leftDockRows.Files : fill) : new GridLength(0);
            rows[2].Height = effects ? (files ? _leftDockRows.Effects : fill) : new GridLength(0);
            rows[0].MinHeight = files ? MinPanelSize : 0;
            rows[2].MinHeight = effects ? MinPanelSize : 0;
        }
    }
}
