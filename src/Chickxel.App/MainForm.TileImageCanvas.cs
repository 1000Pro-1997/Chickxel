using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

// 타일 그림 ↔ 캔버스: 타일 그림(움직이는 타일은 그림마다 프레임 하나, 장당 시간 = 프레임 길이)을 저장하지 않는 문서(탭)로 열고,
// 탭 아래 막대의 "타일에 저장"(또는 저장 단축키)으로 프레임마다 합성한 그림을 그 타일의 그림으로 되돌려 쓴다(그 타일셋을 가진 문서에서 실행취소 한 단계).
// 저장하지 않고 고친 채 닫으면 타일에 저장할지 묻는다.
public sealed partial class MainForm
{
    /// <summary>캔버스에서 고치는 중인 타일 하나: 타일셋을 가진 문서의 프로젝트, 타일셋 이름표, 타일 Id, 고치는 문서.</summary>
    private sealed class TileImageEdit(SpriteProject owner, string tilesetKey, int tileId)
    {
        public SpriteProject Owner { get; } = owner;
        public string TilesetKey { get; } = tilesetKey;
        public int TileId { get; } = tileId;
        public OpenDocument Doc { get; set; } = null!;
    }

    /// <summary>열린 타일 그림 고치기 문서들(타일마다 하나).</summary>
    private readonly List<TileImageEdit> _tileImageEdits = [];

    /// <summary><paramref name="project"/>가 타일 그림을 고치는 문서면 그 고치기(아니면 null).</summary>
    private TileImageEdit? TileImageEditOf(SpriteProject project) => _tileImageEdits.Find(e => ReferenceEquals(e.Doc.Project, project));

    /// <summary>
    /// 지금 프로젝트의 타일셋 <paramref name="tilesetKey"/>에 든 타일 <paramref name="tileId"/>(그 타일셋 안의 Id) 그림을 새 탭에서 고친다(이미 고치는 중이면 그 탭을 고름).
    /// 캔버스 = 차지 칸(기준 자리)이고 그림은 그림 위치(<see cref="TileDef.DrawOffset"/>)에 놓여, 차지 칸 밖으로 솟은 부분은 캔버스 밖 그림으로 보인다 — 밖에 더 그려도 된다.
    /// </summary>
    private void EditTileImageOnCanvas(string tilesetKey, int tileId)
    {
        if (_ctx.Project is not { } owner || !owner.Tilesets.TryGetValue(tilesetKey, out var ts) || ts.Find(tileId) is not { } def) return;
        _ctx.CommitFloating();
        if (_tileImageEdits.Find(e => ReferenceEquals(e.Owner, owner) && e.TilesetKey == ts.Key && e.TileId == tileId) is { } open)
        {
            ShowDocument(open.Doc);
            return;
        }
        var project = new SpriteProject(def.Width * ts.CellWidth, def.Height * ts.CellHeight);
        for (int f = 1; f < def.FrameCount; f++) project.AddFrame();
        for (int f = 0; f < def.FrameCount; f++)
        {
            project.SetFullCel(0, f, def.FrameImage(f).Clone(), def.DrawOffset);
            if (def.IsAnimated) project.SetFrameDuration(f, def.FrameMs);
        }
        project.History.Clear();
        project.IsDirty = false;
        var edit = new TileImageEdit(owner, ts.Key, tileId);
        var name = TileVisuals.TileName(def);
        edit.Doc = new OpenDocument(project, null)
        {
            DetachedTitle = UiStrings.FormatTileImage_TabTitle(name: name),
            Closed = () => _tileImageEdits.Remove(edit),
            ConfirmClose = () => ConfirmCloseTileImageEdit(edit),
            Bar = TileImageEditBar(edit, name),
        };
        _tileImageEdits.Add(edit);
        OpenInNewTab(edit.Doc);
        Activate();
        ShowMessage(UiStrings.TileImage_EditHint);
    }

    /// <summary>탭 아래 막대: 고치는 타일 안내 + "타일에 저장"(강조)·닫기.</summary>
    private Control TileImageEditBar(TileImageEdit edit, string name)
    {
        var text = new TextBlock
        {
            Text = UiStrings.FormatTileImage_Bar(name: name),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0),
        };
        ToolTip.SetTip(text, text.Text);
        var save = Ui.Button(VectorIcon.Save, UiStrings.TileImage_Save, (_, _) => SaveTileImageEdit(edit))
            .Tip(KeyHint(UiStrings.TileImage_SaveTip, "파일/저장(+ 시트 내보내기)"));
        save.Background = AppTheme.AccentBrush;
        save.Foreground = Brushes.White;
        save.FontWeight = FontWeight.Bold;
        var close = Ui.Button(VectorIcon.X, UiStrings.S_Ui_CloseOnly_11312F9A81, (_, _) =>
        {
            if (TabOf(edit.Doc) is { } tab) Dispatcher.UIThread.Post(() => CloseTab(tab));
        }).Tip(UiStrings.TileImage_CloseTip);
        save.Focusable = close.Focusable = false;
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(save, Dock.Right);
        var icon = Ui.Icon(VectorIcon.Pencil, 14);
        icon.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(close);
        row.Children.Add(save);
        row.Children.Add(icon);
        row.Children.Add(text);
        var accent = AppTheme.AccentColor;
        return new Border
        {
            Background = AppTheme.Brush(Color.FromArgb(56, accent.R, accent.G, accent.B)),
            BorderBrush = AppTheme.AccentBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2),
            Child = row,
        };
    }

    /// <summary>
    /// 고친 문서를 타일 그림으로 쓴다: 프레임마다 보이는 일반 레이어를 캔버스 밖 그림까지 합쳐(레이어 불투명도만 — 효과는 빼고), 모든 프레임의 그림을 감싼
    /// 사각형을 그림으로 한다. 캔버스(차지 칸) 왼쪽 위에서 그 사각형까지가 그림 위치가 되므로 위·옆·아래 어디로 넓혀도 차지 칸에 맞는 자리에 그려진다.
    /// 프레임이 여럿이면 움직이는 타일(장당 시간 = 첫 프레임 길이). 타일셋을 가진 문서가 닫혔거나 타일이 지워졌으면 알리고 쓰지 않는다. 썼으면 true.
    /// </summary>
    private bool SaveTileImageEdit(TileImageEdit edit)
    {
        var p = edit.Doc.Project;
        if (ReferenceEquals(p, _ctx.Project)) _ctx.CommitFloating();
        bool ownerOpen = _tabs.All.Any(t => t.Doc is { } d && ReferenceEquals(d.Project, edit.Owner));
        if (!ownerOpen || !edit.Owner.Tilesets.TryGetValue(edit.TilesetKey, out var ts) || ts.Find(edit.TileId) is not { } def)
        {
            Ui.Error(this, UiStrings.TileImage_TargetGone, UiStrings.TileImage_Save);
            return false;
        }
        if (p.Frames.Count > TileDef.MaxFrames)
        {
            Ui.Error(this, UiStrings.FormatTileImage_TooManyFrames(max: TileDef.MaxFrames), UiStrings.TileImage_Save);
            return false;
        }
        if (TileFrames(p) is not { } result)
        {
            Ui.Error(this, UiStrings.TileImage_Empty, UiStrings.TileImage_Save);
            return false;
        }
        int frameMs = Math.Max(TileDef.MinFrameMs, p.Frames[0].DurationMs);
        var next = def.WithFrames(result.Frames, result.Frames.Count > 1 ? frameMs : def.FrameMs) with { DrawOffset = result.Offset };
        edit.Owner.EditTileset(UiStrings.TileImage_Label, TilesetEdit.ReplaceTile(ts, next));
        p.IsDirty = false;
        ShowMessage(UiStrings.FormatTileImage_Saved(name: TileVisuals.TileName(def), count: result.Frames.Count));
        return true;
    }

    /// <summary>
    /// 프레임마다 보이는 일반 레이어(캔버스 안 셀 + 캔버스 밖 그림)를 아래부터 레이어 불투명도로 겹친 그림을, 모든 프레임의 불투명한 픽셀을 감싼 같은 크기로 자른 것과
    /// 그 사각형의 왼쪽 위(캔버스 좌표 = 그림 위치). 모든 프레임이 비면 null.
    /// </summary>
    private static (List<PixelImage> Frames, Point Offset)? TileFrames(SpriteProject p)
    {
        var layers = Enumerable.Range(0, p.Layers.Count).Where(i => p.Layers[i].Kind == LayerKind.Normal && p.IsLayerVisible(i)).ToList();
        var fulls = new List<List<(PixelImage Image, Point Origin, int Opacity)>>();
        Rectangle box = Rectangle.Empty;
        for (int f = 0; f < p.Frames.Count; f++)
        {
            var list = new List<(PixelImage, Point, int)>();
            foreach (int i in layers)
            {
                var (image, origin) = p.GetFullCel(i, f);
                var opaque = image.GetOpaqueBounds();
                if (opaque.IsEmpty) continue;
                opaque.Offset(origin);
                box = box.IsEmpty ? opaque : Rectangle.Union(box, opaque);
                list.Add((image, origin, p.Layers[i].Opacity));
            }
            fulls.Add(list);
        }
        if (box.IsEmpty) return null;
        var frames = new List<PixelImage>(fulls.Count);
        foreach (var list in fulls)
        {
            var frame = new PixelImage(box.Width, box.Height);
            foreach (var (image, origin, opacity) in list)
            {
                var src = image;
                if (opacity < 255)
                {
                    src = image.Clone();
                    var px = src.Pixels;
                    for (int k = 0; k < px.Length; k++)
                        if (px[k].A != 0) px[k] = px[k].WithAlpha((byte)(px[k].A * opacity / 255));
                }
                frame.Blit(src, origin.X - box.X, origin.Y - box.Y, alphaBlend: true);
            }
            frames.Add(frame);
        }
        return (frames, box.Location);
    }

    /// <summary>타일 그림 고치기 문서를 닫기 전: 저장 뒤 고쳤으면 타일에 저장할지 묻는다(저장·저장 안 함·취소). 닫아도 되면 true.</summary>
    private bool ConfirmCloseTileImageEdit(TileImageEdit edit)
    {
        if (!edit.Doc.Project.IsDirty) return true;
        if (TabOf(edit.Doc) is { } tab) ActivateTab(tab);
        return Ui.Ask(this, UiStrings.TileImage_ApplyConfirm, UiStrings.TileImage_Save, MessageButtons.YesNoCancel,
                MessageIcon.Question, okText: UiStrings.TileImage_Save, noText: UiStrings.ShapeCanvas_DontSave) switch
            {
                DialogResult.Yes => SaveTileImageEdit(edit),
                DialogResult.No => true,
                _ => false,
            };
    }
}
