using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일셋 고르기 창: 프로젝트가 담은 타일셋과 기본 타일셋(앱에 든 것 — 고르면 새 사본)을 한 목록으로 보이고, 하나를 누르면 그것으로 닫힌다.
/// 지금 타일맵 레이어에 열려 있는 것은 "열려 있음"으로 보이고 누를 수 없다. 칸 크기가 정해진 자리(다른 타일셋과 함께 쓰기)면 칸 크기가 다른 것도 누를 수 없다.
/// 단추 줄의 "타일셋 관리…"는 새 빈 타일셋·파일 가져오기·이름 바꾸기·파일로 저장·지우기 창(<see cref="TilesetManagerDialog"/>)을 열고, 닫으면 목록을 다시 만든다.
/// Enter로 고르지 않는다(누르기만, Esc = 닫기).
/// </summary>
public sealed class TilesetChoiceDialog : DialogBase
{
    private const double RowWidth = 420;

    private readonly EditorContext _ctx;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly Size? _cell;
    // 지금 레이어에 열려 있는 타일셋 이름표
    private readonly IReadOnlySet<string> _open;
    private readonly Tileset[] _builtIns;
    private Tileset? _chosen;

    private TilesetChoiceDialog(EditorContext ctx, string title, Size? cell, IReadOnlySet<string> open) : base(title)
    {
        CloseOnly();
        _ctx = ctx;
        _cell = cell;
        _open = open;
        _builtIns = [.. BuiltInTilesets.All.Select(e => e.Create())];
        var manage = Ui.Button(VectorIcon.Settings, UiStrings.TilesetChoice_Manage, (_, _) => Manage(), width: 130).Tip(UiStrings.TilesetChoice_ManageTip);
        AddFooter(manage, CancelButtonControl);
        Body.Children.Add(new ScrollViewer
        {
            Content = _list,
            MaxHeight = 460,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        BuildList();
    }

    /// <summary>목록: 프로젝트의 타일셋(이름 차례), 그다음 기본 타일셋.</summary>
    private void BuildList()
    {
        _list.Children.Clear();
        if (_ctx.Project is not { } p) return;
        foreach (var ts in p.AllTilesets.OrderBy(t => t.Name, StringComparer.CurrentCulture))
        {
            var target = ts;
            _list.Children.Add(Row(ts, false, () => target));
        }
        for (int i = 0; i < _builtIns.Length; i++)
        {
            var entry = BuiltInTilesets.All[i];
            _list.Children.Add(Row(_builtIns[i], true, entry.Create));
        }
    }

    /// <summary>칸 크기가 정해진 자리에서 칸 크기가 다르면 그 안내(아니면 null).</summary>
    private string? Mismatch(Tileset ts)
        => _cell is { } c && (ts.CellWidth != c.Width || ts.CellHeight != c.Height) ? UiStrings.FormatTilesetChoice_CellMismatch(w: c.Width, h: c.Height) : null;

    /// <summary>타일셋 한 줄: 앞 타일 몇 개의 그림, 이름, 칸 크기·타일 수·자동 타일 수(열려 있으면 "열려 있음"). 누르면 <paramref name="make"/>의 타일셋으로 닫힌다.</summary>
    private Button Row(Tileset ts, bool builtIn, Func<Tileset> make)
    {
        bool open = !builtIn && _open.Contains(ts.Key);
        string? mismatch = open ? null : Mismatch(ts);
        var name = new TextBlock { Text = ts.Name.Length > 0 ? ts.Name : UiStrings.TilesetChoice_Unnamed, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var info = UiStrings.FormatTilesetChoice_Summary(w: ts.CellWidth, h: ts.CellHeight, tiles: ts.Tiles.Length, sets: ts.AutoTiles.Length);
        if (builtIn) info = UiStrings.TilesetChoice_BuiltInCopy + " · " + info;
        var detail = new TextBlock { Text = mismatch is null ? info : info + " · " + mismatch, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap };
        detail.Foreground = mismatch is null ? AppTheme.DimTextBrush : AppTheme.WarningBrush;
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } };
        if (open)
            text.Children.Add(new TextBlock { Text = UiStrings.TilesetChoice_Open, Foreground = AppTheme.AccentBrush, FontSize = AppTheme.SmallFontSize });
        var strip = new TilesetStrip(ts) { Margin = new Thickness(0, 0, 10, 0) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(text, 1);
        grid.Children.Add(strip);
        grid.Children.Add(text);
        var b = new Button
        {
            Content = grid,
            Width = RowWidth,
            Padding = new Thickness(8, 6),
            Margin = new Thickness(3, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !open && mismatch is null,
            BorderBrush = open ? AppTheme.AccentBrush : AppTheme.BorderBrush,
            BorderThickness = new Thickness(open ? 2 : 1),
        };
        b.Click += (_, _) =>
        {
            _chosen = make();
            DialogResult = DialogResult.OK;
        };
        return b;
    }

    private void Manage()
    {
        TilesetManagerDialog.Run(this, _ctx);
        BuildList();
    }

    /// <summary>
    /// 타일셋을 고른다(닫으면 null). <paramref name="open"/>(지금 레이어에 열린 이름표)은 "열려 있음"으로 보이고 고를 수 없으며,
    /// <paramref name="cell"/>이 있으면 그 칸 크기의 것만 고를 수 있다.
    /// </summary>
    public static Tileset? Run(Visual? owner, EditorContext ctx, string title, IReadOnlySet<string> open, Size? cell = null)
    {
        var dlg = new TilesetChoiceDialog(ctx, title, cell, open);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg._chosen : null;
    }
}

/// <summary>타일셋 앞 타일 몇 개(목록 차례, 큰 타일은 칸에 맞춰 줄임)를 가로로 그린 작은 그림.</summary>
internal sealed class TilesetStrip : Control
{
    private const int Count = 4;
    private const double Slot = 28;
    private static readonly IPen FramePen = AppTheme.Pen(AppTheme.BorderColor);
    private readonly TileDef[] _tiles;

    public TilesetStrip(Tileset ts)
    {
        _tiles = [.. ts.Tiles.OrderBy(t => t.PaletteY).ThenBy(t => t.PaletteX).Take(Count)];
        Width = Count * Slot;
        Height = Slot;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public override void Render(DrawingContext g)
    {
        AppTheme.DrawChecker(g, new Rect(0, 0, Width, Height), 7);
        using (ImageUtil.Pixelated(g))
            for (int i = 0; i < _tiles.Length; i++)
            {
                var image = _tiles[i].Image;
                var slot = new Rect(i * Slot, 0, Slot, Slot).Deflate(2);
                double scale = Math.Min(slot.Width / image.Width, slot.Height / image.Height);
                double w = image.Width * scale, h = image.Height * scale;
                g.DrawImage(TileVisuals.Bitmap(image), new Rect(0, 0, image.Width, image.Height),
                    new Rect(slot.X + (slot.Width - w) / 2, slot.Y + (slot.Height - h) / 2, w, h));
            }
        g.DrawRectangle(null, FramePen, new Rect(0, 0, Width, Height).Deflate(0.5));
    }
}
