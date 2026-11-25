using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>튜토리얼 종류(<see cref="MainForm.StartTutorial"/>). 이름은 마친 튜토리얼 기록(<see cref="AppSettings.TutorialsDone"/>)에도 쓰므로 바꾸지 않는다.</summary>
public enum TutorialKind
{
    /// <summary>그림을 픽셀 아트로(픽셀로 정리 창).</summary>
    Pixelate,
    /// <summary>효과 쓰기(효과 목록 · 오른쪽 효과 설정 · 에셋 받기).</summary>
    Effects,
    /// <summary>AI로 그리기(AI 채팅 여는 법 · 로그인 · 설정 · 요청 — MCP).</summary>
    Ai,
    /// <summary>3D를 픽셀 아트로(3D 모델 창).</summary>
    Model,
    /// <summary>효과 만들기(효과 만들기 창 · AI에게 효과 부탁) — AI로 그리기와 효과 쓰기를 마친 뒤.</summary>
    EffectMaker,
    /// <summary>내보내기(내보내기 창 · 여러 장 · 저장할 때 함께 내보내기).</summary>
    Export,
    /// <summary>레이어와 프레임(레이어 · 프레임 더하고 빼기 · 프레임 링크).</summary>
    LayersFrames,
    /// <summary>레벨(계정 레벨 · 경험치 · 갤러리 올리기).</summary>
    Level,
    /// <summary>캔버스 다루기(확대·옮기기 · 마우스 단추 · Ctrl 끌기 · 캔버스 크기 · 레이어 전체 변형).</summary>
    Canvas,
    /// <summary>타일맵(타일맵 레이어 · 타일셋 · 타일 도구).</summary>
    Tilemap,
    /// <summary>레이어 묶기와 스킨(부모·자식 레이어 · 스킨 갈아 끼우기 · 겹쳐 쌓기).</summary>
    Skins,
}

/// <summary>
/// 튜토리얼 창(모드리스, 하나만): 위에 Chickxel 대표 기능 넷(그림을 픽셀 아트로 · 3D를 픽셀 아트로 · 효과 쓰기 · AI로 그리기)을 예시 움짤과 함께 큰 카드로,
/// 아래 "더 알아보기"에 나머지(효과 만들기 · 캔버스 다루기 · 레이어와 프레임 · 레이어 묶기와 스킨 · 타일맵 · 내보내기 · 레벨)를 작은 카드로(한 줄에 넷) 보인다.
/// 카드를 누르면 창을 닫고 그 튜토리얼을 시작한다.
/// 먼저 마쳐야 하는 튜토리얼이 있는 카드(효과 만들기 = AI로 그리기 + 효과 쓰기)는 잠겨 있고 남은 선행 튜토리얼을 단추로 보인다.
/// 켤 때마다 뜨고(<see cref="AppSettings.ShowTutorialOnStart"/>), "다시 보지 않기"를 켜고 닫으면 다음부터 뜨지 않는다. 도움말 메뉴·메뉴 줄 오른쪽 학사모 단추로 언제든 다시 연다.
/// </summary>
public sealed class TutorialWindow : Window
{
    /// <summary>카드 폭(큰 카드 넷, 작은 카드는 한 줄에 넷 — 같은 폭).</summary>
    private const double CardWidth = 264, Gap = 12;

    /// <summary>먼저 마쳐야 하는 튜토리얼.</summary>
    internal static IReadOnlyList<TutorialKind> Prerequisites(TutorialKind kind) => kind switch
    {
        TutorialKind.EffectMaker => [TutorialKind.Ai, TutorialKind.Effects],
        _ => [],
    };

    /// <summary>튜토리얼 이름(카드 제목·말풍선 머리).</summary>
    internal static string TitleOf(TutorialKind kind) => kind switch
    {
        TutorialKind.Pixelate => UiStrings.Tutorial_Pixelate_Title,
        TutorialKind.Effects => UiStrings.Tutorial_Effects_Title,
        TutorialKind.Ai => UiStrings.Tutorial_Ai_Title,
        TutorialKind.Model => UiStrings.Tutorial_Model_Title,
        TutorialKind.EffectMaker => UiStrings.Tutorial_Maker_Title,
        TutorialKind.Export => UiStrings.Tutorial_Export_Title,
        TutorialKind.LayersFrames => UiStrings.Tutorial_Layers_Title,
        TutorialKind.Canvas => UiStrings.Tutorial_Canvas_Title,
        TutorialKind.Tilemap => UiStrings.Tutorial_Tilemap_Title,
        TutorialKind.Skins => UiStrings.Tutorial_Skins_Title,
        _ => UiStrings.Tutorial_Level_Title,
    };

    private static TutorialWindow? _instance;

    private readonly AppSettings _settings;
    private readonly MainForm _owner;
    private readonly CheckBox _dontShow;

    private TutorialWindow(MainForm owner, AppSettings settings)
    {
        _settings = settings;
        _owner = owner;
        Title = UiStrings.Tutorial_Title;
        Icon = owner.Icon;
        Background = AppTheme.BackBrush;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock { Text = UiStrings.Tutorial_Heading, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush };
        var intro = new TextBlock { Text = UiStrings.Tutorial_Intro, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) };

        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap };
        cards.Children.Add(Card(TutorialKind.Pixelate, HelpWindow.MediaOpener("feature-pixelate"), UiStrings.Tutorial_Pixelate_Desc));
        cards.Children.Add(Card(TutorialKind.Model, () => typeof(TutorialWindow).Assembly.GetManifestResourceStream("Chickxel.Tutorial.card-model.webp"), UiStrings.Tutorial_Model_Desc));
        cards.Children.Add(Card(TutorialKind.Effects, HelpWindow.MediaOpener("feature-effect-drop"), UiStrings.Tutorial_Effects_Desc));
        cards.Children.Add(Card(TutorialKind.Ai, HelpWindow.MediaOpener("feature-ai-draw"), UiStrings.Tutorial_Ai_Desc));

        var moreHeading = new TextBlock { Text = UiStrings.Tutorial_More, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, Margin = new Thickness(0, 18, 0, 8) };
        // 작은 카드는 한 줄에 넷씩(큰 카드와 같은 폭).
        Control[] small =
        [
            SmallCard(TutorialKind.EffectMaker, VectorIcon.Sparkles, UiStrings.Tutorial_Maker_Desc),
            SmallCard(TutorialKind.Canvas, VectorIcon.MousePointerClick, UiStrings.Tutorial_Canvas_Desc),
            SmallCard(TutorialKind.LayersFrames, VectorIcon.Layers, UiStrings.Tutorial_Layers_Desc),
            SmallCard(TutorialKind.Skins, VectorIcon.Shirt, UiStrings.Tutorial_Skins_Desc),
            SmallCard(TutorialKind.Tilemap, VectorIcon.Grid3x3, UiStrings.Tutorial_Tilemap_Desc),
            SmallCard(TutorialKind.Export, VectorIcon.Upload, UiStrings.Tutorial_Export_Desc),
            SmallCard(TutorialKind.Level, VectorIcon.Star, UiStrings.Tutorial_Level_Desc),
        ];
        var more = new StackPanel { Spacing = Gap };
        for (int i = 0; i < small.Length; i += 4)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap };
            foreach (var card in small.Skip(i).Take(4)) row.Children.Add(card);
            more.Children.Add(row);
        }

        _dontShow = Ui.Check(UiStrings.Tutorial_DontShow, !settings.ShowTutorialOnStart).Tip(UiStrings.Tutorial_DontShowTip);
        _dontShow.VerticalAlignment = VerticalAlignment.Center;
        var close = Ui.Button(UiStrings.Tutorial_Close, (_, _) => Close());
        var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(_dontShow);

        Content = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 16),
            MaxWidth = 4 * CardWidth + 3 * Gap + 2 * 22,
            Children = { heading, intro, cards, moreHeading, more, bottom },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            bool show = _dontShow.IsChecked != true;
            if (_settings.ShowTutorialOnStart == show) return;
            _settings.ShowTutorialOnStart = show;
            _settings.Save();
        };
    }

    private bool Done(TutorialKind kind) => _settings.TutorialsDone.Contains(kind.ToString());

    private void Begin(TutorialKind kind)
    {
        Close();
        _owner.StartTutorial(kind);
    }

    /// <summary>초록 "완료" 표시(체크 + 글자).</summary>
    private static StackPanel DoneMark()
    {
        var mark = Ui.Row(Ui.Icon(VectorIcon.Check, 12), new TextBlock { Text = UiStrings.Tutorial_Done, Foreground = AppTheme.SavedBrush, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        ((IconView)mark.Children[0]).Foreground = AppTheme.SavedBrush;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(8, 0, 0, 0);
        return mark;
    }

    /// <summary>
    /// 작은 카드(더 알아보기): 왼쪽 아이콘, 제목(마쳤으면 "완료"), 한 줄 설명, "따라 하기"(마쳤으면 "다시 하기").
    /// 먼저 마쳐야 하는 튜토리얼이 남았으면 잠금 아이콘과 "먼저 마칠 튜토리얼"을 단추로 보인다(누르면 그 튜토리얼 시작).
    /// </summary>
    private Control SmallCard(TutorialKind kind, VectorIcon icon, string description)
    {
        var missing = Prerequisites(kind).Where(p => !Done(p)).ToList();
        bool locked = missing.Count > 0, done = Done(kind);
        var title = new TextBlock { Text = TitleOf(kind), FontSize = 13.5, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        var head = Ui.Row(new IconView(locked ? VectorIcon.Lock : icon, locked ? AppTheme.DimTextBrush : HelpWindow.Yellow) { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, title);
        if (done) head.Children.Add(DoneMark());
        var stack = new StackPanel { Spacing = 6, Width = CardWidth - 20, Children = { head } };
        stack.Children.Add(new TextBlock { Text = description, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap, Height = 36 });
        if (locked)
        {
            stack.Children.Add(new TextBlock { Text = UiStrings.Tutorial_NeedsFirst, Foreground = HelpWindow.Yellow, FontSize = AppTheme.SmallFontSize });
            var needs = new WrapPanel();
            foreach (var p in missing)
            {
                var go = Ui.Button(VectorIcon.Play, TitleOf(p), (_, _) => Begin(p));
                go.Margin = new Thickness(0, 0, 6, 4);
                needs.Children.Add(go);
            }
            stack.Children.Add(needs);
        }
        else
        {
            var start = Ui.Button(done ? VectorIcon.Refresh : VectorIcon.Play, done ? UiStrings.Tutorial_Again : UiStrings.Tutorial_Start, (_, _) => Begin(kind));
            start.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(start);
        }
        var card = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = stack,
            Opacity = locked ? 0.85 : 1,
        };
        if (!locked)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerEntered += (_, _) => card.BorderBrush = HelpWindow.Yellow;
            card.PointerExited += (_, _) => card.BorderBrush = AppTheme.BorderBrush;
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left && !e.Handled) Begin(kind);
            };
        }
        return card;
    }

    /// <summary>튜토리얼 창을 연다(이미 열려 있으면 앞으로).</summary>
    public static void Open(MainForm owner, AppSettings settings)
    {
        if (_instance is { } open)
        {
            open.Activate();
            return;
        }
        _instance = new TutorialWindow(owner, settings);
        _instance.Show(owner);
    }

    /// <summary>열린 튜토리얼 창(점검 샷용), 없으면 null.</summary>
    public static TutorialWindow? Current => _instance;

    /// <summary>
    /// 기능 카드 하나: 위 예시 움짤(<paramref name="media"/>, 카드마다 같은 높이 — 제목 줄이 나란히), 제목(마친 튜토리얼이면 옆에 초록 "완료"), 설명, "따라 하기"(마쳤으면 "다시 하기") 단추.
    /// 카드 어디를 눌러도 시작한다.
    /// </summary>
    private Control Card(TutorialKind kind, Func<Stream?> media, string description)
    {
        void Begin() => this.Begin(kind);
        bool done = Done(kind);
        var stack = new StackPanel { Spacing = 6, Width = CardWidth };
        if (AnimatedImage.TryCreate(media) is { } clip)
        {
            // 움짤 비율이 조금씩 달라(16:9 · 800×484) 같은 높이로 위를 맞춰 아래를 자른다(눌러 펴지 않게 캔버스에 원래 비율로).
            clip.Width = CardWidth;
            var frame = new Canvas { Width = CardWidth, Height = Math.Round(CardWidth * 9 / 16), ClipToBounds = true, Children = { clip } };
            stack.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = frame });
        }
        var heading = new TextBlock { Text = TitleOf(kind), FontSize = 15, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        var row = Ui.Row(heading);
        if (done) row.Children.Add(DoneMark());
        row.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock { Text = description, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, Height = 60 });
        var start = Ui.Button(done ? VectorIcon.Refresh : VectorIcon.Play, done ? UiStrings.Tutorial_Again : UiStrings.Tutorial_Start, (_, _) => Begin());
        start.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(start);
        var card = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
            Tag = kind,
        };
        card.PointerEntered += (_, _) => card.BorderBrush = HelpWindow.Yellow;
        card.PointerExited += (_, _) => card.BorderBrush = AppTheme.BorderBrush;
        card.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && !e.Handled) Begin();
        };
        return card;
    }
}
