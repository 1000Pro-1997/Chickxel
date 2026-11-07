using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Chickxel.App;
using Chickxel.Core.Model;
using Chickxel.Core.Effects;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>Guide images for the current layer, timeline, effects and preview controls.</summary>
internal static class GuideFeatureShots
{
    private static Control Timeline(MainForm m) => ShotKit.Get<Control>(m, "_timeline");
    private static Control Grid(MainForm m) => ShotKit.Get<Control>(Timeline(m), "_grid");
    private static Control Options(MainForm m) => ShotKit.Get<Control>(m, "_toolOptions");
    private static Control Preview(MainForm m) => ShotKit.Get<Control>(m, "_preview");
    private static Control Canvas(MainForm m) => ShotKit.Get<Control>(m, "_canvas");
    private static Rect R(MainForm m, Control c) => ShotKit.Bounds(c, m);
    private static Point GridPoint(MainForm m, double x, double y) => Grid(m).TranslatePoint(new Point(x, y), m)!.Value;
    private static Rect GridRect(MainForm m, double x, double y, double width, double height)
        => new(GridPoint(m, x, y), new Size(width, height));
    private static int Layer(MainForm m, string name) => Project(m).IndexOf(Project(m).Layers.First(l => l.Name == name));
    private static Rect Row(MainForm m, int layer)
    {
        var y = (int)ShotKit.Call(Grid(m), "LayerY", layer)!;
        return GridRect(m, 0, y, 270, 36);
    }
    private static Rect Cel(MainForm m, int layer, int frame)
    {
        var row = Row(m, layer);
        return new Rect(row.X + 270 + frame * 42, row.Y, 42, row.Height);
    }
    private static MainForm Demo(ShotKit k, bool effects = false, bool group = true)
    {
        App.Settings.BottomHeight = 350;
        App.Settings.RightWidth = 390;
        App.Settings.PreviewHeight = 190;
        var main = Open(k, height: 1020);
        Build(k, main, effects: effects, group: group);
        return main;
    }

    [Shot("tools-layer-size")]
    public static void LayerSize(ShotKit k)
    {
        var m = Demo(k, group: false);
        k.Command("레이어 크기…");
        k.Settle(400);
        var from = Ctx(m).LayerResizeFrom;
        if (from.IsEmpty) throw new InvalidOperationException("layer resize was not entered");
        // Reducing the layer area clips the picture; the clipped pixels remain dimmed until Apply.
        var area = new System.Drawing.Rectangle(from.X + 5, from.Y + 4, from.Width - 10, from.Height - 12);
        Ctx(m).SetLayerResize(area, from);
        k.Settle(350);
        var opt = Options(m);
        var border = (Point)ShotKit.Call(Canvas(m), "ImageToScreen", (double)area.X, (double)area.Y)!;
        var handle = Canvas(m).TranslatePoint(border, m)!.Value;
        var crop = R(m, Canvas(m)).Union(R(m, opt)).Intersect(new Rect(m.ClientSize));
        var flip = (Control)ShotKit.Get<Control>(opt, "_crFlipH").Parent!;
        k.Save(m, crop: crop, marks: [new Rect(handle.X - 17, handle.Y - 17, 34, 34), R(m, ShotKit.Get<Control>(opt, "_canvasResizeBox")), R(m, flip)]);
    }

    [Shot("screen-effect-dock")]
    public static void SelectedEffectDock(ShotKit k)
    {
        var m = Demo(k, effects: true);
        var body = Layer(m, "Body");
        var r = Row(m, body);
        // The first effect row immediately follows Body; selecting it opens the separate effect dock.
        var effect = GridRect(m, 95, (r.Y - GridPoint(m, 0, 0).Y) + 36 + 8, 120, 16);
        ShotKit.Call(Grid(m), "SelectEffects", new (Layer Layer, int Index)[] { (Project(m).Layers[body], 0) });
        k.Settle(400);
        var section = ShotKit.Get<Control>(Options(m), "_effectSection");
        if (!section.IsVisible) throw new InvalidOperationException("selected effect did not open the effect dock");
        k.Save(m, crop: R(m, Timeline(m)).Union(R(m, Options(m))), marks: [effect, R(m, section)], firstNumber: 3);
    }

    [Shot("screen-timeline-links")]
    public static void LinkedCels(ShotKit k)
    {
        var m = Demo(k);
        var body = Layer(m, "Body");
        Ctx(m).Structural(() => Project(m).LinkCels([(body, 1), (body, 2), (body, 3)], 1));
        k.Settle(300);
        k.Click(m, Cel(m, body, 1).Center);
        k.Click(m, Cel(m, body, 3).Center, mods: RawInputModifiers.Shift);
        var linked = Cel(m, body, 1).Union(Cel(m, body, 3));
        k.Save(m, crop: R(m, Timeline(m)), marks: [linked]);
        k.Click(m, new Point(Cel(m, body, 3).Right - 5, Cel(m, body, 3).Bottom - 5), MouseButton.Right, settleMs: 500);
        var menu = ScreenTimelineShots.PopupArea(m) ?? throw new InvalidOperationException("cel context menu did not open");
        var crop = linked.Union(menu).Inflate(16).Intersect(new Rect(m.ClientSize));
        k.Save(m, "screen-timeline-links-2", crop: crop, marks: [linked, menu], firstNumber: 2);
    }

    [Shot("screen-timeline-multiselect")]
    public static void ScatteredSelection(ShotKit k)
    {
        var m = Demo(k);
        var body = Layer(m, "Body");
        var shadow = Layer(m, "Shadow");
        k.Click(m, Cel(m, body, 0).Center);
        k.Click(m, Cel(m, body, 2).Center, mods: RawInputModifiers.Control);
        k.Click(m, Cel(m, shadow, 4).Center, mods: RawInputModifiers.Control);
        k.Settle(250);
        k.Save(m, crop: R(m, Timeline(m)), marks: [Cel(m, body, 0), Cel(m, body, 2), Cel(m, shadow, 4)], firstNumber: 4);
    }

    [Shot("screen-timeline-compact")]
    public static void CompactTimeline(ShotKit k)
    {
        var m = Demo(k);
        var t = Timeline(m);
        ShotKit.Set(t, "Thumbnails", false);
        Ctx(m).Structural(() => Project(m).SetCel(Layer(m, "Body"), 2, null));
        k.Settle(350);
        // The image toggle occupies the title's rightmost end; marks identify toggle and dot/empty cells.
        var icon = ShotKit.Get<Control>(t, "_thumbIcon");
        var body = Row(m, Layer(m, "Body"));
        var dots = new Rect(body.X + 270, body.Y, 5 * 20, 22);
        k.Save(m, crop: R(m, t), marks: [R(m, icon).Inflate(6), dots], firstNumber: 20);
    }

    [Shot("screen-timeline-copy-image")]
    public static void CopyRenderedMenu(ShotKit k)
    {
        var m = Demo(k, effects: true);
        var cell = Cel(m, Layer(m, "Body"), 1);
        k.Click(m, cell.Center);
        k.Click(m, new Point(cell.Right - 5, cell.Bottom - 5), MouseButton.Right, settleMs: 450);
        var menu = ScreenTimelineShots.PopupArea(m) ?? throw new InvalidOperationException("context menu did not open");
        k.Save(m, crop: cell.Union(menu).Inflate(15).Intersect(new Rect(m.ClientSize)), marks: [cell, menu], firstNumber: 5);
    }

    [Shot("screen-preview-large")]
    public static void LargePreview(ShotKit k)
    {
        var m = Demo(k, group: false);
        ShotKit.Call(Preview(m), "OpenZoom");
        k.Settle(500);
        var window = k.Windows.Last(w => !ReferenceEquals(w, m));
        window.Width = 750;
        window.Height = 750;
        k.Settle(400);
        k.Save(window, marks: [ShotKit.Bounds((Control)window.Content!, window).Deflate(20)], firstNumber: 7);
    }

    [Shot("screen-preview-tab")]
    public static void PreviewTab(ShotKit k)
    {
        var m = Demo(k, group: false);
        var p = Preview(m);
        var tabButton = ShotKit.All<Control>(p).First(c => ToolTip.GetTip(c) as string == Chickxel.App.Localization.UiStrings.Preview_OpenTabTip);
        ShotKit.Call(m, "OpenPreviewTab");
        k.Settle(500);
        k.Save(m, crop: R(m, p).Union(R(m, Canvas(m))).Inflate(3).Intersect(new Rect(m.ClientSize)), marks: [R(m, tabButton).Inflate(4)], firstNumber: 10);
    }

    [Shot("screen-canvas-effects-toggle")]
    public static void CanvasEffectsToggle(ShotKit k)
    {
        var m = Demo(k, effects: true, group: false);
        var button = ShotKit.Get<Control>(m, "_cbEffects");
        k.Command("캔버스에 효과 보기");
        k.Settle(500);
        k.Save(m, crop: R(m, Canvas(m)).Union(R(m, Preview(m))).Union(R(m, button)).Inflate(3).Intersect(new Rect(m.ClientSize)),
            marks: [R(m, button).Inflate(5), R(m, Canvas(m)).Deflate(45), R(m, Preview(m)).Deflate(18)], firstNumber: 12);
    }

    [Shot("parts-skin-blank-clone")]
    public static void BlankAndCloneSkin(ShotKit k)
    {
        var m = Demo(k);
        var p = Project(m);
        var group = p.Layers.First(l => l.IsGroup);
        var baseSkin = group.BaseSkin;
        Ctx(m).Structural(() =>
        {
            p.AddSkin(group, "Empty", null);
            p.AddSkin(group, "Copy", baseSkin);
        });
        Ctx(m).SetActiveSkin(group, group.Skins.First(s => s.Name == "Empty"));
        k.Settle(350);
        var row = Row(m, p.IndexOf(group));
        var crop = R(m, Canvas(m)).Union(R(m, Preview(m))).Union(R(m, Timeline(m)));
        k.Save(m, crop: crop, marks: [row, R(m, Canvas(m)).Deflate(60), R(m, Preview(m)).Deflate(20)]);
        Ctx(m).SetActiveSkin(group, group.Skins.First(s => s.Name == "Copy"));
        k.Settle(350);
        k.Save(m, "parts-skin-blank-clone-2", crop: crop, marks: [row, R(m, Canvas(m)).Deflate(60)], firstNumber: 4);
    }

    [Shot("screen-layer-effects-current")]
    public static void EffectSettings(ShotKit k)
    {
        var m = Demo(k, effects: true);
        var p = Project(m);
        int body = Layer(m, "Body");
        var shake = Chickxel.Core.Effects.LegacyEffects.Convert(new ShakeEffect { Frames = new FrameRange(1, 3), HoldEnd = true });
        if (shake is ScriptEffect script) p.AddScript(Chickxel.Core.Scripting.ScriptLibrary.SourceOf(script.ScriptHash)!);
        Ctx(m).Structural(() =>
        {
            p.SetLayerEffects(body, [shake, new RotateEffect { Angle = 12, Frames = new FrameRange(1, 3) }, .. p.Layers[body].Effects]);
            // Frame 5 shares the range-end drawing; frame 6 is independent, so both tail intensities are visible.
            p.LinkCels([(body, 3), (body, 4)], 3);
        });
        k.Settle(300);
        k.Dialog(() => ShotKit.Call(Timeline(m), "EditLayerEffects", body, 0, null, null), w =>
        {
            var settings = ShotKit.Get<Control>(w, "_settings");
            var expand = ShotKit.All<CheckBox>(settings).First(c => c.Content is string s && s == Chickxel.App.Localization.UiStrings.Effect_ExpandCanvas);
            var pivot = ShotKit.All<CheckBox>(settings).First(c => c.Content is string s && s == Chickxel.App.Localization.UiStrings.Effect_MovePivot);
            var hold = ShotKit.All<CheckBox>(settings).First(c => c.Content is string s && s == Chickxel.App.Localization.UiStrings.Effect_HoldEnd);
            var mode = ShotKit.Get<Control>(w, "_expandMode");
            k.Save(w, marks: [expand, pivot, hold, (Control)mode.Parent!]);
            ToolTip.SetIsOpen(pivot, true);
            ShotKit.Pump(400);
            var tooltip = ScreenTimelineShots.PopupArea(w) ?? throw new InvalidOperationException("pivot movement tooltip did not open");
            k.Save(w, "screen-layer-effects-pivot-tip", crop: ShotKit.Bounds(settings, w).Union(tooltip).Inflate(12).Intersect(new Rect(w.ClientSize)),
                marks: [pivot], firstNumber: 5);
            ToolTip.SetIsOpen(pivot, false);
        }, settleMs: 700);
        var row = Row(m, body);
        var strip = new Rect(row.X + 270 + 2 * 42, row.Bottom + 2, 4 * 42, 16);
        k.Save(m, "screen-layer-effects-hold-end", crop: R(m, Timeline(m)), marks: [strip], firstNumber: 6);
    }

    [Shot("screen-timeline-add-drag")]
    public static void DragNextFrame(ShotKit k)
    {
        var m = Demo(k);
        var ghost = GridRect(m, 270 + 6 * 42, 20, 42, 30);
        var start = ghost.Center;
        var end = new Point(start.X + 3 * 42, start.Y);
        m.MouseMove(start);
        m.MouseDown(start, MouseButton.Left);
        for (int i = 1; i <= 16; i++)
        {
            m.MouseMove(start + (end - start) * (i / 16.0));
            k.Settle(25);
        }
        k.Settle(300);
        k.Save(m, crop: R(m, Timeline(m)), marks: [ghost.Union(new Rect(end.X - 21, end.Y - 15, 42, 30))], firstNumber: 19);
        m.MouseUp(end, MouseButton.Left);
    }

    [Shot("parts-layer-skin-menu")]
    public static void LayerSkinMenu(ShotKit k)
    {
        var m = Demo(k, group: false);
        var row = Row(m, Layer(m, "Body"));
        k.Click(m, new Point(row.X + 110, row.Center.Y), MouseButton.Right, settleMs: 450);
        var item = ScreenTimelineShots.OpenPopups()
            .SelectMany(p => ShotKit.All<MenuItem>(p.Child!))
            .First(mi => mi.ItemsSource is not null && mi.Header is TextBlock t && t.Text == Chickxel.App.Localization.UiStrings.Timeline_AddSkinToLayer);
        item.IsSubMenuOpen = true;
        k.Settle(450);
        var menu = ScreenTimelineShots.PopupArea(m) ?? throw new InvalidOperationException("skin submenu did not open");
        k.Save(m, crop: row.Union(menu).Inflate(12).Intersect(new Rect(m.ClientSize)), marks: [row, menu]);
    }

    [Shot("screen-timeline-group-links")]
    public static void GroupLinkedCels(ShotKit k)
    {
        var m = Demo(k);
        var p = Project(m);
        var group = p.Layers.First(l => l.IsGroup);
        var groupIndex = p.IndexOf(group);
        Ctx(m).Structural(() => p.LinkCels([(groupIndex, 0), (groupIndex, 1), (groupIndex, 2)], 0));
        k.Settle(350);
        var r = Row(m, groupIndex);
        var children = p.ChildrenOf(group).Select(l => Row(m, p.IndexOf(l))).ToList();
        var groupCells = new Rect(r.X + 270, r.Y, 3 * 42, r.Height);
        var partCells = new Rect(children.Min(c => c.X) + 270, children.Min(c => c.Y), 3 * 42, children.Max(c => c.Bottom) - children.Min(c => c.Y));
        k.Save(m, crop: R(m, Timeline(m)), marks: [groupCells, partCells], firstNumber: 4);
    }

    [Shot("screen-timeline-group-drag")]
    public static void ShiftGroupDrag(ShotKit k)
    {
        var m = Demo(k);
        var source = Row(m, Layer(m, "Body"));
        var target = Row(m, Layer(m, "Props"));
        var from = new Point(source.X + 105, source.Center.Y);
        var to = new Point(target.X + 115, target.Center.Y);
        k.Click(m, from);
        k.Settle(650); // The drag must not count as the second click that starts inline renaming.
        m.MouseMove(from);
        m.MouseDown(from, MouseButton.Left);
        var lifting = new Point(from.X, from.Y - 12);
        m.MouseMove(lifting);
        k.Settle(90);
        for (int i = 1; i <= 16; i++)
        {
            m.MouseMove(lifting + (to - lifting) * (i / 16.0), RawInputModifiers.Shift);
            k.Settle(25);
        }
        k.Settle(350);
        var reordering = ShotKit.Get<bool>(Grid(m), "_reordering");
        var nesting = ShotKit.Get<bool>(Grid(m), "_nestMode");
        var drop = ShotKit.Get<object?>(Grid(m), "_nestDrop");
        if (!reordering || !nesting || drop is null)
            throw new InvalidOperationException($"Shift nesting preview was not active: reordering={reordering}, nesting={nesting}, target={drop}");
        k.Save(m, crop: R(m, Timeline(m)), marks: [source, target]);
        m.MouseUp(to, MouseButton.Left, RawInputModifiers.Shift);
    }

    [Shot("tools-canvas-size-ctrl")]
    public static void CanvasSizeWithControl(ShotKit k)
    {
        var m = Demo(k, group: false);
        k.Command("캔버스 크기…");
        k.Settle(350);
        var c = Ctx(m).CanvasResizeCanvas;
        var canvas = Canvas(m);
        var from = canvas.TranslatePoint((Point)ShotKit.Call(canvas, "ImageToScreen", (double)c.Right, (double)c.Bottom)!, m)!.Value;
        var to = new Point(from.X + 32, from.Y + 24);
        m.MouseMove(from, RawInputModifiers.Control);
        m.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        for (int i = 1; i <= 12; i++)
        {
            m.MouseMove(from + (to - from) * (i / 12.0), RawInputModifiers.Control);
            k.Settle(20);
        }
        k.Settle(250);
        k.Save(m, crop: R(m, canvas), marks: [new Rect(to.X - 18, to.Y - 18, 36, 36)]);
        m.MouseUp(to, MouseButton.Left, RawInputModifiers.Control);
    }

    [Shot("parts-pivot-frame")]
    public static void PartFramePivot(ShotKit k)
    {
        var m = Demo(k);
        var p = Project(m);
        int part = Layer(m, "Gem");
        Ctx(m).Structural(() => p.SetLayerFramePivot(part, 2, new Pivot(0.25f, 0.75f)));
        Ctx(m).SetCurrent(part, 2);
        ToolFileHelp.Tool(k, "피벗");
        k.Settle(350);
        var check = ShotKit.Get<Control>(Options(m), "_framePivot");
        k.Save(m, crop: R(m, Timeline(m)).Union(R(m, Options(m))), marks: [Cel(m, part, 2), R(m, check).Inflate(5)]);
    }

    [Shot("parts-pivot-effect-view")]
    public static void MovedPartPivot(ShotKit k)
    {
        var m = PartsSample.Open(k);
        var p = Project(m);
        var group = p.Layers.First(l => l.Name == PartsSample.GroupName);
        var head = p.Layers.First(l => l.Name == "Head");
        int index = p.IndexOf(head);
        Ctx(m).SetActiveSkin(group, group.BaseSkin!);
        var shake = Chickxel.Core.Effects.LegacyEffects.Convert(new ShakeEffect
        {
            Frames = new FrameRange(0, 3), Amount = 8, Axis = EffectAxis.Both, Decay = false, Seed = 3
        });
        if (shake is ScriptEffect script) p.AddScript(Chickxel.Core.Scripting.ScriptLibrary.SourceOf(script.ScriptHash)!);
        Ctx(m).Structural(() => p.SetLayerEffects(index, [shake]));
        Ctx(m).SetCurrent(index, 1);
        ToolFileHelp.Tool(k, "피벗");
        k.Settle(500);
        var shift = p.EffectPivotShift(head, 1);
        if (Math.Abs(shift.X) + Math.Abs(shift.Y) < 1)
            throw new InvalidOperationException("moving part effect reported no pivot displacement for this frame");
        var pivot = p.EffectPivot(head, 1);
        var canvas = Canvas(m);
        Point At(double x, double y) => canvas.TranslatePoint((Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!, m)!.Value;
        var moved = At(pivot.X + shift.X, pivot.Y + shift.Y);
        var original = At(pivot.X, pivot.Y);
        var effects = ShotKit.Get<Control>(m, "_cbEffects");
        var crop = R(m, canvas).Union(R(m, Preview(m))).Union(R(m, effects)).Intersect(new Rect(m.ClientSize));
        k.Save(m, crop: crop, marks: [new Rect(moved.X - 15, moved.Y - 15, 30, 30), R(m, effects).Inflate(5)]);
        k.Command("캔버스에 효과 보기");
        k.Settle(350);
        k.Save(m, "parts-pivot-effect-view-2", crop: crop,
            marks: [new Rect(original.X - 15, original.Y - 15, 30, 30), R(m, effects).Inflate(5)], firstNumber: 3);
    }
}
