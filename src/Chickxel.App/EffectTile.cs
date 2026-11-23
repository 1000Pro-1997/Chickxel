using Chickxel.Core.Assets;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 효과 종류 상자(효과 창 왼쪽 칸과 효과 목록 패널이 같이 씀): 미리보기 그림(같은 정수 배율, 작으면 맞춤 축소 — 움직이는 효과는 <see cref="AssetPreview.Steps"/>칸) + 이름.
/// 미리보기는 그 효과가 선언한 미리보기 병아리(<see cref="PreviewChicks"/> — 패키지 미리보기와 같음, 없으면 기본 병아리)에 입힌 모습이라 지금 그림과 상관없고,
/// 에셋 상자는 그린 칸을 카탈로그 항목마다 앱을 켜 둔 동안 간직해 효과 창·효과 목록 패널을 다시 열어도 다시 그리지 않는다(에셋이 바뀌면 카탈로그 항목이 새로 생겨 다시 그림).
/// 보통은 위 = 미리보기, 아래 = 이름인 네모 상자이고, <see cref="Compact"/>면 왼쪽 = 작은 미리보기, 오른쪽 = 이름인 한 줄이다.
/// </summary>
internal sealed class EffectTile : Control, IDisposable
{
    /// <summary>카탈로그 항목마다 그린 미리보기 칸(항목이 없어지면 같이 없어짐).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AssetEntry, WriteableBitmap?[]> s_rendered = new();
    /// <summary>병아리 ID → 병아리 그림(읽기 전용으로만 씀).</summary>
    private static readonly Dictionary<string, PixelImage> s_chicks = new();

    private readonly WriteableBitmap?[] _frames;
    /// <summary>미리보기 병아리 ID(에셋 선언의 <c>previewChick</c>).</summary>
    private readonly string _chick;
    private int _shown;
    private bool _hover, _helpHover;

    public EffectTile(string name, IReadOnlyList<LayerEffect> effects, AssetEntry? entry = null, EffectRecipe? template = null)
    {
        Title = name;
        Key = entry?.Id ?? name;
        Effects = effects;
        Entry = entry;
        Template = template;
        // 스크립트 표시는 공식 효과가 아닌 원문이 든 상자에만(공식 효과는 예전 기본 효과 상자처럼 보임)
        _codeMark = template?.Scripts.Keys.Any(hash => !OfficialEffects.IsOfficialHash(hash)) == true;
        _chick = entry?.Script?.PreviewChick ?? template?.PreviewChick ?? PreviewChicks.Default;
        int steps = effects.Any(effect => Moves(effect, entry)) ? AssetPreview.Steps : 1;
        _frames = entry is null ? new WriteableBitmap?[steps] : s_rendered.GetValue(entry, _ => new WriteableBitmap?[steps]);
        if (entry?.Store is not null && entry.Author is { } author) LoadAvatar(author);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private readonly bool _codeMark;
    private Bitmap? _avatar;
    private bool _disposed;

    private async void LoadAvatar(AssetAuthor author)
    {
        var bitmap = await AuthorAvatarView.LoadAsync(author);
        if (_disposed) return;
        _avatar = bitmap;
        InvalidateVisual();
    }

    /// <summary>
    /// 효과가 진행에 따라 바뀌는지: 움직이는 그라데이션, 선언이 <c>animated</c>인 스크립트(에셋의 선언, 없으면 실행할 수 있는 원문 — 모르면 움직인다고 봄),
    /// 다른 움직이는 효과.
    /// </summary>
    private static bool Moves(LayerEffect effect, AssetEntry? entry)
        => effect.ColorGradients.Values.Any(gradient => gradient.Animated) || effect switch
        {
            ScriptEffect script => entry?.Script is { } asset && asset.Hash == script.ScriptHash ? asset.Animated
                : ScriptLibrary.Runnable(script.ScriptHash)?.Animated ?? true,
            AnimatedEffect => true,
            _ => false,
        };

    public string Title { get; }
    /// <summary>효과 설명(물음표 창의 글). 없으면 물음표를 그리지 않는다.</summary>
    public string? Description { get; set; }
    public IReadOnlyList<LayerEffect> Effects { get; }
    /// <summary>카탈로그 에셋 상자의 항목(앱에 든 효과 종류 상자는 null).</summary>
    public AssetEntry? Entry { get; }
    /// <summary>넣을 레시피(에셋 항목의 <see cref="AssetEntry.EffectTemplate"/> — 스크립트 원문을 함께 복사함, 앱에 든 효과 종류 상자는 null).</summary>
    public EffectRecipe? Template { get; }
    public bool Animated => _frames.Length > 1;
    /// <summary>즐겨찾기 열쇠: 에셋 상자는 에셋 ID, 앱에 든 효과는 <c>effect:</c> + 종류 ID(<see cref="EffectTileCatalog.Create"/>).</summary>
    public string Key { get; init; }

    /// <summary>즐겨찾기한 상자(한 줄 모양이면 이름 앞에 별).</summary>
    public bool Favorite
    {
        get => _favorite;
        set
        {
            if (_favorite == value) return;
            _favorite = value;
            InvalidateVisual();
        }
    }
    private bool _favorite;

    /// <summary>검색어 <paramref name="query"/>가 이름·설명·만든 사람·상점 이름 어딘가에 드는지(대소문자 무시, 빈 검색어는 늘 맞음).</summary>
    public bool Matches(string query)
    {
        if (query.Length == 0) return true;
        bool In(string? text) => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
        return In(Title) || In(Description) || In(Entry?.Author?.Name) || In(Entry?.Store?.DisplayName);
    }
    /// <summary>한 줄 모양(왼쪽 작은 미리보기, 오른쪽 이름·만든 사람) — 좁은 효과 목록 패널.</summary>
    public bool Compact { get; init; }

    /// <summary><paramref name="step"/>번째 모습을 보인다(그 칸을 처음 볼 때 한 번만 그림).</summary>
    public void Show(int step)
    {
        step = Math.Clamp(step, 0, _frames.Length - 1);
        _frames[step] ??= ImageUtil.ToBitmap(AssetPreview.Render(Effects, Chick(_chick), step));
        if (_shown == step) return;
        _shown = step;
        InvalidateVisual();
    }

    private static PixelImage Chick(string id)
    {
        if (!s_chicks.TryGetValue(id, out var image)) s_chicks[id] = image = PreviewChicks.Load(id);
        return image;
    }

    /// <summary>물음표 자리(상자 좌표): 상자는 오른쪽 위, 한 줄 모양은 오른쪽 가운데.</summary>
    private Rect HelpRect(Avalonia.Size size) => Compact ? new(size.Width - 19, Math.Round((size.Height - 16) / 2), 16, 16) : new(size.Width - 19, 3, 16, 16);

    /// <summary><paramref name="p"/>(상자 좌표)가 물음표 위인가(조금 넉넉히). 설명이 없으면 물음표가 없어 늘 false.</summary>
    public bool HelpHit(Avalonia.Point p) => Description is not null && HelpRect(Bounds.Size).Inflate(2).Contains(p);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        bool over = HelpHit(e.GetPosition(this));
        if (over == _helpHover) return;
        _helpHover = over;
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = _helpHover = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext g)
    {
        var b = new Rect(Bounds.Size);
        g.FillRectangle(_hover ? AppTheme.HoverBrush : AppTheme.PanelBrush, b);
        g.DrawRectangle(new Pen(AppTheme.BorderBrush, 1), b.Deflate(0.5));
        // 설명이 있으면 물음표(누르면 설명 창), 공식 효과가 아닌 스크립트 효과가 든 상자는 그 왼쪽(물음표가 없으면 그 자리)에 코드 표시 — 미리보기 위에 그린다
        var help = HelpRect(b.Size);
        double codeRight = Description is not null ? help.X - 3 : b.Width - 3;
        double markLeft = _codeMark ? codeRight - 17 : codeRight;
        if (Compact) RenderRow(g, b, markLeft);
        else RenderBox(g, b);
        if (Description is not null)
        {
            var helpBrush = _helpHover ? AppTheme.TextBrush : AppTheme.DimTextBrush;
            g.DrawRectangle(_helpHover ? AppTheme.PanelBrush : null, new Pen(helpBrush, 1), help.Deflate(0.5), 4, 4);
            VectorIcons.Draw(g, VectorIcon.QuestionMark, help, 11, helpBrush, 2.6);
        }
        if (_codeMark)
            VectorIcons.Draw(g, VectorIcon.Code, new Rect(codeRight - 14, help.Y, 14, 14), 12, AppTheme.DimTextBrush);
    }

    private void DrawThumb(DrawingContext g, Rect area)
    {
        if (_frames[_shown] is not { } bmp) return;
        var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
        double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
        ImageUtil.DrawFitted(g, bmp, src, area, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
    }

    /// <summary>만든 사람 줄: 상점 에셋이면 프로필 그림, 아니면 사람 아이콘 + 이름.</summary>
    private void DrawAuthor(DrawingContext g, AssetEntry entry, double x, double y, double right)
    {
        var icon = new Rect(x, y + 2, 13, 13);
        if (_avatar is { } avatar)
            g.DrawImage(avatar, new Rect(0, 0, avatar.PixelSize.Width, avatar.PixelSize.Height), icon);
        else
            VectorIcons.Draw(g, VectorIcon.CircleUser, icon, 12, AppTheme.DimTextBrush);
        if (entry.Author is { } author)
            AppTheme.DrawTextIn(g, author.Name, new Rect(x + 15, y, Math.Max(1, right - x - 15), 17), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
    }

    /// <summary>상자 모양: 위 = 미리보기, 아래 = 이름(에셋이면 그 아래 만든 사람).</summary>
    private void RenderBox(DrawingContext g, Rect b)
    {
        DrawThumb(g, new Rect(5, 5, b.Width - 10, b.Height - (Entry is null ? 28 : 45)));
        AppTheme.DrawTextIn(g, Title, new Rect(2, b.Height - (Entry is null ? 21 : 36), b.Width - 4, 18),
            AppTheme.TextBrush, AppTheme.SmallFontSize, center: true);
        if (Entry is { } entry) DrawAuthor(g, entry, 3, b.Height - 19, b.Width - 3);
    }

    /// <summary>한 줄 모양: 왼쪽 = 정사각 미리보기, 오른쪽 = 이름(에셋이면 그 아래 만든 사람). <paramref name="right"/> = 표시(물음표·코드) 왼쪽 끝.</summary>
    private void RenderRow(DrawingContext g, Rect b, double right)
    {
        double side = b.Height - 8;
        DrawThumb(g, new Rect(4, 4, side, side));
        double x = side + 10;
        if (_favorite)
        {
            // 이름 줄 앞 별(에셋이면 이름 줄, 아니면 가운데 줄)
            double starY = Entry is not null ? b.Height / 2 - 16 : (b.Height - 14) / 2;
            VectorIcons.Draw(g, VectorIcon.Star, new Rect(x, starY, 14, 14), 12, AppTheme.FavoriteBrush);
            x += 17;
        }
        if (Entry is { } entry)
        {
            AppTheme.DrawTextIn(g, Title, new Rect(x, b.Height / 2 - 18, Math.Max(1, right - x), 18), AppTheme.TextBrush, AppTheme.FontSize);
            DrawAuthor(g, entry, x, b.Height / 2 + 1, right);
        }
        else AppTheme.DrawTextIn(g, Title, new Rect(x, (b.Height - 18) / 2, Math.Max(1, right - x), 18), AppTheme.TextBrush, AppTheme.FontSize);
    }

    public void Dispose()
    {
        _disposed = true;
        if (Entry is not null) return; // 에셋 상자의 칸은 카탈로그 항목 몫(다음 상자가 다시 씀)
        for (int i = 0; i < _frames.Length; i++)
        {
            _frames[i]?.Dispose();
            _frames[i] = null;
        }
    }
}

/// <summary>
/// 효과 종류 상자 목록(효과 창 왼쪽 칸과 효과 목록 패널 공용): 상자 차례·풍선·대상 도움말 자리, 에셋 상자의 우클릭 메뉴(효과 만들기·이름 바꾸기·삭제 등), 물음표 설명.
/// 효과 탭은 하나씩 넣는 효과를 모두 한 목록에 보인다: 켜진 공식 효과 패키지의 스크립트(예전 기본 효과 차례 — <see cref="OfficialEffects.Ordered"/>),
/// 공식 스크립트가 없는 C# 효과(<see cref="EffectRegistry.Selectable"/> — 글씨), 다른 켜진 패키지의 스크립트, 개인 스크립트 효과 차례. 프리셋 탭은 효과 레시피만.
/// </summary>
internal static class EffectTileCatalog
{
    /// <summary>
    /// 효과 탭 상자(공식 스크립트 → 고를 수 있는 C# 효과 → 받은·내 스크립트)와 프리셋 탭 상자(효과 레시피)를 새로 만든다(미리보기는 아직 그리지 않음).
    /// <paramref name="motionTip"/>: 움직이는 효과 상자 풍선 끝에 덧붙일 추가 범위 안내(없으면 null).
    /// </summary>
    public static (List<EffectTile> Effects, List<EffectTile> Presets) Create(string? motionTip, bool compact = false)
    {
        var culture = global::System.Globalization.CultureInfo.CurrentCulture;
        var effects = new List<EffectTile>();
        var presets = new List<EffectTile>();
        var scripts = AppAssets.Catalog.EffectScripts;
        int officialCount = scripts.TakeWhile(entry => AssetCatalog.OfficialIdOf(entry) is not null).Count();
        foreach (var entry in scripts.Take(officialCount)) effects.Add(AssetTile(entry, culture, motionTip, compact));
        foreach (var definition in EffectRegistry.Selectable)
        {
            var ui = LayerEffectsDialog.UiOf(definition);
            var tile = new EffectTile(ui.Name, [definition.Create()]) { Description = ui.Description, Compact = compact, Key = "effect:" + definition.TypeId };
            ToolTip.SetTip(tile, $"{ui.Name}\n{ui.Description}" + MotionNote(tile, motionTip));
            tile.HelpAt("screen-effect-list", anchor: definition.TypeId == TextDefinition.Id ? "Text" : null);
            effects.Add(tile);
        }
        foreach (var entry in scripts.Skip(officialCount)) effects.Add(AssetTile(entry, culture, motionTip, compact));
        foreach (var entry in AppAssets.Catalog.Recipes) presets.Add(AssetTile(entry, culture, motionTip, compact));
        return (effects, presets);
    }

    /// <summary>개인 에셋 라이브러리를 읽지 못한 까닭(읽었으면 null) — 두 탭 위에 보인다.</summary>
    public static string? LoadError => AppAssets.Store.LibraryError is { } problem
        ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectPreset_LoadError, problem)
        : null;

    /// <summary>움직이는 효과 상자 풍선 끝에 붙는 안내(움직이는 효과 + 추가 범위).</summary>
    private static string MotionNote(EffectTile tile, string? motionTip)
        => tile.Animated ? UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_164E2EA098 + (motionTip is not null ? " " + motionTip : "") : "";

    /// <summary>에셋 하나의 상자: 스크립트 효과는 효과 탭, 효과 레시피는 프리셋 탭.</summary>
    private static EffectTile AssetTile(AssetEntry entry, global::System.Globalization.CultureInfo culture, string? motionTip, bool compact)
    {
        var recipe = entry.EffectTemplate!;
        bool script = entry.Kind == AssetKinds.EffectScript;
        var tile = new EffectTile(entry.DisplayName, recipe.Effects, entry, recipe) { Compact = compact };
        string tip = (script, AppAssets.IsMine(entry)) switch
        {
            (true, true) => UiStrings.EffectPreset_ScriptTileTip,
            (true, false) => string.Format(culture, UiStrings.EffectPreset_ReceivedScriptTileTip, ReceivedFrom(entry)),
            (false, true) => string.Format(culture, UiStrings.EffectPreset_TileTip, recipe.Effects.Count),
            (false, false) => string.Format(culture, UiStrings.EffectPreset_ReceivedTileTip, ReceivedFrom(entry), recipe.Effects.Count),
        };
        string? description = entry.Script?.DescriptionFor(global::System.Globalization.CultureInfo.CurrentUICulture) is { Length: > 0 } text ? text : null;
        if (description is not null) tip = description + "\n" + tip;
        if (entry.ScriptHashes.Any(hash => ScriptLibrary.TrustOf(hash) != ScriptTrust.Trusted))
            tip += "\n" + string.Format(culture, UiStrings.EffectPreset_ScriptTileNote, recipe.Effects.Count(effect => effect is ScriptEffect));
        tile.Description = description;
        ToolTip.SetTip(tile, tip + (script ? MotionNote(tile, motionTip) : ""));
        // 대상 도움말(F1): 공식 효과 = 기본 효과 목록의 그 효과 줄, 받은·내 스크립트 = 스크립트 효과 ① 상자 항목, 프리셋 = 프리셋 탭 ①~③ 항목
        if (LayerEffectsDialog.OfficialHelpTarget(AssetCatalog.OfficialIdOf(entry)) is { } official) tile.HelpAt(official.PageId, anchor: official.Anchor);
        else if (script) tile.HelpAt("screen-script-effects", anchor: "After the official effects and Text");
        else tile.HelpAt("screen-effect-recipes", anchor: "Build an effect list in the effects dialog and click");
        return tile;
    }

    /// <summary>받은 에셋의 출처 이름: 상점 이름, 없으면 만든 사람.</summary>
    private static string ReceivedFrom(AssetEntry entry) => entry.Store?.DisplayName ?? entry.Author?.Name ?? entry.Id;

    /// <summary>에셋 상자 우클릭 메뉴: 에셋 관리자에서 보기, 내 에셋이면 효과 만들기에서 고치기(스크립트)·이름 바꾸기·매개변수(레시피)·삭제.</summary>
    public static IEnumerable<object> Menu(Window owner, AssetEntry entry)
    {
        MenuItem Item(string text, VectorIcon icon, Action action)
        {
            var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 14) };
            item.Click += (_, _) => Dispatcher.UIThread.Post(action);
            return item;
        }
        var show = Item(UiStrings.EffectPreset_ShowInManager, VectorIcon.Package, () => AssetManagerWindow.Open(owner, entry.Kind, entry.Id));
        if (!AppAssets.IsMine(entry)) return [show];
        // 효과 만들기는 스크립트만 다룬다. 스크립트 에셋의 이름은 원문의 선언이라 여기서 바꾸지 않는다
        if (entry.Script is not null)
        {
            var maker = Item(UiStrings.EffectMaker_EditIn, VectorIcon.Sparkles, () => EffectMakerWindow.OpenFromAnywhere(entry.Id));
            return [maker, Item(UiStrings.EffectPreset_Delete, VectorIcon.Trash, () => Delete(owner, entry)), new Separator(), show];
        }
        return
        [
            Item(UiStrings.EffectPreset_Rename, VectorIcon.Pencil, () => Rename(owner, entry)),
            Item(UiStrings.RecipeParams_Edit, VectorIcon.SlidersHorizontal, () => RecipeParametersDialog.Edit(owner, entry)),
            Item(UiStrings.EffectPreset_Delete, VectorIcon.Trash, () => Delete(owner, entry)),
            new Separator(),
            show,
        ];
    }

    /// <summary>내 효과 레시피(이름 겹침 검사·덮어쓰기 대상). 받은 레시피는 고치지 않는다.</summary>
    public static IEnumerable<AssetEntry> MyRecipes => AppAssets.Catalog.Recipes.Where(AppAssets.IsMine);

    private static void Rename(Window owner, AssetEntry entry)
    {
        if (!AppAssets.IsMine(entry) || entry.Recipe is not { } recipe) return;
        var personal = MyRecipes.ToList();
        var name = InputDialog.Ask(owner, UiStrings.EffectPreset_RenameTitle, UiStrings.EffectPreset_NamePrompt,
            recipe.Name, personal.Select(item => item.Name))?.Trim();
        if (string.IsNullOrEmpty(name) || name == recipe.Name) return;
        if (personal.Any(item => item.Name == name))
        {
            Ui.Warn(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.EffectPreset_DuplicateName, name), UiStrings.EffectPreset_RenameTitle);
            return;
        }
        Persist(owner, () => AppAssets.Store.SaveRecipe(recipe with { Name = name }));
    }

    private static void Delete(Window owner, AssetEntry entry)
    {
        if (Ui.Ask(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.EffectPreset_DeleteQuestion, entry.Name), UiStrings.EffectPreset_Delete,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        Persist(owner, () => AppAssets.Store.Remove(entry.Id));
    }

    /// <summary>내 에셋을 바꾼다. 상자는 저장소 변경 알림(<see cref="AppAssets.Changed"/>)으로 다시 만들어진다.</summary>
    public static void Persist(Window owner, Action change) => AppAssets.Try(owner, change, UiStrings.EffectPreset_SaveError);

    /// <summary>상자 물음표를 누르면 효과 이름과 설명만 띄운다. 같은 물음표를 다시 누르거나 바깥을 누르면 닫힌다.</summary>
    public static void ShowHelp(EffectTile tile)
    {
        var text = new TextBlock { Text = tile.Description, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush };
        var flyout = new Flyout
        {
            Placement = PlacementMode.RightEdgeAlignedTop,
            Content = new Border { Width = 300, Padding = new Thickness(4), Child = Ui.Stack(Ui.Label(tile.Title, bold: true), text) },
        };
        PopupMenus.Show(tile, flyout);
    }
}
