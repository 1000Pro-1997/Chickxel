using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>보낼 첨부 그림(PNG 바이트 + 미리보기, 픽셀 아트인지 — 미리보기를 최근접으로 키울지).</summary>
internal sealed record ChatAttachment(byte[] Png, Bitmap Thumb, bool PixelArt);

/// <summary>
/// AI 대화의 그림 도우미(AI 채팅 창과 효과 만들기 AI 칸이 같이 씀): 첨부 PNG 만들기, 체커 바탕, 그림 칩(입력란·말풍선 같은 모양),
/// 크게 보기 풍선(복사·저장·첨부 빼기), 말풍선 글 사이 칩, 대화 속 그림의 누르기·우클릭 동작.
/// </summary>
internal static class ChatImages
{
    /// <summary>첨부 이미지의 긴 변 최대(이보다 크면 줄여서 PNG로 보냄).</summary>
    public const int AttachMaxSide = 1568;
    /// <summary>픽셀 아트 첨부(캔버스·미리보기)를 정수 배로 키울 때의 긴 변 목표.</summary>
    public const int CanvasAttachSide = 512;
    /// <summary>칩 미리보기 풍선의 긴 변, 크게 보기의 긴 변 최대.</summary>
    private const int ChipTipSide = 160, ChipViewSide = 420;
    /// <summary>말풍선 칩 안 미리보기 한 변.</summary>
    private const int BubbleChipThumb = 18;

    public static readonly IBrush ChipBrush = AppTheme.Brush(Color.FromRgb(40, 54, 76));
    public static readonly IBrush ChipSelectedBrush = AppTheme.Brush(Color.FromRgb(46, 86, 140));

    /// <summary>투명한 곳이 보이게 체커 무늬 위에 자식을 그리는 틀.</summary>
    public sealed class CheckerBackground : Decorator
    {
        public override void Render(DrawingContext g) => AppTheme.DrawChecker(g, new Rect(Bounds.Size), 6);
    }

    /// <summary>작은 칩 미리보기 바탕의 잔 체커(칸 3px — 투명한 곳이 보이게).</summary>
    private sealed class SmallCheckerBackground : Decorator
    {
        public override void Render(DrawingContext g) => AppTheme.DrawChecker(g, new Rect(Bounds.Size), 3);
    }

    /// <summary>비트맵을 PNG로(긴 변이 너무 크면 줄임).</summary>
    public static byte[] ToPng(Bitmap bmp)
    {
        var size = bmp.PixelSize;
        int longSide = Math.Max(size.Width, size.Height);
        Bitmap src = bmp;
        if (longSide > AttachMaxSide)
        {
            double k = (double)AttachMaxSide / longSide;
            src = bmp.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(size.Width * k)), Math.Max(1, (int)(size.Height * k))),
                BitmapInterpolationMode.HighQuality);
        }
        using var ms = new MemoryStream();
        src.Save(ms);
        if (!ReferenceEquals(src, bmp)) src.Dispose();
        return ms.ToArray();
    }

    /// <summary>비트맵 → 픽셀 그림(PNG를 거침). 읽지 못하면 null.</summary>
    public static PixelImage? ToPixelImage(Bitmap bmp)
    {
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms);
            ms.Position = 0;
            return PngIO.Load(ms);
        }
        catch (Exception)
        {
            // 플랫폼 인코더·디코더가 던지는 예외 형식은 정해져 있지 않다 — 메뉴 항목을 끈다.
            return null;
        }
    }

    /// <summary>정수 배 최근접 확대.</summary>
    public static PixelImage ScaleUp(PixelImage src, int n)
    {
        var dst = new PixelImage(src.Width * n, src.Height * n);
        for (int y = 0; y < dst.Height; y++)
        {
            int row = (y / n) * src.Width, dstRow = y * dst.Width;
            for (int x = 0; x < dst.Width; x++) dst.Pixels[dstRow + x] = src.Pixels[row + x / n];
        }
        return dst;
    }

    /// <summary>
    /// 픽셀 그림을 첨부 모양으로: 픽셀 아트면 작은 그림을 정수 배 최근접으로 키워(긴 변 <see cref="CanvasAttachSide"/> 안팎) 보내고,
    /// 아니면 긴 변이 <see cref="AttachMaxSide"/>를 넘을 때 줄여 보낸다.
    /// </summary>
    public static ChatAttachment Attachment(PixelImage img, bool pixelArt)
    {
        var bmp = ImageUtil.ToBitmap(img);
        if (!pixelArt && Math.Max(img.Width, img.Height) > AttachMaxSide) return new ChatAttachment(ToPng(bmp), bmp, pixelArt);
        int n = pixelArt ? Math.Max(1, CanvasAttachSide / Math.Max(img.Width, img.Height)) : 1;
        using var ms = new MemoryStream();
        PngIO.Save(n > 1 ? ScaleUp(img, n) : img, ms);
        return new ChatAttachment(ms.ToArray(), bmp, pixelArt);
    }

    /// <summary>그림 우클릭 메뉴(이미지 저장…·이미지 복사). 저장·복사는 화면에 보이는 비트맵 그대로(픽셀 크기 그대로).</summary>
    public static void AttachImageMenu(Control view, Bitmap bmp)
        => PopupMenus.Attach(view, () => ImageActions.Menu(view, () => ToPixelImage(bmp), () => "chickxel-chat.png"));

    /// <summary>대화 속 그림: 한 번 누르면 크게 보기(<see cref="ShowChipFlyout"/>), 우클릭하면 이미지 저장·복사.</summary>
    public static Control WithImageActions(Control view, Bitmap bmp)
    {
        view.Cursor = new Cursor(StandardCursorType.Hand);
        view.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            ShowChipFlyout(view, bmp, null);
        };
        AttachImageMenu(view, bmp);
        return view;
    }

    /// <summary>
    /// 칩 안 내용(입력란·말풍선 같은 모양): 정사각 미리보기(<paramref name="side"/>, 체커 바탕 위 비율 그대로 — 픽셀 아트·작은 그림은 최근접으로 픽셀 그대로,
    /// 큰 사진은 부드럽게 줄임; 그림이 없으면 그림 아이콘)와 강조색 굵은 "#N".
    /// </summary>
    public static StackPanel ChipContent(int number, Bitmap? thumb, double side, double fontSize, bool pixelArt)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (thumb is not null && side > 0)
        {
            var px = thumb.PixelSize;
            double k = Math.Min(side / Math.Max(1, px.Width), side / Math.Max(1, px.Height));
            var image = new Image
            {
                Source = thumb,
                Width = Math.Max(1, px.Width * k),
                Height = Math.Max(1, px.Height * k),
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapInterpolationMode(image, pixelArt || k >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            panel.Children.Add(new Border
            {
                Width = side,
                Height = side,
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new SmallCheckerBackground { Child = image },
            });
        }
        else panel.Children.Add(new IconView(VectorIcon.Image) { Width = 12, Height = 12, Foreground = AppTheme.AccentBrush, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "#" + number, Foreground = AppTheme.AccentBrush, FontWeight = FontWeight.Bold, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    /// <summary>미리보기 풍선 내용(긴 변 <see cref="ChipTipSide"/>, 작은 그림은 정수 배 최근접).</summary>
    public static Control ChipTip(Bitmap thumb)
    {
        var px = thumb.PixelSize;
        double k = Math.Min((double)ChipTipSide / Math.Max(1, px.Width), (double)ChipTipSide / Math.Max(1, px.Height));
        if (k > 1) k = Math.Max(1, Math.Floor(k));
        var image = new Image { Source = thumb, Width = Math.Max(1, px.Width * k), Height = Math.Max(1, px.Height * k), Stretch = Stretch.Fill };
        if (k > 1) RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return new CheckerBackground { Child = image };
    }

    /// <summary>칩을 누르면: 그림을 크게(긴 변 <see cref="ChipViewSide"/> 안, 작은 그림은 정수 배 최근접) 보이고 복사·저장 단추를 제공한다. <paramref name="remove"/>가 있으면 "첨부 빼기" 단추.</summary>
    public static void ShowChipFlyout(Control anchor, Bitmap image, Action? remove)
    {
        var px = image.PixelSize;
        double k = Math.Min((double)ChipViewSide / Math.Max(1, px.Width), (double)ChipViewSide / Math.Max(1, px.Height));
        if (k > 1) k = Math.Max(1, Math.Floor(k));
        var view = new Image { Source = image, Width = Math.Max(1, px.Width * k), Height = Math.Max(1, px.Height * k), Stretch = Stretch.Fill };
        if (k > 1) RenderOptions.SetBitmapInterpolationMode(view, BitmapInterpolationMode.None);
        var stack = new StackPanel { Spacing = 6, Children = { new CheckerBackground { Child = view, HorizontalAlignment = HorizontalAlignment.Left } } };
        AttachImageMenu(view, image);
        var flyout = new Flyout { Placement = PlacementMode.Top, Content = stack };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(Ui.Button(VectorIcon.Copy, UiStrings.Preview_Menu_CopyImage, (_, _) =>
        {
            if (ToPixelImage(image) is { } img) ImageActions.Copy(anchor, img);
            flyout.Hide();
        }));
        actions.Children.Add(Ui.Button(VectorIcon.Download, UiStrings.Image_SaveMenu, (_, _) =>
        {
            flyout.Hide();
            Dispatcher.UIThread.Post(() =>
            {
                if (ToPixelImage(image) is { } img) ImageActions.SaveAs(anchor, img, "chickxel-chat.png");
            });
        }));
        stack.Children.Add(actions);
        if (remove is not null)
        {
            var button = Ui.Button(VectorIcon.Trash, UiStrings.S_ChatWindow_AddAttachment_1F581E4392, (_, _) =>
            {
                flyout.Hide();
                remove();
            });
            button.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(button);
        }
        flyout.ShowAt(anchor);
    }

    /// <summary>보낸 말풍선 글 사이의 칩: 작은 미리보기(없으면 그림 아이콘) + "#N". 마우스를 올리면 미리보기, 누르면 크게 보기.</summary>
    private static Control BubbleChip(int number, Bitmap? thumb)
    {
        var chip = new Border
        {
            Background = ChipBrush,
            BorderBrush = AppTheme.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 1, 4, 1),
            Margin = new Thickness(1, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = ChipContent(number, thumb, BubbleChipThumb, AppTheme.FontSize, pixelArt: true),
        };
        if (thumb is not null)
        {
            ToolTip.SetTip(chip, ChipTip(thumb));
            AttachImageMenu(chip, thumb);
            chip.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                ShowChipFlyout(chip, thumb, null);
            };
        }
        return chip;
    }

    /// <summary>
    /// 그림 칩이 든 사용자 메시지 글(<see cref="ChatImageTokens"/>): 글 사이 칩 자리에 칩(작은 미리보기 + 번호)을 넣은 고를 수 있는 글.
    /// <paramref name="images"/> = 첨부 순서(칩 번호 − 1)의 미리보기(없으면 null). 글은 끌어서 골라 Ctrl+C로 복사한다.
    /// </summary>
    public static SelectableTextBlock BubbleText(string text, IReadOnlyList<Bitmap?> images)
    {
        var chips = new List<(Control Chip, Bitmap Image)>();
        var block = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.TextBrush,
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
        };
        foreach (var s in ChatImageTokens.Split(text))
        {
            if (!s.IsImage) block.Inlines!.Add(new Run(s.Text));
            else
            {
                var thumb = s.Image <= images.Count ? images[s.Image - 1] : null;
                var chip = BubbleChip(s.Image, thumb);
                block.Inlines!.Add(new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center });
                if (thumb is not null) chips.Add((chip, thumb));
            }
        }
        RouteBubbleChipEvents(block, chips);
        return block;
    }

    /// <summary>
    /// SelectableTextBlock가 안쪽 칩보다 먼저 우클릭을 처리해 글 복사 메뉴를 열거나 포인터를 잡을 수 있으므로
    /// 글 컨트롤의 터널 단계에서 칩 자리를 처리한다. 칩 밖의 글 선택·기본 메뉴는 그대로 둔다.
    /// </summary>
    private static void RouteBubbleChipEvents(SelectableTextBlock text, IReadOnlyList<(Control Chip, Bitmap Image)> chips)
    {
        if (chips.Count == 0) return;
        text.AddHandler(Control.ContextRequestedEvent, (_, e) =>
        {
            foreach (var (chip, image) in chips)
            {
                if (!e.TryGetPosition(chip, out var point) || !new Rect(chip.Bounds.Size).Contains(point)) continue;
                PopupMenus.Show(chip, ImageActions.Menu(chip, () => ToPixelImage(image), () => "chickxel-chat.png"), e);
                return;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        text.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            foreach (var (chip, image) in chips)
            {
                if (!new Rect(chip.Bounds.Size).Contains(e.GetPosition(chip))) continue;
                e.Handled = true;
                ShowChipFlyout(chip, image, null);
                return;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}

/// <summary>
/// 그림 칩이 들어가는 AI 대화 입력란(AI 채팅 창의 탭·효과 만들기 AI 칸이 같이 씀): 붙여 넣기·끌어 놓기·"이미지…"·"캔버스 첨부" 등으로 넣은 그림은
/// 커서 자리에 칩 글자(<see cref="ChatImageTokens"/> — 보이지 않는 표시 글자로 감싼 [이미지 #N])로 들어가고, 입력란 위 층이 그 글자 자리를
/// 칩(체커 위 작은 정사각 미리보기 + 강조색 #N)으로 덮는다. 칩은 한 덩어리라 Backspace·Delete 한 번에 지워지고(그 첨부도 빠짐) 커서가 칩 안에 들어가지 않으며,
/// 지우거나 옮기면 번호를 글 순서대로 다시 매긴다. 마우스를 올리면 미리보기, 누르면 크게 보기(빼기 단추).
/// Enter(보내기)는 쓰는 쪽이 <see cref="Input"/>에 따로 건다(칩 자판을 먼저 처리하도록 이 컨트롤이 먼저 건다).
/// </summary>
internal sealed class ChatImageInput : Panel
{
    /// <summary>입력란 칩 미리보기의 최소 한 변, 칩이 글자 줄 위아래로 넘치는 거리(줄 사이 여백 안).</summary>
    private const int MinChipThumb = 10, ChipBleed = 1;

    /// <summary>입력란 위에 그림 칩을 그리는 층(칩 글자 자리를 덮음).</summary>
    private readonly Canvas _chipLayer = new() { ClipToBounds = false };
    /// <summary>입력란의 그림(칩 번호 순서).</summary>
    private readonly List<ChatAttachment> _attachments = [];
    /// <summary>입력란 글을 그리는 부분(글자 자리 계산)과 그 스크롤.</summary>
    private TextPresenter? _presenter;
    private ScrollViewer? _inputScroll;
    /// <summary>입력란 글을 코드가 바꾸는 중(바뀜 이벤트로 다시 맞추지 않게).</summary>
    private bool _syncingInput, _syncPosted;
    /// <summary>층에 놓인 칩(글 안 칩 순서).</summary>
    private readonly List<Border> _chipViews = [];

    public ChatImageInput(string watermark)
    {
        Input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            MaxHeight = 160,
            Watermark = watermark,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(Input, ScrollBarVisibility.Auto);
        Input.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Input.PropertyChanged += (_, e) =>
        {
            if (_syncingInput) return;
            if (e.Property == TextBox.TextProperty) PostSync();
            else if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty) SnapSelection();
        };
        Input.TemplateApplied += (_, e) =>
        {
            _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
            if (_presenter is null) return;
            _presenter.LayoutUpdated += (_, _) => UpdateChipLayer();
            _inputScroll = _presenter.FindAncestorOfType<ScrollViewer>();
            if (_inputScroll is not null) _inputScroll.ScrollChanged += (_, _) => UpdateChipLayer();
        };
        DragDrop.SetAllowDrop(Input, true);
        Input.AddHandler(DragDrop.DragEnterEvent, OnDragOver, RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        Input.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        Input.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        Children.Add(Input);
        Children.Add(_chipLayer);
    }

    /// <summary>입력란(보내기 자판·포커스·워터마크는 쓰는 쪽이 다룸).</summary>
    public TextBox Input { get; }

    /// <summary>입력란의 첨부(칩 번호 순서). 읽기 전에 <see cref="Sync"/>.</summary>
    public IReadOnlyList<ChatAttachment> Attachments => _attachments;

    /// <summary>보낼 글: 칩을 저장할 모양(칩 이름 "Image")으로 바꾸고 앞뒤 공백을 뺀 글. 칩 번호 = 첨부 순서.</summary>
    public string StoredText()
    {
        Sync();
        return ChatImageTokens.Relabel((Input.Text ?? "").Trim(), ChatImageTokens.StoredLabel);
    }

    private static string ChipLabel => UiStrings.Chat_Image_Label;

    public void FocusInput() => Dispatcher.UIThread.Post(() => Input.Focus(), DispatcherPriority.Background);

    // ───────────── 첨부 ─────────────

    /// <summary>그림 파일을 하나 골라 칩으로 넣는다.</summary>
    public void AttachFile()
    {
        var path = Ui.OpenFile(this, UiStrings.S_ChatWindow_AttachFile_A024695555, null, new FileFilter(UiStrings.S_ChatWindow_AttachFile_1565F6E888, "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"));
        if (path is not null) AttachPath(path);
    }

    /// <summary>픽셀 그림을 칩으로 넣는다(<see cref="ChatImages.Attachment"/>의 크기 규칙).</summary>
    public void AttachImage(PixelImage img, bool pixelArt) => Add(ChatImages.Attachment(img, pixelArt));

    /// <summary>그림 파일을 칩으로 넣는다(읽지 못하면 알림). PNG이고 크지 않으면 그대로, 아니면 PNG로(긴 변이 크면 줄여서) 보낸다.</summary>
    public void AttachPath(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_AttachFile_FB523C7B58, ex.Message));
            return;
        }
        Bitmap bmp;
        try
        {
            using var ms = new MemoryStream(bytes);
            bmp = new Bitmap(ms);
        }
        catch (Exception)
        {
            // 디코더가 던지는 예외 형식은 플랫폼마다 다르다.
            Ui.Error(this, UiStrings.S_ChatWindow_AttachFile_0469DC1AFF);
            return;
        }
        bool isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
        bool small = Math.Max(bmp.PixelSize.Width, bmp.PixelSize.Height) <= ChatImages.AttachMaxSide;
        Add(new ChatAttachment(isPng && small ? bytes : ChatImages.ToPng(bmp), bmp, PixelArt: false));
    }

    /// <summary>⌘/Ctrl+V: 클립보드에 그림(또는 복사한 그림 파일)이 있으면 커서 자리에 칩으로 넣는다(글자 붙여넣기는 입력란이 그대로 함).</summary>
    public async Task PasteImageAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        Bitmap? bmp;
        List<string> files = [];
        try
        {
            bmp = await clipboard.TryGetBitmapAsync();
            if (bmp is null && await clipboard.TryGetFilesAsync() is { } items)
                files = [.. items.Select(f => f.TryGetLocalPath()).OfType<string>().Where(IsAttachable)];
        }
        catch (Exception)
        {
            // 플랫폼 클립보드가 그림 형식을 못 주면 무시(글자 붙여넣기만).
            return;
        }
        if (bmp is not null) Add(new ChatAttachment(ChatImages.ToPng(bmp), bmp, PixelArt: false));
        foreach (var f in files) AttachPath(f);
    }

    /// <summary>
    /// 그림 하나를 커서 자리에 칩으로 넣는다(고른 글이 있으면 그 자리를 바꿈). 앞뒤 글자와 붙지 않게 사이를 한 칸 띄우고, 커서는 칩 뒤로.
    /// </summary>
    private void Add(ChatAttachment attachment)
    {
        Sync();
        _attachments.Add(attachment);
        var text = Input.Text ?? "";
        int a = Math.Clamp(Input.SelectionStart, 0, text.Length), b = Math.Clamp(Input.SelectionEnd, 0, text.Length);
        int start = Math.Min(a, b), end = Math.Max(a, b);
        if (start == end) start = end = Math.Clamp(Input.CaretIndex, 0, text.Length);
        string before = text[..start], after = text[end..];
        var insert = (before.Length > 0 && !char.IsWhiteSpace(before[^1]) ? " " : "")
                     + ChatImageTokens.Token(ChipLabel, _attachments.Count)
                     + (after.Length == 0 || !char.IsWhiteSpace(after[0]) ? " " : "");
        SetInputText(before + insert + after, start + insert.Length);
        Sync();
        FocusInput();
    }

    /// <summary>입력란의 글과 그림을 모두 비운다(보낸 뒤).</summary>
    public void Clear()
    {
        _attachments.Clear();
        SetInputText("", 0);
        UpdateChipLayer();
    }

    /// <summary>
    /// 입력란에 글을 넣는다(보내지 않음). 이미 넣은 그림 칩은 앞에 그대로 둔다(그림을 붙인 뒤 부탁 글을 채울 때).
    /// </summary>
    public void Prefill(string text)
    {
        Sync();
        var current = Input.Text ?? "";
        var chips = string.Join(" ", ChatImageTokens.Find(current).Select(t => current.Substring(t.Start, t.Length)));
        var full = chips.Length > 0 ? chips + " " + text : text;
        SetInputText(full, full.Length);
        Sync();
        FocusInput();
    }

    /// <summary>마지막 메시지 고치기: 저장한 글(칩 이름 "Image")과 그 첨부를 입력란에 되살린다. 첨부가 없으면(지난 실행의 대화) 칩은 빠진다.</summary>
    public void Restore(string storedText, IReadOnlyList<ChatAttachment> attachments)
    {
        _attachments.Clear();
        _attachments.AddRange(attachments);
        var text = ChatImageTokens.Relabel(storedText, ChipLabel);
        SetInputText(text, text.Length);
        Sync();
    }

    // ───────────── 끌어 놓기·자판 ─────────────

    /// <summary>끌어 오는 그림 파일들(없으면 빈 목록).</summary>
    private static List<string> DraggedImages(IDataTransfer data)
        => data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(IsAttachable).ToList() ?? [];

    private static bool IsAttachable(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DraggedImages(e.DataTransfer).Count == 0) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = DraggedImages(e.DataTransfer);
        if (files.Count == 0) return;
        e.Handled = true;
        foreach (var f in files) AttachPath(f);
        FocusInput();
    }

    /// <summary>칩 자판(지우기·건너뛰기)과 ⌘/Ctrl+V 그림 붙여넣기(글자 붙여넣기는 입력란이 그대로 함).</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleChipKey(e)) return;
        if (e.Key == Key.V && Ui.HasCommand(e.KeyModifiers)) _ = PasteImageAsync();
    }

    /// <summary>
    /// 칩 자판: 커서 바로 뒤(Backspace)·앞(Delete)의 칩을 한 번에 지우고, 칩 앞뒤에서 왼쪽·오른쪽 화살표는 칩을 건너뛴다. 처리했으면 true.
    /// </summary>
    private bool HandleChipKey(KeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.Delete or Key.Left or Key.Right)) return false;
        if (Input.SelectionStart != Input.SelectionEnd || (e.KeyModifiers & KeyModifiers.Shift) != 0) return false;
        var text = Input.Text ?? "";
        int caret = Input.CaretIndex;
        bool backward = e.Key is Key.Back or Key.Left;
        if (ChatImageTokens.At(text, caret, backward) is not { } t) return false;
        e.Handled = true;
        if (e.Key is Key.Left or Key.Right)
        {
            SetInputText(text, backward ? t.Start : t.End);
            return true;
        }
        DeleteChip(text, t);
        Sync();
        return true;
    }

    // ───────────── 칩 맞추기 ─────────────

    private void SetInputText(string text, int caret)
    {
        _syncingInput = true;
        try
        {
            Input.Text = text;
            Input.SelectionStart = Input.SelectionEnd = Input.CaretIndex = Math.Clamp(caret, 0, text.Length);
        }
        finally
        {
            _syncingInput = false;
        }
    }

    /// <summary>입력란 글이 바뀜: 입력란이 커서까지 다 옮긴 뒤 칩과 첨부를 맞춘다(한 번만).</summary>
    private void PostSync()
    {
        if (_syncPosted) return;
        _syncPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncPosted = false;
            Sync();
        });
    }

    /// <summary>
    /// 글 안의 칩과 첨부를 맞춘다(<see cref="ChatImageTokens.Normalize"/>): 지워진 칩의 첨부는 빼고, 남은 칩을 글 순서대로 1부터 다시 매기고,
    /// 깨진 칩 조각은 지운다. 글이 바뀌면 커서를 같은 자리에 둔다. 그리고 칩 층을 다시 놓는다.
    /// </summary>
    public void Sync()
    {
        var text = Input.Text ?? "";
        int caret = Math.Clamp(Input.CaretIndex, 0, text.Length);
        var normalized = ChatImageTokens.Normalize(text, _attachments.Count, ChipLabel, out var order, ref caret);
        var kept = order.Select(i => _attachments[i]).ToList();
        _attachments.Clear();
        _attachments.AddRange(kept);
        if (normalized != text) SetInputText(normalized, caret);
        UpdateChipLayer();
    }

    /// <summary>커서·고른 범위가 칩 안에 걸치면 칩 끝으로 넓힌다(커서만이면 가까운 쪽 끝으로) — 칩은 한 덩어리.</summary>
    private void SnapSelection()
    {
        var tokens = ChatImageTokens.Find(Input.Text);
        if (tokens.Count == 0) return;
        int s = Input.SelectionStart, e = Input.SelectionEnd;
        int ns = s, ne = e;
        foreach (var t in tokens)
        {
            if (s == e)
            {
                if (s > t.Start && s < t.End) ns = ne = s - t.Start < t.End - s ? t.Start : t.End;
                continue;
            }
            int lo = Math.Min(s, e), hi = Math.Max(s, e);
            if (lo > t.Start && lo < t.End) lo = t.Start;
            if (hi > t.Start && hi < t.End) hi = t.End;
            (ns, ne) = s <= e ? (Math.Min(ns, lo), Math.Max(ne, hi)) : (Math.Max(ns, hi), Math.Min(ne, lo));
        }
        if (ns == s && ne == e) return;
        Dispatcher.UIThread.Post(() =>
        {
            var len = (Input.Text ?? "").Length;
            _syncingInput = true;
            try
            {
                Input.SelectionStart = Math.Clamp(ns, 0, len);
                Input.SelectionEnd = Math.Clamp(ne, 0, len);
            }
            finally
            {
                _syncingInput = false;
            }
            UpdateChipLayer();
        });
    }

    /// <summary>칩 하나를 글에서 지운다(그 첨부도 빠짐 — 크게 보기의 "첨부 빼기").</summary>
    private void RemoveChip(int number)
    {
        var text = Input.Text ?? "";
        if (ChatImageTokens.Find(text).FirstOrDefault(t => t.Number == number) is not { Length: > 0 } t) return;
        DeleteChip(text, t);
        Sync();
        FocusInput();
    }

    /// <summary>칩 글자를 지운다(넣을 때 붙인 사이 칸도 하나 — 앞뒤가 모두 공백이거나 글 끝이면). 커서는 칩이 있던 자리.</summary>
    private void DeleteChip(string text, ChatImageToken t)
    {
        int start = t.Start, end = t.End;
        bool spaceBefore = start == 0 || text[start - 1] == ' ', spaceAfter = end == text.Length || text[end] == ' ';
        if (spaceBefore && spaceAfter)
        {
            if (end < text.Length) end++;
            else if (start > 0) start--;
        }
        SetInputText(text.Remove(start, end - start), start);
    }

    /// <summary>
    /// 칩 층을 입력란 글의 칩 자리에 맞춘다: 칩 글자 범위(<see cref="TextPresenter.TextLayout"/>의 첫 줄 사각형)를 덮는 칩을 놓고, 입력란의 보이는 칸
    /// 밖은 자른다. 값이 같으면 건드리지 않아 배치가 되풀이되지 않는다.
    /// </summary>
    private void UpdateChipLayer()
    {
        var tokens = _presenter is null ? [] : ChatImageTokens.Find(Input.Text);
        while (_chipViews.Count > tokens.Count)
        {
            _chipLayer.Children.Remove(_chipViews[^1]);
            _chipViews.RemoveAt(_chipViews.Count - 1);
        }
        if (tokens.Count == 0 || _presenter is null) return;
        var layout = _presenter.TextLayout;
        int preedit = _presenter.PreeditText?.Length ?? 0;
        int caret = Input.CaretIndex;
        int selLo = Math.Min(Input.SelectionStart, Input.SelectionEnd), selHi = Math.Max(Input.SelectionStart, Input.SelectionEnd);
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (i == _chipViews.Count)
            {
                var view = NewChipView();
                _chipViews.Add(view);
                _chipLayer.Children.Add(view);
            }
            var chip = _chipViews[i];
            int start = preedit > 0 && t.Start >= caret ? t.Start + preedit : t.Start;
            var rects = layout.HitTestTextRange(start, t.Length).ToList();
            if (rects.Count == 0 || t.Number > _attachments.Count)
            {
                chip.IsVisible = false;
                continue;
            }
            var first = rects[0];
            var line = rects.Where(r => Math.Abs(r.Top - first.Top) < 0.5).Aggregate(first, (u, r) => u.Union(r));
            if (_presenter.TranslatePoint(line.TopLeft, _chipLayer) is not { } p)
            {
                chip.IsVisible = false;
                continue;
            }
            chip.IsVisible = true;
            // 칩은 칩 글자 범위를 덮는다: 가로는 글자 가장자리의 부드럽게 그린 픽셀까지(양쪽 1px 더), 세로는 위아래로 한 픽셀씩 더 커서
            // 미리보기가 줄 높이만큼 보인다(줄 사이 여백 안).
            double left = Math.Floor(p.X) - ChipBleed, right = Math.Ceiling(p.X + line.Width) + ChipBleed;
            SetIfChanged(chip, Canvas.LeftProperty, left);
            SetIfChanged(chip, Canvas.TopProperty, Math.Round(p.Y) - ChipBleed);
            SetIfChanged(chip, Layoutable.WidthProperty, Math.Max(1, right - left));
            SetIfChanged(chip, Layoutable.HeightProperty, Math.Max(1, Math.Round(line.Height) + 2 * ChipBleed));
            var brush = selLo < selHi && selLo <= t.Start && selHi >= t.End ? ChatImages.ChipSelectedBrush : ChatImages.ChipBrush;
            if (!ReferenceEquals(chip.Background, brush)) chip.Background = brush;
            var a = _attachments[t.Number - 1];
            // 미리보기 한 변 = 칩 안쪽 높이(테두리 1px씩) − 위아래 1px.
            double side = Math.Max(MinChipThumb, Math.Round(line.Height) + 2 * ChipBleed - 4);
            if (chip.Tag is not (int n, ChatAttachment shown, double shownSide) || n != t.Number || !ReferenceEquals(shown, a) || shownSide != side)
            {
                chip.Tag = (t.Number, a, side);
                chip.Child = ChatImages.ChipContent(t.Number, a.Thumb, side, Input.FontSize, a.PixelArt);
                ToolTip.SetTip(chip, ChatImages.ChipTip(a.Thumb));
            }
        }
        // 입력란의 보이는 칸 밖(스크롤로 가려진 줄)은 자른다.
        if (_inputScroll is not null && _inputScroll.TranslatePoint(default, _chipLayer) is { } o)
        {
            var clip = new Rect(o, _inputScroll.Viewport);
            if (_chipLayer.Clip is not RectangleGeometry g || g.Rect != clip) _chipLayer.Clip = new RectangleGeometry(clip);
        }
    }

    private static void SetIfChanged(Control c, StyledProperty<double> property, double value)
    {
        double old = c.GetValue(property);
        if (double.IsNaN(old) || Math.Abs(old - value) > 0.01) c.SetValue(property, value);
    }

    /// <summary>입력란 위 칩 하나(누르면 크게 보기 + 첨부 빼기).</summary>
    private Border NewChipView()
    {
        var chip = new Border
        {
            Background = ChatImages.ChipBrush,
            BorderBrush = AppTheme.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        chip.PointerPressed += (_, e) => e.Handled = true;
        chip.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || chip.Tag is not (int number, ChatAttachment a, double _)) return;
            e.Handled = true;
            ChatImages.ShowChipFlyout(chip, a.Thumb, () => RemoveChip(number));
        };
        return chip;
    }
}
