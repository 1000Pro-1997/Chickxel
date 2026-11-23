using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 효과 창 왼쪽 끝의 효과 종류 칸: 종류마다 미리보기 그림(효과가 정한 미리보기 병아리에 기본 설정으로 입힌 모습 — 움직이는 효과는 돌려 보임, <see cref="EffectTile"/>) + 이름 상자.
/// 두 번 누르거나 효과 목록으로 끌어 놓으면 "추가 범위"로 추가한다(끌어 놓으면 놓은 자리에).
/// 상자 차례·풍선·우클릭 메뉴는 효과 목록 패널과 같이 쓰는 <see cref="EffectTileCatalog"/>가 정한다(효과 탭 = 하나씩 넣는 효과, 프리셋 탭 = 효과 레시피).
/// 프리셋 저장·이름 바꾸기·삭제는 개인 에셋 저장소에 한다.
/// 레시피를 넣으면 효과 값과 스크립트 원문을 복사하므로 나중에 레시피를 지우거나 꺼도 이미 넣은 효과는 바뀌지 않는다.
/// 스크립트가 든 상자는 모서리에 코드 표시를 그리고, 실행을 허용하지 않은 스크립트가 있으면 풍선에 알린다(넣어도 허용 전에는 그리지 않음).
/// </summary>
public sealed partial class LayerEffectsDialog
{
    private const int TileWidth = 88, TileHeight = 110, ThumbSteps = AssetPreview.Steps, ThumbHold = 4;
    /// <summary>효과 종류 칸의 한 줄 상자 수.</summary>
    private const int PaletteColumns = 4;

    private readonly List<EffectTile> _tiles = new();
    /// <summary>효과 탭 격자: 공식 스크립트, 고를 수 있는 C# 효과, 패키지·개인 스크립트 상자(저장소가 바뀌면 모두 다시 만듦).</summary>
    private readonly WrapPanel _effectsWrap = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _presetWrap = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _presetEmpty = Ui.Label("");
    /// <summary>효과 탭 위의 개인 에셋 읽기 오류(읽었으면 숨김).</summary>
    private readonly TextBlock _effectsError = Ui.Label("");
    private readonly DispatcherTimer _thumbTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _thumbTick;
    private EffectTile? _pressedTile;
    private Avalonia.Point _pressPoint;
    private bool _tileDragging;
    private Button? _savePreset;
    private Action? _onAssetsChanged;
    /// <summary>움직이는 효과 상자 풍선에 덧붙이는 추가 범위 안내(효과 창이 정함).</summary>
    private string? _motionTip;

    private Control BuildPalette(string? motionTip)
    {
        _motionTip = motionTip;

        var store = Ui.Button(VectorIcon.Store, UiStrings.EffectPreset_OpenStore,
            (_, _) => AssetManagerWindow.Open(this, AssetKinds.EffectScript, tab: AssetManagerTab.Store));
        store.HorizontalAlignment = HorizontalAlignment.Stretch;
        store.Margin = new Thickness(2, 6, 2, 0);
        ToolTip.SetTip(store, UiStrings.EffectPreset_OpenStoreTip);
        store.HelpAt("screen-asset-manager", anchor: "Open Store at the bottom of the Effects tab");
        _savePreset = Ui.Button(VectorIcon.Layers, UiStrings.EffectPreset_SavePreset, (_, _) => SavePresetGroup())
            .HelpAt("screen-effect-recipes", anchor: "① Save Current Effects as Preset");
        _savePreset.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(_savePreset, UiStrings.EffectPreset_SaveGroupTip);
        var presetsHelp = Ui.Label(UiStrings.EffectPreset_PresetsHelp);
        foreach (var label in new[] { presetsHelp, _presetEmpty, _effectsError })
        {
            label.TextWrapping = TextWrapping.Wrap;
            label.MaxWidth = (TileWidth + 4) * PaletteColumns;
        }
        _effectsError.Foreground = AppTheme.WarningBrush;
        ReloadAssetTiles();
        // 에셋 관리자에서 켜고 끄거나 지우면 에셋 상자만 다시 만든다(이미 넣은 효과는 값 사본이라 그대로).
        _onAssetsChanged = () => Dispatcher.UIThread.Post(ReloadAssetTiles);
        AppAssets.Changed += _onAssetsChanged;

        _thumbTimer.Tick += (_, _) =>
        {
            // 움직이는 효과: 0 → 끝까지 돌리고 끝 모습을 잠깐 보여 준 뒤 처음부터.
            _thumbTick = (_thumbTick + 1) % (ThumbSteps + ThumbHold);
            int step = Math.Min(_thumbTick, ThumbSteps - 1);
            foreach (var tile in _tiles)
                if (tile.Animated) tile.Show(step);
        };
        _thumbTimer.Start();

        int width = (TileWidth + 4) * PaletteColumns + 40; // + 탭 안쪽 여백과 세로 스크롤 막대
        // 스크롤 칸은 높이를 정하지 않고 탭 안 남은 자리를 채운다 — 정한 높이가 탭 내용 칸보다 크면 맨 아래 상자가 잘려 스크롤로도 닿지 않았다.
        ScrollViewer Scroll(Control content) => new()
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        // 효과 탭: 맨 아래에 상점 단추를 고정하고 격자는 남은 자리에서 스크롤한다(효과를 만드는 일은 효과 만들기 창에서).
        DockPanel.SetDock(_effectsError, Dock.Top);
        DockPanel.SetDock(store, Dock.Bottom);
        var effects = new DockPanel { Children = { _effectsError, store, Scroll(_effectsWrap) } }.HelpAt("screen-effect-list");
        var presetHead = Ui.Stack(_savePreset, presetsHelp, _presetEmpty);
        presetHead.Margin = new Thickness(0);
        DockPanel.SetDock(presetHead, Dock.Top);
        var presets = new DockPanel { Children = { presetHead, Scroll(_presetWrap) } }.HelpAt("screen-effect-recipes", anchor: "Presets tab");
        // 대상 도움말(F1): 탭 머리 = 효과 창 그림 ⑥·⑦ 줄, 탭 내용 = 기본 효과 목록·효과 프리셋 쪽(상자마다 자기 자리 — AddPaletteTile)
        var tabs = new TabControl { Width = width, Height = 590 };
        tabs.Items.Add(new TabItem { Header = UiStrings.EffectPreset_EffectsTab, Content = effects }.HelpAt(HelpPage, anchor: "⑥ Effects tab"));
        tabs.Items.Add(new TabItem { Header = UiStrings.EffectPreset_PresetsTab, Content = presets }.HelpAt(HelpPage, anchor: "⑦ Presets tab"));
        return Ui.Stack(Ui.Row(Ui.Label(UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_F3EEB77504, bold: true), Ui.InfoBadge(UiStrings.EffectPreset_PaletteHelp))
            .HelpAt(HelpPage, anchor: "⑥ Effects tab"), tabs);
    }

    private void AddPaletteTile(EffectTile tile, Panel panel)
    {
        tile.Width = TileWidth;
        tile.Height = TileHeight;
        tile.Margin = new Thickness(2);
        tile.Show(0);
        AttachTileInput(tile);
        if (tile.Entry is { } entry)
            PopupMenus.Attach(tile, () => EffectTileCatalog.Menu(this, entry));
        _tiles.Add(tile);
        panel.Children.Add(tile);
    }

    /// <summary>
    /// 효과 탭과 프리셋 탭 상자를 다시 만든다. 효과 탭: 공식 효과 스크립트(예전 기본 효과 차례), 고를 수 있는 C# 효과,
    /// 받은 스크립트, 내 스크립트. 프리셋 탭: 효과 레시피. 에셋 라이브러리를 읽지 못했으면 두 탭에 그 까닭을 보인다.
    /// </summary>
    private void ReloadAssetTiles()
    {
        foreach (var tile in _tiles) tile.Dispose();
        _tiles.Clear();
        _effectsWrap.Children.Clear();
        _presetWrap.Children.Clear();
        var (effects, presets) = EffectTileCatalog.Create(_motionTip);
        foreach (var tile in effects) AddPaletteTile(tile, _effectsWrap);
        foreach (var tile in presets) AddPaletteTile(tile, _presetWrap);
        string? error = EffectTileCatalog.LoadError;
        _effectsError.Text = error ?? "";
        _effectsError.IsVisible = error is not null;
        _presetEmpty.Text = error ?? UiStrings.EffectPreset_Empty;
        _presetEmpty.IsVisible = presets.Count == 0 || error is not null;
    }

    private void SavePresetGroup()
    {
        if (_effects.Count == 0) return;
        var personal = EffectTileCatalog.MyRecipes.ToList();
        if (RecipeSaveDialog.Ask(this, _effects[0].DisplayName, PreviewChicks.Default, personal.Select(entry => entry.Name)) is not { } saved) return;
        var (name, chick) = saved;

        var duplicate = personal.FirstOrDefault(entry => entry.Name == name);
        if (duplicate is not null && Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.EffectPreset_OverwriteQuestion, name), UiStrings.EffectPreset_SaveTitle,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        var recipe = EffectRecipe.Capture(duplicate?.Id ?? "", name, _effects, _project) with { PreviewChick = chick };
        EffectTileCatalog.Persist(this, () => AppAssets.Store.SaveRecipe(recipe));
    }

    // ───────────── 두 번 누르기·끌어 놓기 ─────────────

    private void AttachTileInput(EffectTile tile)
    {
        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
            // 오른쪽 위 물음표: 넣지 않고 설명만 띄운다(두 번째 누르기도 넣기로 넘기지 않음)
            if (tile.HelpHit(e.GetPosition(tile)))
            {
                e.Handled = true;
                EffectTileCatalog.ShowHelp(tile);
                return;
            }
            if (e.ClickCount == 2)
            {
                Add(tile.Effects, _effects.Count, tile.Template);
                return;
            }
            _pressedTile = tile;
            _pressPoint = e.GetPosition(this);
            _tileDragging = false;
            e.Pointer.Capture(tile);
        };
        tile.PointerMoved += (_, e) =>
        {
            if (_pressedTile != tile) return;
            if (!_tileDragging && ReorderCore.Exceeded(_pressPoint, e.GetPosition(this))) _tileDragging = true;
            if (!_tileDragging) return;
            bool over = DropIndex(e) is not null;
            tile.Cursor = new Cursor(over ? StandardCursorType.DragCopy : StandardCursorType.No);
            ShowDropTarget(over);
        };
        tile.PointerReleased += (_, e) =>
        {
            if (_pressedTile != tile) return;
            int? at = _tileDragging ? DropIndex(e) : null;
            EndTileDrag();
            e.Pointer.Capture(null);
            if (at is { } i) Add(tile.Effects, i, tile.Template);
        };
        tile.PointerCaptureLost += (_, _) =>
        {
            if (_pressedTile == tile) EndTileDrag();
        };
    }

    private void EndTileDrag()
    {
        if (_pressedTile is { } tile) tile.Cursor = new Cursor(StandardCursorType.Hand);
        _pressedTile = null;
        _tileDragging = false;
        ShowDropTarget(false);
    }

    /// <summary>포인터가 효과 목록 위면 넣을 효과 번호(그 위치보다 위에 있는 줄 다음 줄의 효과 — 접힌 묶음 앞뒤), 아니면 null.</summary>
    private int? DropIndex(PointerEventArgs e)
    {
        var p = e.GetPosition(_list);
        if (!new Rect(_list.Bounds.Size).Contains(p)) return null;
        int row = 0;
        foreach (var item in _list.Items.OfType<ListBoxItem>())
            if (item.TranslatePoint(new Avalonia.Point(0, item.Bounds.Height / 2), _list) is { } mid && mid.Y < p.Y) row++;
        return EffectIndexAtRow(row);
    }

    private void ShowDropTarget(bool on)
    {
        if (on)
        {
            _list.BorderBrush = AppTheme.AccentBrush;
            _list.BorderThickness = new Thickness(2);
        }
        else
        {
            _list.ClearValue(BorderBrushProperty);
            _list.ClearValue(BorderThicknessProperty);
        }
    }

    private void DisposePalette()
    {
        _thumbTimer.Stop();
        if (_onAssetsChanged is { } changed) AppAssets.Changed -= changed;
        foreach (var tile in _tiles) tile.Dispose();
    }
}
