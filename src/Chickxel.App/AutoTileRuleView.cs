using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

public sealed partial class AutoTileEditorDialog
{
    /// <summary>
    /// 규칙 한 줄: 번호, 둘레 조건 3×3(<see cref="ConditionGrid"/>), 넣은 타일(<see cref="RuleTilesView"/>), 뒤집기·회전 허용 토글, 위/아래·복제·지우기.
    /// 안쪽 어디를 눌러도 이 규칙이 선택된다(파란 테두리). 미리보기 판에서 쓰인 규칙이면 주황 테두리.
    /// </summary>
    private sealed class RuleCard : Border
    {
        private static readonly IBrush IdleBorder = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

        private readonly AutoTileEditorDialog _owner;
        private readonly TextBlock _number;
        private readonly ConditionGrid _grid;
        private readonly RuleTilesView _tiles;
        private readonly ToggleButton _flipX, _flipY, _rotate;
        private readonly TextBox _targetGroup;
        private readonly Control _targetRow;
        private bool _updating;

        public int Index { get; }

        public RuleCard(AutoTileEditorDialog owner, int index)
        {
            _owner = owner;
            Index = index;
            BorderThickness = new Thickness(2);
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(4);
            _number = new TextBlock
            {
                Text = (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture),
                Width = 24,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.Bold,
            };
            _grid = new ConditionGrid(owner, index) { VerticalAlignment = VerticalAlignment.Center };
            _grid.Tip(UiStrings.AutoTile_ConditionTip);
            _tiles = new RuleTilesView(owner, index) { Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
            _tiles.Tip(UiStrings.AutoTile_RuleTilesTip);
            _flipX = Toggle(VectorIcon.FlipLeftRight, UiStrings.AutoTile_FlipXTip);
            _flipY = Toggle(VectorIcon.FlipUpDown, UiStrings.AutoTile_FlipYTip);
            bool canRotate = owner._ts.CanRotate;
            _rotate = Toggle(VectorIcon.RotateCw, canRotate ? UiStrings.AutoTile_RotateTip : UiStrings.AutoTile_RotateNeedsSquare);
            _rotate.IsEnabled = canRotate;
            ToolTip.SetShowOnDisabled(_rotate, true);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    Ui.IconButton(VectorIcon.ArrowUp, UiStrings.AutoTile_MoveUp, (_, _) => owner.MoveRule(Index, -1)),
                    Ui.IconButton(VectorIcon.ArrowDown, UiStrings.AutoTile_MoveDown, (_, _) => owner.MoveRule(Index, 1)),
                    Ui.IconButton(VectorIcon.Copy, UiStrings.AutoTile_DuplicateRule, (_, _) => owner.DuplicateRule(Index)),
                    Ui.IconButton(VectorIcon.Trash, UiStrings.AutoTile_DeleteRule, (_, _) => owner.DeleteRule(Index)),
                },
            };
            ((Button)actions.Children[0]).IsEnabled = index > 0;
            ((Button)actions.Children[1]).IsEnabled = index < owner._rules.Count - 1;
            _targetGroup = Ui.Text("", 90).Tip(UiStrings.AutoTile_TargetGroupTip);
            _targetGroup.LostFocus += (_, _) => ApplyTargetGroup();
            _targetGroup.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyTargetGroup(); };
            _targetRow = Ui.Row(Ui.Label(UiStrings.AutoTile_TargetGroup), _targetGroup);
            var side = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { _flipX, _flipY, _rotate } }, actions, _targetRow },
            };
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _number, _grid, _tiles, side } };
            Refresh();
            // 단추·토글을 눌러도 먼저 이 규칙을 선택한다.
            AddHandler(PointerPressedEvent, (_, _) => owner.Select(Index), RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void ApplyTargetGroup()
        {
            var rule = _owner._rules[Index];
            var name = _targetGroup.Text?.Trim() ?? "";
            if (name != rule.TargetGroup) _owner.SetRule(Index, rule with { TargetGroup = name });
        }

        private ToggleButton Toggle(VectorIcon icon, string tip)
        {
            var iconView = Ui.Icon(icon, 14);
            iconView.Margin = new Thickness(0, 1);
            var t = new ToggleButton
            {
                Content = iconView,
                Margin = new Thickness(2, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            }.Tip(tip);
            t.IsCheckedChanged += (_, _) =>
            {
                if (_updating) return;
                var rule = _owner._rules[Index];
                _owner.SetRule(Index, rule with { AllowFlipX = _flipX.On(), AllowFlipY = _flipY.On(), AllowRotate = _rotate.On() && _owner._ts.CanRotate });
            };
            return t;
        }

        /// <summary>규칙 값이 바뀜: 토글과 그림을 맞춘다.</summary>
        public void Refresh()
        {
            var rule = _owner._rules[Index];
            _updating = true;
            try
            {
                _flipX.IsChecked = rule.AllowFlipX;
                _flipY.IsChecked = rule.AllowFlipY;
                _rotate.IsChecked = rule.AllowRotate && _owner._ts.CanRotate;
                // 무리 이름은 "정한 무리 있음" 조건을 쓸 때만
                _targetRow.IsVisible = rule.Target != 0;
                if (!_targetGroup.IsFocused) _targetGroup.Text = rule.TargetGroup;
            }
            finally
            {
                _updating = false;
            }
            _grid.InvalidateVisual();
            _tiles.InvalidateMeasure();
            _tiles.InvalidateVisual();
        }

        /// <summary>선택(파란 테두리·밝은 바탕)과 미리보기에서 쓰인 규칙(주황 테두리) 표시.</summary>
        public void UpdateState(bool selected, bool hovered)
        {
            BorderBrush = hovered ? AppTheme.WarningBrush : selected ? AppTheme.AccentBrush : IdleBorder;
            Background = selected ? AppTheme.HoverBrush : AppTheme.PanelBrush;
            _number.Foreground = selected ? AppTheme.AccentBrush : hovered ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
        }
    }

    /// <summary>
    /// 규칙의 둘레 조건 3×3: 가운데 = 이 칸(넣은 첫 타일), 둘레 8칸 = 상관없음(빈 칸)·있어야 함(초록 더하기)·없어야 함(빨강 빼기)·뭐라도 있음(주황 세모)·
    /// 정한 무리 있음(파랑 동그라미). 누르면 그 차례로 돌고, 오른쪽 버튼은 거꾸로.
    /// </summary>
    private sealed class ConditionGrid : Control
    {
        private const double Cell = 17, Gap = 2;
        private static readonly IBrush AnyBrush = new SolidColorBrush(Color.FromRgb(50, 50, 56));
        private static readonly IBrush SameBrush = new SolidColorBrush(Color.FromRgb(62, 158, 92));
        private static readonly IBrush OtherBrush = new SolidColorBrush(Color.FromRgb(196, 70, 70));
        private static readonly IBrush FilledBrush = new SolidColorBrush(Color.FromRgb(190, 140, 40));
        private static readonly IBrush TargetBrush = new SolidColorBrush(Color.FromRgb(70, 120, 200));
        private static readonly IBrush CenterBrush = new SolidColorBrush(Color.FromRgb(36, 36, 42));
        private static readonly IBrush HoverOverlay = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        private static readonly IPen CellPen = AppTheme.Pen(Color.FromArgb(70, 255, 255, 255));
        private static readonly IPen CenterPen = AppTheme.Pen(AppTheme.AccentColor, 1.5);

        private readonly AutoTileEditorDialog _owner;
        private readonly int _index;
        private (int X, int Y)? _hover;

        public ConditionGrid(AutoTileEditorDialog owner, int index)
        {
            _owner = owner;
            _index = index;
            Width = Height = Cell * 3 + Gap * 2;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        /// <summary>격자 칸(-1~1)의 이웃 비트(가운데면 -1).</summary>
        private static int BitAt(int dx, int dy) => Array.IndexOf(AutoTileShapes.Directions, (dx, dy));

        private static Rect CellRect(int dx, int dy) => new((dx + 1) * (Cell + Gap), (dy + 1) * (Cell + Gap), Cell, Cell);

        public override void Render(DrawingContext g)
        {
            var rule = _owner._rules[_index];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var r = CellRect(dx, dy);
                    int bit = BitAt(dx, dy);
                    if (bit < 0)
                    {
                        g.FillRectangle(CenterBrush, r);
                        if (rule.HasTiles && _owner._ts.Find(rule.Tiles[0].Tile) is { } tile)
                            DrawFitted(g, _owner.Bitmap(tile.Image), tile.Image.Width, tile.Image.Height, r.Deflate(1));
                        g.DrawRectangle(null, CenterPen, r);
                        continue;
                    }
                    var cond = rule.Condition(bit);
                    g.FillRectangle(cond switch
                    {
                        NeighborCondition.Same => SameBrush,
                        NeighborCondition.Other => OtherBrush,
                        NeighborCondition.Filled => FilledBrush,
                        NeighborCondition.Target => TargetBrush,
                        _ => AnyBrush,
                    }, r);
                    if (cond == NeighborCondition.Same) VectorIcons.Draw(g, VectorIcon.Plus, r, 13, Brushes.White, 2.6);
                    else if (cond == NeighborCondition.Other) VectorIcons.Draw(g, VectorIcon.Minus, r, 13, Brushes.White, 2.6);
                    else if (cond == NeighborCondition.Filled) VectorIcons.Draw(g, VectorIcon.Triangle, r, 11, Brushes.White, 2, filled: true);
                    else if (cond == NeighborCondition.Target) VectorIcons.Draw(g, VectorIcon.Circle, r, 11, Brushes.White, 2.4);
                    g.DrawRectangle(null, CellPen, r);
                    if (_hover == (dx, dy)) g.FillRectangle(HoverOverlay, r);
                }
        }

        private static (int X, int Y)? CellAt(Avalonia.Point p)
        {
            int x = (int)Math.Floor(p.X / (Cell + Gap)) - 1, y = (int)Math.Floor(p.Y / (Cell + Gap)) - 1;
            return x is >= -1 and <= 1 && y is >= -1 and <= 1 && BitAt(x, y) >= 0 ? (x, y) : null;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var hover = CellAt(e.GetPosition(this));
            if (hover == _hover) return;
            _hover = hover;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = null;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (CellAt(pt.Position) is not { } c) return;
            int bit = BitAt(c.X, c.Y);
            var rule = _owner._rules[_index];
            bool back = pt.Properties.IsRightButtonPressed;
            // 누르기: 상관없음 → 있어야 함 → 없어야 함 → 뭐라도 있음 → 정한 무리 있음 → 상관없음, 오른쪽 버튼은 거꾸로
            var next = (rule.Condition(bit), back) switch
            {
                (NeighborCondition.Any, false) => NeighborCondition.Same,
                (NeighborCondition.Same, false) => NeighborCondition.Other,
                (NeighborCondition.Other, false) => NeighborCondition.Filled,
                (NeighborCondition.Filled, false) => NeighborCondition.Target,
                (NeighborCondition.Any, true) => NeighborCondition.Target,
                (NeighborCondition.Target, true) => NeighborCondition.Filled,
                (NeighborCondition.Filled, true) => NeighborCondition.Other,
                (NeighborCondition.Other, true) => NeighborCondition.Same,
                _ => NeighborCondition.Any,
            };
            _owner.SetRule(_index, rule.WithCondition(bit, next));
            e.Handled = true;
        }
    }

    /// <summary>
    /// 규칙에 넣은 타일들(작은 칸, 여러 장이면 비중 숫자). 누르기 = 고른 타일로 바꾸기, Ctrl+누르기 = 더하기,
    /// 타일 위 오른쪽 버튼(고른 타일이 없을 때는 왼쪽 버튼도) = 비중 바꾸기·빼기 메뉴.
    /// </summary>
    private sealed class RuleTilesView : Control
    {
        private const double Chip = 30, Gap = 3, ViewWidth = 186, MinViewHeight = 55;
        private static readonly IBrush Back = new SolidColorBrush(Color.FromRgb(34, 34, 40));
        private static readonly IBrush WeightBack = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        private static readonly IPen BackPen = AppTheme.Pen(Color.FromArgb(60, 255, 255, 255));
        private static readonly IPen ChipPen = AppTheme.Pen(Color.FromArgb(90, 255, 255, 255));
        private static readonly IPen HoverPen = AppTheme.Pen(AppTheme.AccentColor, 2);

        private readonly AutoTileEditorDialog _owner;
        private readonly int _index;
        private int _hover = -1;
        private bool _hoverArea;

        public RuleTilesView(AutoTileEditorDialog owner, int index)
        {
            _owner = owner;
            _index = index;
            Width = ViewWidth;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        private static int PerRow => (int)((ViewWidth - Gap) / (Chip + Gap));

        private AutoTileRule Rule => _owner._rules[_index];

        protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
        {
            int n = Rule.Tiles.Length;
            int rows = Math.Max(1, (n + PerRow - 1) / PerRow);
            return new Avalonia.Size(ViewWidth, Math.Max(MinViewHeight, rows * (Chip + Gap) + Gap));
        }

        private static Rect ChipRect(int i) => new(Gap + i % PerRow * (Chip + Gap), Gap + i / PerRow * (Chip + Gap), Chip, Chip);

        private int ChipAt(Avalonia.Point p)
        {
            for (int i = 0; i < Rule.Tiles.Length; i++)
                if (ChipRect(i).Contains(p)) return i;
            return -1;
        }

        public override void Render(DrawingContext g)
        {
            var area = new Rect(Bounds.Size);
            g.DrawRectangle(Back, _hoverArea && _hover < 0 ? HoverPen : BackPen, area.Deflate(0.5), 3, 3);
            var tiles = Rule.Tiles;
            if (tiles.IsDefaultOrEmpty)
            {
                AppTheme.DrawTextIn(g, UiStrings.AutoTile_RuleNoTiles, area, AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
                return;
            }
            bool showWeight = tiles.Length > 1 || tiles.Any(c => c.Weight > 1);
            for (int i = 0; i < tiles.Length; i++)
            {
                var r = ChipRect(i);
                AppTheme.DrawChecker(g, r, 4);
                if (_owner._ts.Find(tiles[i].Tile) is { } tile)
                    DrawFitted(g, _owner.Bitmap(tile.Image), tile.Image.Width, tile.Image.Height, r);
                g.DrawRectangle(null, i == _hover ? HoverPen : ChipPen, r);
                if (!showWeight) continue;
                var text = tiles[i].Weight.ToString(System.Globalization.CultureInfo.CurrentCulture);
                var ft = AppTheme.Text(text, AppTheme.TextBrush, AppTheme.SmallFontSize, bold: true);
                var box = new Rect(r.Right - ft.Width - 3, r.Bottom - ft.Height, ft.Width + 3, ft.Height);
                g.FillRectangle(WeightBack, box);
                g.DrawText(ft, new Avalonia.Point(box.X + 1.5, box.Y));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            int hover = ChipAt(e.GetPosition(this));
            if (hover == _hover && _hoverArea) return;
            _hover = hover;
            _hoverArea = true;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = -1;
            _hoverArea = false;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            int chip = ChipAt(pt.Position);
            if (pt.Properties.IsLeftButtonPressed)
            {
                if (_owner._palette.SelectedTiles.Count > 0) _owner.PutPicked(_index, Ui.HasCommand(e.KeyModifiers));
                else if (chip >= 0) OpenMenu(chip);
            }
            else if (pt.Properties.IsRightButtonPressed) OpenMenu(chip);
            e.Handled = true;
        }

        /// <summary>타일 <paramref name="chip"/>(없으면 -1 — 모두 빼기만)의 비중·빼기 메뉴.</summary>
        private void OpenMenu(int chip)
        {
            var tiles = Rule.Tiles;
            if (tiles.IsDefaultOrEmpty) return;
            var menu = new ContextMenu();
            if (chip >= 0)
            {
                var c = tiles[chip];
                menu.Items.Add(Item(UiStrings.AutoTile_WeightSet, VectorIcon.Pencil, () =>
                {
                    if (WeightDialog.Ask(_owner, c.Weight) is { } weight && weight != c.Weight)
                        SetTiles(tiles.SetItem(chip, c with { Weight = weight }));
                }));
                menu.Items.Add(Item(UiStrings.AutoTile_WeightUp, VectorIcon.Plus, () => SetTiles(tiles.SetItem(chip, c with { Weight = c.Weight + 1 })), c.Weight < int.MaxValue));
                menu.Items.Add(Item(UiStrings.AutoTile_WeightDown, VectorIcon.Minus, () => SetTiles(tiles.SetItem(chip, c with { Weight = c.Weight - 1 })), c.Weight > 1));
                menu.Items.Add(Item(UiStrings.AutoTile_RemoveTile, VectorIcon.X, () => SetTiles(tiles.RemoveAt(chip))));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item(UiStrings.AutoTile_ClearRuleTiles, VectorIcon.Trash, () => SetTiles([])));
            menu.Open(this);
        }

        private static MenuItem Item(string text, VectorIcon icon, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 14), IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        private void SetTiles(System.Collections.Immutable.ImmutableArray<AutoTileChoice> tiles)
            => _owner.SetRule(_index, Rule with { Tiles = tiles });
    }

    /// <summary>규칙에 넣은 타일 한 장의 비중을 숫자로 정한다(취소하면 변경 없음).</summary>
    private sealed class WeightDialog : DialogBase
    {
        private readonly NumericUpDown _weight;

        private WeightDialog(int current) : base(UiStrings.AutoTile_WeightSet)
        {
            _weight = Ui.Num(1, int.MaxValue, current, 100);
            Body.Children.Add(Ui.Row(Ui.Label(UiStrings.AutoTile_WeightValue), _weight));
        }

        public static int? Ask(Visual owner, int current)
        {
            var dlg = new WeightDialog(current);
            return dlg.ShowDialog(owner) == DialogResult.OK ? (int)dlg._weight.NumValue() : null;
        }
    }

    /// <summary>그림을 칸 가운데에 비율을 지켜 맞춰 그린다(점 그대로 확대).</summary>
    private static void DrawFitted(DrawingContext g, WriteableBitmap bmp, int width, int height, Rect r)
    {
        double s = Math.Min(r.Width / width, r.Height / height);
        double w = width * s, h = height * s;
        using (ImageUtil.Pixelated(g))
            g.DrawImage(bmp, new Rect(0, 0, width, height), new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
    }
}
