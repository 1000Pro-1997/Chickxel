using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 스킨 관리 창(모드리스, 하나만): 맨 위 보기 거르기 [전체 | 그룹 스킨 | 개별 스킨], 지금 프레임의 전체 착장, 착장 프리셋 카드 줄, 그룹마다(타임라인처럼 위→아래) 스킨 카드 줄.
/// 카드를 누르면 바로 그 스킨(착장)을 입히고, 우클릭 메뉴로 복제·이름·삭제·순서를 고친다(실행취소됨). "▸ 부위별"을 펼치면
/// 부위마다 "없음" + 그룹 스킨 카드 + 그 부위 전용(개별) 스킨 카드("개별" 표시)가 보여 부위 하나만 다른 스킨을 입힐 수 있고, 줄 끝 "＋ 이 부위 스킨"으로 부위 전용 스킨을 만든다.
/// "개별 스킨" 거르기면 그룹 카드는 숨기고 부위 줄을 모두 펼쳐 부위 전용 스킨만 보인다.
/// 프로젝트 변경·현재 프레임 변경·다른 프로젝트 열기 때 약 100ms 늦춰 다시 만든다(그리는 중에는 끝날 때까지 미룸).
/// </summary>
public sealed class SkinManagerWindow : Window
{
    private const int GroupThumb = 72, PartThumb = 52, PreviewH = 200;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private static SkinManagerWindow? _instance;

    private readonly EditorContext _ctx;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly SkinThumb _preview = new(null, default) { Height = PreviewH, Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _outfits = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel _list = new() { Spacing = 6 };
    /// <summary>마지막 착장 적용에서 건너뛴 항목 안내(다음 적용·프로젝트 바꾸기 때 지움).</summary>
    private string? _outfitWarning;
    /// <summary>이번에 만든 썸네일 비트맵(다시 만들거나 닫을 때 Dispose).</summary>
    private readonly List<WriteableBitmap> _bitmaps = new();
    /// <summary>부위 줄을 펼친 그룹.</summary>
    private readonly HashSet<Layer> _expanded = new(ReferenceEqualityComparer.Instance);
    /// <summary>강조할 그룹/부위(칩을 눌러 열었을 때), 다음 다시 만들기에서 그 줄로 스크롤할지.</summary>
    private Layer? _focus;
    private bool _scrollToFocus;
    /// <summary>보기 거르기: 0 = 전체, 1 = 그룹 스킨(전체 스킨)만, 2 = 개별 스킨(부위 전용)만.</summary>
    private readonly TabStrip _filter = new(TabStripKind.Segmented, "전체", "그룹 스킨", "개별 스킨");

    private bool ShowFull => _filter.SelectedIndex != 2;
    private bool ShowPartOnly => _filter.SelectedIndex != 1;

    private SkinManagerWindow(EditorContext ctx)
    {
        _ctx = ctx;
        Title = "스킨 관리";
        ShowInTaskbar = false;
        Width = 680;
        Height = 720;
        MinWidth = 360;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var help = new TextBlock
        {
            Text = "그룹 = 스킨 칸: 같은 그룹의 스킨은 서로 갈아입히는 그림이고(한 번에 하나), 그룹들은 레이어 순서대로 겹칩니다. " +
                   "카드를 누르면 바로 적용 · 두 번 누르면 적용하고 창 닫기 · 우클릭 = 복제·이름·삭제·순서. \"▸ 부위별\"에서 부위 하나만 다른 스킨을 입히거나, 그 부위에만 있는 개별 스킨(\"＋ 이 부위 스킨\")을 만들 수 있습니다.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.DimTextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var previewTitle = Ui.Label("전체 착장 (지금 프레임)", bold: true);
        previewTitle.Margin = new Thickness(0, 0, 0, 2);
        _filter.SelectedIndex = 0;
        _filter.SetTip(0, "그룹 스킨과 부위 전용(개별) 스킨을 모두 보기")
            .SetTip(1, "그룹 전체 스킨만 보기(부위 줄에는 \"없음\" + 그룹 스킨)")
            .SetTip(2, "부위 하나에만 있는 개별 스킨만 보기(부위 줄을 모두 펼침)");
        _filter.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        var filterLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4), Children = { Ui.Label("보기"), _filter } };
        var content = new StackPanel { Margin = new Thickness(10, 8), Children = { filterLine, help, previewTitle, _preview, _outfits, _list } };
        // 아래 확인 단추(Enter·Esc도 닫기): 카드를 누를 때마다 이미 적용되므로 창만 닫는다.
        var ok = new Button { Content = "확인", MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close();
        var footer = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 6),
            Child = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } },
        };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(Ui.ScrollHost(content));
        Content = root;

        _timer.Tick += (_, _) =>
        {
            // 그리는 중에는 미룬다(획이 끝나면 다음 틱에 마지막으로 한 번 다시 만든다).
            if (_ctx.Project is { IsStrokeActive: true }) return;
            _timer.Stop();
            Rebuild();
        };
        _ctx.ProjectChanged += OnProjectChanged;
        _ctx.CurrentChanged += OnCurrentChanged;
        _ctx.ProjectReplaced += OnProjectReplaced;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    /// <summary>
    /// 스킨 관리 창을 연다(이미 열려 있으면 앞으로). <paramref name="focus"/>가 그룹/부위면 그 줄을 강조하고 그 줄로 스크롤한다
    /// (부위면 그 그룹의 부위 줄을 펼침).
    /// </summary>
    public static void Open(EditorContext ctx, Visual? owner, Layer? focus = null)
    {
        var w = _instance;
        if (w is null)
        {
            w = new SkinManagerWindow(ctx);
            _instance = w;
            w.Closed += (_, _) => { if (ReferenceEquals(_instance, w)) _instance = null; };
            w.SetFocus(focus);
            w.Rebuild();
            if ((Ui.MainWindow ?? Ui.Owner(owner)) is { } main && !ReferenceEquals(main, w)) w.Show(main);
            else w.Show();
            return;
        }
        w.SetFocus(focus);
        w.Rebuild();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    private void SetFocus(Layer? focus)
    {
        _focus = focus;
        _scrollToFocus = focus is not null;
        if (focus is { IsPart: true, Parent: { IsGroup: true } g }) _expanded.Add(g);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _ctx.ProjectChanged -= OnProjectChanged;
        _ctx.CurrentChanged -= OnCurrentChanged;
        _ctx.ProjectReplaced -= OnProjectReplaced;
        _preview.Picture = null;
        _outfits.Children.Clear();
        _list.Children.Clear();
        DisposeBitmaps();
        base.OnClosed(e);
    }

    // ───────────── 다시 만들기 ─────────────

    private int _builtFrame = -1;

    private void Schedule()
    {
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        // 착장 목록 편집은 Layers | Settings로 온다(Settings만 와도 다시 만듦).
        if ((e.Change & (ProjectChange.Layers | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Canvas | ProjectChange.Settings)) != 0) Schedule();
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        if (_ctx.CurrentFrame != _builtFrame) Schedule();
    }

    private void OnProjectReplaced(object? sender, EventArgs e)
    {
        _expanded.Clear();
        _focus = null;
        _outfitWarning = null;
        _scrollToFocus = false;
        Schedule();
    }

    private void DisposeBitmaps()
    {
        foreach (var b in _bitmaps) b.Dispose();
        _bitmaps.Clear();
    }

    private WriteableBitmap Track(PixelImage image)
    {
        var b = ImageUtil.ToBitmap(image);
        _bitmaps.Add(b);
        return b;
    }

    private static bool HasContent(PixelImage? image) => image is not null && !image.IsRegionEmpty(image.Bounds);

    private void Rebuild()
    {
        _timer.Stop();
        _preview.Picture = null;
        _list.Children.Clear();
        _outfits.Children.Clear();
        DisposeBitmaps();
        if (_ctx.Project is not { } p || p.Frames.Count == 0)
        {
            _builtFrame = -1;
            _preview.IsVisible = false;
            _outfits.IsVisible = false;
            _list.Children.Add(Note("열린 프로젝트가 없습니다."));
            return;
        }
        int frame = Math.Clamp(_ctx.CurrentFrame, 0, p.Frames.Count - 1);
        _builtFrame = _ctx.CurrentFrame;
        _preview.IsVisible = true;
        _preview.Source = p.Size;
        _preview.Picture = Track(p.Flatten(frame));

        var groups = p.Layers.Where(l => l.IsGroup).Reverse().ToList();
        _outfits.IsVisible = groups.Count > 0 || p.Outfits.Count > 0;
        if (_outfits.IsVisible) _outfits.Children.Add(OutfitSection(p, frame));
        if (groups.Count == 0)
        {
            _list.Children.Add(Note("그룹이 없습니다 — 새 그룹(Ctrl+Shift+N)을 만들고 부위를 넣으세요."));
            return;
        }
        if (_focus is not null && !p.Layers.Contains(_focus)) _focus = null;
        Control? focusRow = null;
        foreach (var group in groups)
        {
            var row = GroupRow(p, group, frame, ref focusRow);
            _list.Children.Add(row);
        }
        if (_scrollToFocus && focusRow is not null)
        {
            _scrollToFocus = false;
            var target = focusRow;
            Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = AppTheme.DimTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 8),
    };

    // ───────────── 착장 프리셋 ─────────────

    /// <summary>착장 프리셋 묶음: 제목 + "지금 착장 저장…", 건너뛴 항목 안내, 착장 카드(누르면 적용, 우클릭 = 메뉴).</summary>
    private Control OutfitSection(SpriteProject p, int frame)
    {
        var save = new Button { Content = "지금 착장 저장…", Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center }
            .Tip("그룹마다 지금 스킨과 부위별 스킨을 착장 프리셋으로 저장");
        save.Click += (_, _) => Dispatcher.UIThread.Post(SaveOutfit);
        var title = new TextBlock { Text = "착장 프리셋", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(save, Dock.Right);
        head.Children.Add(save);
        head.Children.Add(title);

        var body = new StackPanel { Children = { head } };
        if (_outfitWarning is { } warning)
        {
            var line = new TextBlock
            {
                Text = warning + " (눌러서 닫기)",
                Foreground = AppTheme.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 4),
                Cursor = HandCursor,
            };
            line.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _outfitWarning = null;
                Dispatcher.UIThread.Post(Rebuild);
            };
            body.Children.Add(line);
        }
        var outfits = p.Outfits;
        if (outfits.Count == 0)
        {
            var hint = Note("지금 착장을 저장해 두면 클릭 한 번으로 바꾸고, 내보내기 설정의 \"스킨 고르기\"에서 체크하면 착장마다 프레임 시트를 만듭니다.");
            hint.Margin = new Thickness(2, 2);
            body.Children.Add(hint);
        }
        else
        {
            var cards = new WrapPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < outfits.Count; i++)
            {
                var outfit = outfits[i];
                var missing = p.MissingOutfitEntries(outfit);
                var bmp = Track(p.FlattenOutfit(frame, outfit, null));
                var card = Card(bmp, p.Size, GroupThumb, outfit.Name, IsCurrentOutfit(p, outfit), missing.Count > 0 ? "!" : null);
                card.Tip(missing.Count == 0
                    ? $"\"{outfit.Name}\" 착장 입히기 · 우클릭 = 메뉴"
                    : $"\"{outfit.Name}\" 착장 입히기 · 우클릭 = 메뉴\n쓸 수 없는 항목(건너뜀):\n" + string.Join("\n", missing.Select(m => "· " + m)));
                int index = i;
                Clickable(card, () => ApplyOutfit(outfit), () => OutfitCardMenu(outfit, index));
                cards.Children.Add(card);
            }
            body.Children.Add(cards);
        }
        return new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6),
            Child = body,
        };
    }

    /// <summary>지금 착장이 이 착장 프리셋과 같은지(<see cref="SpriteProject.ApplyOutfit"/>가 바꿀 것이 없으면 같음).</summary>
    public static bool IsCurrentOutfit(SpriteProject p, Outfit outfit)
    {
        bool any = false;
        foreach (var (group, skin) in outfit.Slots)
        {
            if (!p.Layers.Contains(group)) continue;
            any = true;
            if (group.Skins.Contains(skin) && !ReferenceEquals(group.ActiveSkin, skin)) return false;
            foreach (var part in p.ChildrenOf(group))
            {
                Skin? over = null;
                foreach (var e in outfit.Parts)
                    if (ReferenceEquals(e.Part, part) && part.SkinChoices.Contains(e.Skin)) over = e.Skin;
                if (!ReferenceEquals(part.SkinOverride, over)) return false;
            }
        }
        return any;
    }

    private void ApplyOutfit(Outfit outfit)
    {
        if (_ctx.Project is not { } p || !p.Outfits.Contains(outfit)) return;
        IReadOnlyList<string> missing = [];
        _ctx.Structural(() => missing = p.ApplyOutfit(outfit));
        _outfitWarning = missing.Count == 0 ? null : $"\"{outfit.Name}\" 착장에서 건너뛴 항목: {string.Join(", ", missing)}";
        Schedule();
    }

    private void SaveOutfit()
    {
        if (_ctx.Project is not { } p) return;
        var name = InputDialog.Ask(this, "착장 저장", "지금 착장(그룹마다 스킨 + 부위별 스킨)을 저장할 이름", p.UniqueOutfitName("착장"));
        if (name is null || _ctx.Project != p) return;
        _ctx.Structural(() => p.SaveOutfit(name));
    }

    private void RenameOutfit(Outfit outfit)
    {
        if (_ctx.Project is not { } p) return;
        var name = InputDialog.Ask(this, "착장 이름 바꾸기", "착장 이름", outfit.Name);
        if (name is null || !p.Outfits.Contains(outfit)) return;
        try
        {
            _ctx.Structural(() => p.RenameOutfit(outfit, name));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, ex.ParamName is { } param ? ex.Message.Replace($" (Parameter '{param}')", "") : ex.Message);
        }
    }

    private Control[] OutfitCardMenu(Outfit outfit, int index)
    {
        int count = _ctx.Project?.Outfits.Count ?? 0;
        void Run(Action<SpriteProject> action)
        {
            if (_ctx.Project is { } p && p.Outfits.Contains(outfit)) _ctx.Structural(() => action(p));
        }
        return
        [
            Item("적용", () => ApplyOutfit(outfit)),
            Item("지금 착장으로 덮어쓰기", () => Run(p => p.OverwriteOutfit(outfit))),
            Item("이름 바꾸기…", () => RenameOutfit(outfit)),
            Item("삭제", () => Run(p => p.DeleteOutfit(outfit))),
            Item("앞으로", () => Run(p => p.MoveOutfit(index, index - 1)), index > 0),
            Item("뒤로", () => Run(p => p.MoveOutfit(index, index + 1)), index < count - 1),
        ];
    }

    /// <summary>그룹 한 줄: 머리(펼치기 · 이름 · 부위 재정의 되돌리기) + 스킨 카드 + 새 스킨/복제, 펼쳤으면 부위 줄("개별 스킨" 거르기면 부위 줄만, 늘 펼침).</summary>
    private Control GroupRow(SpriteProject p, Layer group, int frame, ref Control? focusRow)
    {
        var parts = p.ChildrenOf(group).Reverse().ToList();
        int overrides = parts.Count(x => x.SkinOverride is not null);
        int partOnly = group.AllSkins.Count(s => s.IsPartOnly);
        bool expanded = _expanded.Contains(group) || (!ShowFull && parts.Count > 0);

        var toggle = new Button
        {
            Content = $"{(expanded ? "▾" : "▸")} 부위별 ({parts.Count}{(partOnly > 0 ? $" · 개별 {partOnly}" : "")})",
            Padding = new Thickness(6, 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = parts.Count > 0 && ShowFull,
        }.Tip("부위마다 따로 스킨 고르기 · 부위 전용(개별) 스킨");
        toggle.Click += (_, _) =>
        {
            if (!_expanded.Remove(group)) _expanded.Add(group);
            Dispatcher.UIThread.Post(Rebuild);
        };
        var name = new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeight.Bold,
            Foreground = group.Visible ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(toggle, Dock.Left);
        head.Children.Add(toggle);
        if (overrides > 0)
        {
            var reset = new Button { Content = $"모든 부위를 그룹 스킨으로 ({overrides})", Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center }
                .Tip("부위마다 따로 고른 스킨을 지우고 모두 그룹 스킨을 따르게 합니다");
            reset.Click += (_, _) => _ctx.ResetPartSkins(group);
            DockPanel.SetDock(reset, Dock.Right);
            head.Children.Add(reset);
        }
        head.Children.Add(name);

        var body = new StackPanel { Children = { head } };
        if (ShowFull)
        {
            // 카드는 한 줄(가로 스크롤)로 늘어놓아 끌어서 순서를 바꾼다(맨 앞 = 기본 스킨). 누르기만 하면 그 스킨을 입힌다.
            var cards = new StackPanel { Orientation = Orientation.Horizontal };
            var drag = new DragReorder(Orientation.Horizontal);
            drag.Moved += (from, to) => Dispatcher.UIThread.Post(() => SkinOps.MoveSkin(_ctx, group, from, to));
            var skins = group.Skins;
            for (int i = 0; i < skins.Count; i++)
            {
                var skin = skins[i];
                var bmp = Track(p.FlattenGroup(group, frame, skin));
                var card = Card(bmp, p.Size, GroupThumb, i == 0 ? $"{skin.Name} (기본)" : skin.Name, ReferenceEquals(skin, group.ActiveSkin), badge: null);
                card.Tip($"\"{group.Name}\" 그룹에 \"{skin.Name}\" 입히기 · 끌어서 순서 바꾸기(맨 앞 = 기본) · 우클릭 = 메뉴");
                int index = i;
                Clickable(card, () => _ctx.SetActiveSkin(group, skin), () => GroupCardMenu(group, skin, index), onRelease: true);
                drag.Attach(card);
                cards.Children.Add(card);
            }
            var add = Ui.Button("＋ 새 스킨", (_, _) => SkinOps.AddSkin(_ctx, group, null)).Tip("빈 스킨 추가(빈 셀은 기본 스킨 그림을 빌려 보임)");
            var dup = Ui.Button("복제", (_, _) => SkinOps.AddSkin(_ctx, group, group.ActiveSkin)).Tip("지금 스킨을 그림째 복제");
            add.HorizontalAlignment = dup.HorizontalAlignment = HorizontalAlignment.Stretch;
            // 단추는 카드 줄 끝에(등록하지 않은 줄이라 끄는 동안 제자리를 지킨다).
            cards.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), Children = { add, dup } });
            body.Children.Add(new ScrollViewer
            {
                Content = cards,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
        }
        else if (parts.Count == 0) body.Children.Add(Note("부위가 없습니다 — 개별 스킨은 부위마다 만듭니다."));
        if (expanded)
        {
            foreach (var part in parts)
            {
                var partRow = PartRow(p, group, part, frame);
                if (ReferenceEquals(part, _focus)) focusRow = partRow;
                body.Children.Add(partRow);
            }
        }
        var row = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = ReferenceEquals(group, _focus) ? AppTheme.AccentBrush : AppTheme.BorderBrush,
            BorderThickness = new Thickness(ReferenceEquals(group, _focus) ? 2 : 1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6),
            Child = body,
        };
        if (ReferenceEquals(group, _focus)) focusRow = row;
        return row;
    }

    /// <summary>
    /// 부위 한 줄: 이름 + "없음" 카드 + 그룹 스킨 카드 + 이 부위 전용(개별) 스킨 카드(이 스킨 자기 그림이 없어 기본 스킨 그림을 빌리면 "빌림") + "＋ 이 부위 스킨"/"복제".
    /// 거르기에 따라 그룹 스킨 쪽("없음" 포함)이나 개별 스킨 쪽만.
    /// </summary>
    private Control PartRow(SpriteProject p, Layer group, Layer part, int frame)
    {
        var f = p.Frames[frame];
        var size = part.PartSize;
        var own = part.SkinOverride;
        var baseSkin = group.BaseSkin;
        bool baseHas = baseSkin is not null && HasContent(p.GetOwnCel(part, f, baseSkin));

        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        WriteableBitmap? Cel(Skin skin) => p.GetSkinCel(part, f, skin) is { } cel && HasContent(cel) ? Track(cel) : null;

        if (ShowFull)
        {
            // 첫 카드 "없음" = 이 부위만 따로 입힌 스킨 없음(그룹 스킨을 그대로 보임) — 그림 대신 금지 표시.
            var follow = Card(null, size, PartThumb, "없음", own is null, badge: null, none: true);
            follow.Tip($"부위만 따로 입힌 스킨 없음 — 그룹 스킨(\"{group.ActiveSkin?.Name}\")을 그대로 보임");
            Clickable(follow, () => _ctx.SetPartSkin(part, null), () => PartCardMenu(group, part, null));
            cards.Children.Add(follow);
            var skins = group.Skins;
            for (int i = 0; i < skins.Count; i++)
            {
                var skin = skins[i];
                bool borrowed = i > 0 && baseHas && !HasContent(p.GetOwnCel(part, f, skin));
                var card = Card(Cel(skin), size, PartThumb, i == 0 ? $"{skin.Name} (기본)" : skin.Name, ReferenceEquals(skin, own), borrowed ? "빌림" : null);
                card.Tip(borrowed
                    ? $"\"{part.Name}\"만 \"{skin.Name}\" 입히기 — 이 스킨에 자기 그림이 없어 \"{baseSkin!.Name}\" 그림을 빌려 보임"
                    : $"\"{part.Name}\"만 \"{skin.Name}\" 입히기");
                Clickable(card, () => _ctx.SetPartSkin(part, skin), () => PartCardMenu(group, part, skin));
                cards.Children.Add(card);
            }
        }
        if (ShowPartOnly)
        {
            var ownSkins = part.OwnSkins;
            for (int i = 0; i < ownSkins.Count; i++)
            {
                var skin = ownSkins[i];
                bool borrowed = baseHas && !HasContent(p.GetOwnCel(part, f, skin));
                var card = Card(Cel(skin), size, PartThumb, skin.Name, ReferenceEquals(skin, own), borrowed ? "빌림" : null, partOnly: true);
                card.Tip(($"\"{part.Name}\" 전용(개별) 스킨 \"{skin.Name}\" 입히기 — 다른 부위·그룹 목록에는 없습니다" +
                          (borrowed ? $"\n이 스킨에 자기 그림이 없어 \"{baseSkin!.Name}\" 그림을 빌려 보임" : "")) + " · 우클릭 = 메뉴");
                int index = i, count = ownSkins.Count;
                Clickable(card, () => _ctx.SetPartSkin(part, skin), () => PartOnlyCardMenu(group, part, skin, index, count));
                cards.Children.Add(card);
            }
            var add = Ui.Button("＋ 이 부위 스킨", (_, _) => SkinOps.AddPartSkin(_ctx, this, part, null, prompt: true))
                .Tip($"\"{part.Name}\"에만 있는 새 스킨(개별 스킨) 추가 — 빈 칸은 기본 스킨 그림을 빌려 보임");
            var dup = Ui.Button("복제", (_, _) => SkinOps.AddPartSkin(_ctx, this, part, part.ShownSkin, prompt: true))
                .Tip($"\"{part.Name}\"에 지금 보이는 스킨(\"{part.ShownSkin?.Name}\") 그림을 복제해 이 부위 전용 스킨으로");
            add.HorizontalAlignment = dup.HorizontalAlignment = HorizontalAlignment.Stretch;
            cards.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), Children = { add, dup } });
        }

        bool focused = ReferenceEquals(part, _focus);
        var label = new TextBlock
        {
            Text = part.Name,
            Foreground = part.Visible ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            FontWeight = focused ? FontWeight.Bold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0),
        }.Tip(own is null ? $"{part.Name}: 그룹 스킨을 따름" : $"{part.Name}: 부위만 따로 \"{own.Name}\"");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
        grid.Children.Add(label);
        Grid.SetColumn(cards, 1);
        grid.Children.Add(cards);
        return new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(2),
            BorderBrush = focused ? AppTheme.AccentBrush : AppTheme.BorderBrush,
            BorderThickness = new Thickness(focused ? 2 : 0, focused ? 2 : 1, focused ? 2 : 0, focused ? 2 : 0),
            CornerRadius = new CornerRadius(focused ? 3 : 0),
            Child = grid,
        };
    }

    /// <summary>썸네일 + 이름(+ 작은 표시, 부위 전용이면 왼쪽 위 "개별") 카드. 고른 카드는 강조 테두리.</summary>
    private static Border Card(Bitmap? bmp, Size source, int thumb, string title, bool selected, string? badge, bool none = false, bool partOnly = false)
    {
        var image = new SkinThumb(bmp, source) { Width = thumb, Height = thumb, ShowNone = none };
        var panel = new Panel { Children = { image } };
        if (badge is not null)
        {
            panel.Children.Add(new Border
            {
                Background = AppTheme.WarningBrush,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                Child = new TextBlock { Text = badge, FontSize = AppTheme.SmallFontSize - 1, Foreground = Brushes.Black },
            });
        }
        if (partOnly)
        {
            var mark = SkinPickerWindow.Badge();
            mark.HorizontalAlignment = HorizontalAlignment.Left;
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(2, 2, 0, 0);
            panel.Children.Add(mark);
        }
        var text = new TextBlock
        {
            Text = title,
            FontSize = AppTheme.SmallFontSize,
            Foreground = selected ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            FontWeight = selected ? FontWeight.Bold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = thumb + 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
        return new Border
        {
            Width = thumb + 12,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(selected ? 3 : 4),
            Background = selected ? AppTheme.HoverBrush : AppTheme.PanelBrush,
            BorderBrush = selected ? AppTheme.AccentBrush : AppTheme.BorderBrush,
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(3),
            Cursor = HandCursor,
            Child = new StackPanel { Children = { panel, text } },
        };
    }

    /// <summary>왼쪽 클릭 = <paramref name="apply"/>, 두 번 누르기 = 적용하고 창 닫기, 오른쪽 클릭 = 메뉴.</summary>
    /// <param name="onRelease">끌어서 순서를 바꾸는 카드: 누를 때가 아니라 끌지 않고 놓을 때 입힌다(끌면 <see cref="DragReorder"/>가 놓기를 처리함).</param>
    private void Clickable(Control card, Action apply, Func<Control[]> menu, bool onRelease = false)
    {
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (e.ClickCount >= 2)
            {
                Dispatcher.UIThread.Post(() => { apply(); Close(); });
                return;
            }
            // 다시 만들기가 이 카드를 지우므로 처리기가 끝난 뒤에 적용한다.
            if (!onRelease) Dispatcher.UIThread.Post(apply);
        };
        if (onRelease)
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || !card.Bounds.WithX(0).WithY(0).Contains(e.GetPosition(card))) return;
                e.Handled = true;
                Dispatcher.UIThread.Post(apply);
            };
        PopupMenus.Attach(card, menu);
    }

    private static MenuItem Item(string text, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = new TextBlock { Text = text }, IsEnabled = enabled };
        // 메뉴가 닫힌 뒤 실행해 대화상자가 메뉴와 겹치지 않게 한다.
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return item;
    }

    private Control[] GroupCardMenu(Layer group, Skin skin, int index)
    {
        int count = group.Skins.Count;
        bool anyOverride = _ctx.Project is { } p && p.ChildrenOf(group).Any(x => x.SkinOverride is not null);
        return
        [
            Item("적용", () => _ctx.SetActiveSkin(group, skin)),
            Item("복제", () => SkinOps.AddSkin(_ctx, group, skin)),
            Item("이름 바꾸기…", () => SkinOps.RenameSkin(_ctx, this, group, skin)),
            Item("삭제", () => SkinOps.DeleteSkin(_ctx, this, group, skin), count > 1),
            Item("앞으로", () => SkinOps.MoveSkin(_ctx, group, index, index - 1), index > 0),
            Item("뒤로", () => SkinOps.MoveSkin(_ctx, group, index, index + 1), index < count - 1),
            new Separator(),
            Item("모든 부위를 그룹 스킨으로", () => _ctx.ResetPartSkins(group), anyOverride),
        ];
    }

    private Control[] PartCardMenu(Layer group, Layer part, Skin? skin)
    {
        var items = new List<Control>
        {
            Item(skin is null ? "그룹 스킨 따르기" : $"\"{part.Name}\"에만 적용", () => _ctx.SetPartSkin(part, skin)),
            Item("그룹 전체에 적용", () => _ctx.SetActiveSkin(group, skin ?? group.ActiveSkin!), skin is not null),
            new Separator(),
            Item("모든 부위를 그룹 스킨으로", () => _ctx.ResetPartSkins(group),
                _ctx.Project is { } p && p.ChildrenOf(group).Any(x => x.SkinOverride is not null)),
        };
        if (skin is not null)
        {
            items.Add(new Separator());
            items.Add(Item($"\"{part.Name}\" 전용으로 내리기…", () => SkinOps.DemoteSkin(_ctx, this, group, skin, part),
                !ReferenceEquals(skin, group.BaseSkin) && group.Skins.Count > 1));
        }
        return [.. items];
    }

    /// <summary>부위 전용(개별) 스킨 카드 메뉴(<paramref name="index"/>/<paramref name="count"/> = 그 부위 전용 스킨 안 순서).</summary>
    private Control[] PartOnlyCardMenu(Layer group, Layer part, Skin skin, int index, int count) =>
    [
        Item("적용", () => _ctx.SetPartSkin(part, skin)),
        Item("그룹 스킨 따르기", () => _ctx.SetPartSkin(part, null), ReferenceEquals(part.SkinOverride, skin)),
        Item("복제", () => SkinOps.AddPartSkin(_ctx, this, part, skin, prompt: false)),
        Item("이름 바꾸기…", () => SkinOps.RenameSkin(_ctx, this, group, skin)),
        Item("삭제", () => SkinOps.DeleteSkin(_ctx, this, group, skin)),
        Item("앞으로", () => SkinOps.MoveSkinBy(_ctx, group, skin, -1), index > 0),
        Item("뒤로", () => SkinOps.MoveSkinBy(_ctx, group, skin, 1), index < count - 1),
        new Separator(),
        Item("전체 스킨으로 올리기", () => SkinOps.PromoteSkin(_ctx, group, skin)),
    ];

    // ───────────── 썸네일 ─────────────

    /// <summary>체커 위에 비트맵을 정수 배율(최근접)로 가운데 맞춰 그린다. 비트맵이 없으면 원본 비율의 체커만.</summary>
    private sealed class SkinThumb(Bitmap? bitmap, Size source) : Control
    {
        private Bitmap? _bitmap = bitmap;

        public Size Source { get; set; } = source;

        /// <summary>그림 대신 회색 금지 표시(원 + 사선): "없음" 카드(부위만 따로 입힌 스킨 없음).</summary>
        public bool ShowNone { get; init; }

        public Bitmap? Picture
        {
            get => _bitmap;
            set { _bitmap = value; InvalidateVisual(); }
        }

        public override void Render(DrawingContext g)
        {
            var area = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.InputBrush, area);
            var inner = area.Deflate(2);
            if (ShowNone)
            {
                double r = Math.Min(inner.Width, inner.Height) * 0.32;
                var c = inner.Center;
                var pen = new Pen(AppTheme.DimTextBrush, Math.Max(2, r * 0.18));
                g.DrawEllipse(null, pen, c, r, r);
                double d = r * Math.Sqrt(0.5);
                g.DrawLine(pen, new Avalonia.Point(c.X - d, c.Y - d), new Avalonia.Point(c.X + d, c.Y + d));
                return;
            }
            if (Source.Width <= 0 || Source.Height <= 0 || inner.Width <= 0 || inner.Height <= 0) return;
            var src = new Rectangle(0, 0, Source.Width, Source.Height);
            if (_bitmap is not null)
            {
                ImageUtil.DrawFitted(g, _bitmap, src, inner, checker: true);
                return;
            }
            double fit = Math.Min(inner.Width / Source.Width, inner.Height / Source.Height);
            double scale = fit >= 1 ? Math.Floor(fit) : fit;
            double w = Math.Max(1, Math.Floor(Source.Width * scale)), h = Math.Max(1, Math.Floor(Source.Height * scale));
            var dest = new Rect(Math.Floor(inner.X + (inner.Width - w) / 2), Math.Floor(inner.Y + (inner.Height - h) / 2), w, h);
            AppTheme.DrawChecker(g, dest, Math.Max(2, Math.Min(8, (int)scale * 2)));
        }
    }

    // ───────────── 상태 글 ─────────────

    /// <summary>
    /// 레이어의 스킨 상태 한 줄(그룹·부위가 아니면 ""): "스킨: knight", 부위면 "스킨: knight · 부위만 따로",
    /// 지금 셀이 첫 스킨 그림을 빌려 보이면 "(Head는 기본에서 빌림)"을 붙인다.
    /// </summary>
    public static string SkinStatus(SpriteProject p, Layer layer, int frameIndex)
    {
        if (layer.ShownSkin is not { } shown) return "";
        var text = $"스킨: {shown.Name}";
        if (!layer.IsPart) return text;
        if (layer.SkinOverride is { } over) text += over.IsPartOnly ? " · 이 부위 전용(개별)" : " · 부위만 따로";
        if (frameIndex >= 0 && frameIndex < p.Frames.Count && layer.Parent?.BaseSkin is { } baseSkin && p.IsCelInherited(layer, p.Frames[frameIndex]))
            text += $" ({layer.Name}{Topic(layer.Name)} {baseSkin.Name}에서 빌림)";
        return text;
    }

    /// <summary>이름 뒤 보조사(받침 있는 한글로 끝나면 "은", 아니면 "는").</summary>
    private static string Topic(string name)
    {
        char c = name.Length > 0 ? name[^1] : ' ';
        return c is >= '가' and <= '힣' && (c - '가') % 28 != 0 ? "은" : "는";
    }
}

/// <summary>스킨 편집(실행취소됨) 공용 동작: 타임라인 메뉴와 스킨 관리 창이 함께 쓴다. 오류는 대화상자로 알린다.</summary>
public static class SkinOps
{
    /// <summary>그룹에 스킨을 더한다(<paramref name="copyFrom"/>이 있으면 그 스킨 그림 복제). 새 스킨이 지금 스킨이 된다.</summary>
    public static void AddSkin(EditorContext ctx, Layer group, Skin? copyFrom)
    {
        if (ctx.Project is not { } p) return;
        ctx.Structural(() => p.AddSkin(group, null, copyFrom));
    }

    public static void RenameSkin(EditorContext ctx, Visual owner, Layer group, Skin skin)
    {
        if (ctx.Project is not { } p) return;
        var prompt = skin.Owner is { } part ? $"\"{part.Name}\" 부위 전용 스킨 이름" : $"\"{group.Name}\" 그룹의 스킨 이름";
        var name = InputDialog.Ask(owner, "스킨 이름 바꾸기", prompt, skin.Name);
        if (name is null) return;
        try
        {
            ctx.Structural(() => p.RenameSkin(group, skin, name));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(owner, Message(ex));
        }
    }

    public static void DeleteSkin(EditorContext ctx, Visual owner, Layer group, Skin skin)
    {
        if (ctx.Project is not { } p) return;
        try
        {
            ctx.Structural(() => p.DeleteSkin(group, skin));
        }
        catch (InvalidOperationException ex)
        {
            Ui.Info(owner, ex.Message);
        }
    }

    public static void MoveSkin(EditorContext ctx, Layer group, int from, int to)
    {
        if (ctx.Project is not { } p || to < 0 || to >= group.Skins.Count) return;
        ctx.Structural(() => p.MoveSkin(group, from, to));
    }

    /// <summary>스킨을 같은 목록 안에서 앞(−)/뒤(+)로(전체 스킨은 전체 스킨끼리, 부위 전용 스킨은 같은 부위 전용끼리).</summary>
    public static void MoveSkinBy(EditorContext ctx, Layer group, Skin skin, int delta)
    {
        if (ctx.Project is not { } p) return;
        ctx.Structural(() => p.MoveSkin(group, skin, delta));
    }

    /// <summary>
    /// 부위 하나에만 있는 스킨(부위 전용·개별 스킨)을 만들고 그 부위에 입힌다. <paramref name="copyFrom"/>이 있으면 그 부위의 그 스킨 그림 복제.
    /// <paramref name="prompt"/>면 이름을 묻는다(제안: "{부위} 스킨" / "{스킨} 복사", 그룹 안에서 겹치지 않게).
    /// </summary>
    public static void AddPartSkin(EditorContext ctx, Visual owner, Layer part, Skin? copyFrom, bool prompt)
    {
        if (ctx.Project is not { } p || part.Parent is not { IsGroup: true } group) return;
        string? name = null;
        if (prompt)
        {
            var suggested = p.UniqueSkinName(group, copyFrom is null ? part.Name + " 스킨" : copyFrom.Name + " 복사");
            name = InputDialog.Ask(owner, copyFrom is null ? "부위 스킨 추가" : "부위 스킨 복제",
                copyFrom is null
                    ? $"\"{part.Name}\"에만 있는 새 스킨 이름(빈 칸은 기본 스킨 그림을 빌려 보임)"
                    : $"\"{part.Name}\"의 \"{copyFrom.Name}\" 그림을 복제한 부위 전용 스킨 이름",
                suggested);
            if (name is null || !ReferenceEquals(ctx.Project, p)) return;
        }
        try
        {
            ctx.Structural(() => p.AddPartSkin(part, name, copyFrom));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(owner, Message(ex));
        }
    }

    /// <summary>부위 전용 스킨을 그룹 전체 스킨으로 올린다(다른 부위에서도 고를 수 있게).</summary>
    public static void PromoteSkin(EditorContext ctx, Layer group, Skin skin)
    {
        if (ctx.Project is not { } p) return;
        ctx.Structural(() => p.PromoteSkin(group, skin));
    }

    /// <summary>전체 스킨을 <paramref name="part"/> 전용으로 내린다(다른 부위의 그 스킨 그림이 지워지므로 확인, 실행취소됨).</summary>
    public static void DemoteSkin(EditorContext ctx, Visual owner, Layer group, Skin skin, Layer part)
    {
        if (ctx.Project is not { } p) return;
        int drawn = p.ChildrenOf(group).Count(other => !ReferenceEquals(other, part)
            && p.Frames.Any(f => p.GetOwnCel(other, f, skin) is { } cel && !cel.IsRegionEmpty(cel.Bounds)));
        var message = $"\"{skin.Name}\" 스킨을 \"{part.Name}\" 부위 전용(개별) 스킨으로 내립니다 — 다른 부위와 그룹 목록에서는 사라집니다." +
                      (drawn > 0 ? $"\n다른 부위 {drawn}개에 그린 \"{skin.Name}\" 그림은 지워집니다(실행취소로 되돌릴 수 있음)." : "") + "\n계속할까요?";
        if (Ui.Ask(owner, message, "부위 전용으로 내리기", MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        try
        {
            ctx.Structural(() => p.DemoteSkin(group, skin, part));
        }
        catch (InvalidOperationException ex)
        {
            Ui.Info(owner, ex.Message);
        }
    }

    /// <summary>예외 글에서 " (Parameter '…')"를 뗀다.</summary>
    private static string Message(ArgumentException ex)
        => ex.ParamName is { } param ? ex.Message.Replace($" (Parameter '{param}')", "") : ex.Message;
}
