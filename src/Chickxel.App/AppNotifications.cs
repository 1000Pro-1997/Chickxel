using Avalonia.Animation;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 앱 알림(AI 질문·작업 완료): 화면 오른쪽 아래에 잠깐 뜨는 작은 알림 창(앱 아이콘 · 제목 · 내용 · 닫기)과 고른 알림 소리(<see cref="NotificationSounds"/>).
/// 알림 창은 하나뿐이라 새 알림은 떠 있는 창의 내용을 바꾼다(쌓이거나 여러 번 뜨지 않음). 포커스를 가져가지 않고, 6초 뒤 사라지며(마우스를 올린 동안은 그대로),
/// 누르면 알린 창을 앞으로 가져온다. 설정(<see cref="AppSettings.NotifyEnabled"/>)으로 끄고, 알린 창을 보고 있으면 기본은 알리지 않는다.
/// </summary>
internal static class AppNotifications
{
    private static Toast? _toast;

    /// <summary>알린다(설정이 꺼졌거나, <paramref name="owner"/>를 보고 있고 그때 알리지 않기면 아무것도 안 함). UI 스레드.</summary>
    public static void Show(Window owner, string title, string message)
    {
        var settings = App.Settings;
        if (!settings.NotifyEnabled) return;
        if (!settings.NotifyWhenActive && owner.IsActive) return;
        NotificationSounds.Play(settings.NotifySound);
        Pop(owner, title, message);
    }

    /// <summary>미리 보기(설정 창): 이 소리로 보기 알림을 띄운다(설정과 관계없이).</summary>
    public static void Preview(Window owner, string sound)
    {
        NotificationSounds.Play(sound);
        Pop(owner, UiStrings.AiNotify_Done, UiStrings.AiNotify_DoneBody);
    }

    /// <summary><paramref name="owner"/>가 띄운 알림을 닫는다(그 창을 닫을 때).</summary>
    public static void Clear(Window owner)
    {
        if (_toast is { } toast && ReferenceEquals(toast.Source, owner)) toast.Close();
    }

    private static void Pop(Window owner, string title, string message)
    {
        if (_toast is not { } toast)
        {
            toast = new Toast();
            toast.Closed += (_, _) => { if (ReferenceEquals(_toast, toast)) _toast = null; };
            _toast = toast;
        }
        toast.Present(owner, title, message);
    }

    /// <summary>알림 창: 테두리 없는 둥근 카드(그림자), 맨 위, 작업 표시줄·포커스 없음.</summary>
    private sealed class Toast : Window
    {
        /// <summary>카드 폭, 화면 작업 영역 가장자리와의 틈(DIP).</summary>
        private const double CardWidth = 340, ScreenGap = 16;
        private static readonly TimeSpan Life = TimeSpan.FromSeconds(6);

        private readonly TextBlock _title, _message;
        private readonly Border _card;
        private readonly DispatcherTimer _timer = new() { Interval = Life };

        /// <summary>알린 창(누르면 앞으로 가져옴).</summary>
        public Window? Source { get; private set; }

        public Toast()
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0;
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];

            var icon = new Image { Source = Icons.AppIconBitmap(), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
            RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.None);
            _title = new TextBlock { FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
            _message = new TextBlock { Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
            var close = Ui.IconButton(VectorIcon.X, UiStrings.Notify_Close, (_, _) => Close(), size: 11);
            close.Padding = new Thickness(4);
            close.MinHeight = 0;
            close.Background = Brushes.Transparent;
            close.BorderThickness = new Thickness(0);
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new Thickness(8, 0, 0, 0);
            var text = new StackPanel { Children = { _title, _message } };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(close, 2);
            _card = new Border
            {
                Width = CardWidth,
                Background = AppTheme.PanelBrush,
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 8, 12),
                BoxShadow = BoxShadows.Parse("0 6 20 0 #70000000"),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { icon, text, close } },
            };
            // 그림자가 잘리지 않게 카드 둘레에 여백.
            Content = new Border { Padding = new Thickness(14), Child = _card };

            _timer.Tick += (_, _) => Close();
            _card.PointerEntered += (_, _) => _timer.Stop();
            _card.PointerExited += (_, _) => _timer.Start();
            _card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || e.Handled) return;
                if (Source is { } source)
                {
                    if (source.WindowState == WindowState.Minimized) source.WindowState = WindowState.Normal;
                    source.Activate();
                }
                Close();
            };
            Closed += (_, _) => _timer.Stop();
        }

        /// <summary>내용을 바꾸고 <paramref name="owner"/>가 있는 화면 오른쪽 아래(작업 영역 안)에 띄운다. 떠 있으면 남은 시간을 처음부터.</summary>
        public void Present(Window owner, string title, string message)
        {
            Source = owner;
            _title.Text = title;
            _message.Text = message;
            var content = (Control)Content!;
            content.Measure(Avalonia.Size.Infinity);
            var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
            if (screen is not null)
            {
                var area = screen.WorkingArea;
                double scale = screen.Scaling;
                var size = content.DesiredSize;
                Position = new PixelPoint(
                    (int)(area.Right - (size.Width + ScreenGap) * scale),
                    (int)(area.Bottom - (size.Height + ScreenGap) * scale));
            }
            if (!IsVisible)
            {
                Show();
                Opacity = 1;
            }
            _timer.Stop();
            _timer.Start();
        }
    }
}
