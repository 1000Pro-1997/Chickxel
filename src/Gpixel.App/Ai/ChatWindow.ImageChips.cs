using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Gpixel.Core.IO;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

// 입력란의 그림 칩([이미지 #1]): 붙여 넣기·끌어 놓기·"이미지…"·"캔버스 첨부"로 넣은 그림은 커서 자리에 칩 글자(ChatImageTokens — 보이지 않는
// 표시 글자로 감싼 [이미지 #N])로 들어가고, 입력란 위 층(_chipLayer)이 그 글자 자리를 칩(체커 위 작은 정사각 미리보기 + 강조색 #N)으로 덮는다. 칩은 한 덩어리라
// Backspace·Delete 한 번에 지워지고(그 첨부도 빠짐) 커서가 칩 안에 들어가지 않으며, 지우거나 옮기면 번호를 글 순서대로 다시 매긴다.
// 마우스를 올리면 미리보기, 누르면 크게 보기(빼기 단추). 보낸 말풍선에도 글 사이에 같은 칩(작은 미리보기 + 번호)이 들어간다.
public sealed partial class ChatWindow
{
    /// <summary>칩 미리보기 풍선의 긴 변, 크게 보기의 긴 변 최대.</summary>
    private const int ChipTipSide = 160, ChipViewSide = 420;
    /// <summary>말풍선 칩 안 미리보기 한 변.</summary>
    private const int BubbleChipThumb = 18;
    /// <summary>입력란 칩 미리보기의 최소 한 변, 칩이 글자 줄 위아래로 넘치는 거리(줄 사이 여백 안).</summary>
    private const int MinChipThumb = 10, ChipBleed = 1;

    private static readonly IBrush ChipBrush = AppTheme.Brush(Color.FromRgb(40, 54, 76));
    private static readonly IBrush ChipSelectedBrush = AppTheme.Brush(Color.FromRgb(46, 86, 140));

    /// <summary>
    /// 칩 안 내용(입력란·말풍선 같은 모양): 정사각 미리보기(<paramref name="side"/>, 체커 바탕 위 비율 그대로 — 픽셀 아트·작은 그림은 최근접으로 픽셀 그대로,
    /// 큰 사진은 부드럽게 줄임; 그림이 없으면 그림 아이콘)와 강조색 굵은 "#N".
    /// </summary>
    private static StackPanel ChipContent(int number, Bitmap? thumb, double side, double fontSize, bool pixelArt)
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

    /// <summary>작은 칩 미리보기 바탕의 잔 체커(칸 3px — 투명한 곳이 보이게).</summary>
    private sealed class SmallCheckerBackground : Decorator
    {
        public override void Render(DrawingContext g) => AppTheme.DrawChecker(g, new Rect(Bounds.Size), 3);
    }

    /// <summary>미리보기 풍선 내용(긴 변 <see cref="ChipTipSide"/>, 작은 그림은 정수 배 최근접).</summary>
    private static Control ChipTip(Bitmap thumb)
    {
        var px = thumb.PixelSize;
        double k = Math.Min((double)ChipTipSide / Math.Max(1, px.Width), (double)ChipTipSide / Math.Max(1, px.Height));
        if (k > 1) k = Math.Max(1, Math.Floor(k));
        var image = new Image { Source = thumb, Width = Math.Max(1, px.Width * k), Height = Math.Max(1, px.Height * k), Stretch = Stretch.Fill };
        if (k > 1) RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return new CheckerBackground { Child = image };
    }

    /// <summary>칩을 누르면: 그림을 크게(긴 변 <see cref="ChipViewSide"/> 안, 작은 그림은 정수 배 최근접) 보이는 풍선. <paramref name="remove"/>가 있으면 "첨부 빼기" 단추.</summary>
    private static void ShowChipFlyout(Control anchor, Bitmap image, Action? remove)
    {
        var px = image.PixelSize;
        double k = Math.Min((double)ChipViewSide / Math.Max(1, px.Width), (double)ChipViewSide / Math.Max(1, px.Height));
        if (k > 1) k = Math.Max(1, Math.Floor(k));
        var view = new Image { Source = image, Width = Math.Max(1, px.Width * k), Height = Math.Max(1, px.Height * k), Stretch = Stretch.Fill };
        if (k > 1) RenderOptions.SetBitmapInterpolationMode(view, BitmapInterpolationMode.None);
        var stack = new StackPanel { Spacing = 6, Children = { new CheckerBackground { Child = view, HorizontalAlignment = HorizontalAlignment.Left } } };
        AttachImageMenu(view, image);
        var flyout = new Flyout { Placement = PlacementMode.Top, Content = stack };
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
    private Control BubbleChip(int number, Bitmap? thumb)
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
            chip.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                ShowChipFlyout(chip, thumb, null);
            };
        }
        return chip;
    }

    private sealed partial class ChatTab
    {
        /// <summary>입력란 글을 그리는 부분(글자 자리 계산)과 그 스크롤.</summary>
        private TextPresenter? _presenter;
        private ScrollViewer? _inputScroll;
        /// <summary>입력란 글을 코드가 바꾸는 중(바뀜 이벤트로 다시 맞추지 않게).</summary>
        private bool _syncingInput, _syncPosted;
        /// <summary>층에 놓인 칩(글 안 칩 순서).</summary>
        private readonly List<Border> _chipViews = [];

        /// <summary>입력란 칩: 글·고른 범위 바뀜, 끌어 놓기, 칩 층 자리 맞추기를 건다(생성자에서 한 번).</summary>
        private void SetupInlineImages()
        {
            _input.PropertyChanged += (_, e) =>
            {
                if (_syncingInput) return;
                if (e.Property == TextBox.TextProperty) PostSync();
                else if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty) SnapSelection();
            };
            _input.TemplateApplied += (_, e) =>
            {
                _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
                if (_presenter is null) return;
                _presenter.LayoutUpdated += (_, _) => UpdateChipLayer();
                _inputScroll = _presenter.FindAncestorOfType<ScrollViewer>();
                if (_inputScroll is not null) _inputScroll.ScrollChanged += (_, _) => UpdateChipLayer();
            };
            DragDrop.SetAllowDrop(_input, true);
            _input.AddHandler(DragDrop.DragEnterEvent, OnInputDragOver, RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
            _input.AddHandler(DragDrop.DragOverEvent, OnInputDragOver, RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
            _input.AddHandler(DragDrop.DropEvent, OnInputDrop, RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        }

        private static string ChipLabel => UiStrings.Chat_Image_Label;

        /// <summary>끌어 오는 그림 파일들(없으면 빈 목록).</summary>
        private static List<string> DraggedImages(IDataTransfer data)
            => data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(IsAttachable).ToList() ?? [];

        private static bool IsAttachable(string path)
            => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);

        private void OnInputDragOver(object? sender, DragEventArgs e)
        {
            if (DraggedImages(e.DataTransfer).Count == 0) return;
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void OnInputDrop(object? sender, DragEventArgs e)
        {
            var files = DraggedImages(e.DataTransfer);
            if (files.Count == 0) return;
            e.Handled = true;
            foreach (var f in files) AttachPath(f);
            FocusInput();
        }

        /// <summary>
        /// 그림 하나를 커서 자리에 칩으로 넣는다(고른 글이 있으면 그 자리를 바꿈). 앞뒤 글자와 붙지 않게 사이를 한 칸 띄우고, 커서는 칩 뒤로.
        /// </summary>
        private void AddAttachment(byte[] png, Bitmap thumb, bool pixelArt)
        {
            SyncInlineImages();
            _attachments.Add(new Attachment(png, thumb, pixelArt));
            var text = _input.Text ?? "";
            int a = Math.Clamp(_input.SelectionStart, 0, text.Length), b = Math.Clamp(_input.SelectionEnd, 0, text.Length);
            int start = Math.Min(a, b), end = Math.Max(a, b);
            if (start == end) start = end = Math.Clamp(_input.CaretIndex, 0, text.Length);
            string before = text[..start], after = text[end..];
            var insert = (before.Length > 0 && !char.IsWhiteSpace(before[^1]) ? " " : "")
                         + ChatImageTokens.Token(ChipLabel, _attachments.Count)
                         + (after.Length == 0 || !char.IsWhiteSpace(after[0]) ? " " : "");
            SetInputText(before + insert + after, start + insert.Length);
            SyncInlineImages();
            FocusInput();
        }

        /// <summary>입력란의 글과 그림을 모두 비운다.</summary>
        private void ClearAttachments()
        {
            _attachments.Clear();
            if ((_input.Text ?? "").Length > 0 && ChatImageTokens.HasTokens(_input.Text)) SetInputText(ChatImageTokens.WithoutTokens(_input.Text ?? ""), 0);
            UpdateChipLayer();
        }

        /// <summary>마지막 메시지 고치기: 저장한 글(칩 이름 "Image")과 그 첨부를 입력란에 되살린다. 첨부가 없으면(지난 실행의 대화) 칩은 빠진다.</summary>
        private void RestoreInput(string storedText, IReadOnlyList<Attachment> attachments)
        {
            _attachments.Clear();
            _attachments.AddRange(attachments);
            var text = ChatImageTokens.Relabel(storedText, ChipLabel);
            SetInputText(text, text.Length);
            SyncInlineImages();
        }

        private void SetInputText(string text, int caret)
        {
            _syncingInput = true;
            try
            {
                _input.Text = text;
                _input.SelectionStart = _input.SelectionEnd = _input.CaretIndex = Math.Clamp(caret, 0, text.Length);
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
                SyncInlineImages();
            });
        }

        /// <summary>
        /// 글 안의 칩과 첨부를 맞춘다(<see cref="ChatImageTokens.Normalize"/>): 지워진 칩의 첨부는 빼고, 남은 칩을 글 순서대로 1부터 다시 매기고,
        /// 깨진 칩 조각은 지운다. 글이 바뀌면 커서를 같은 자리에 둔다. 그리고 칩 층을 다시 놓는다.
        /// </summary>
        private void SyncInlineImages()
        {
            var text = _input.Text ?? "";
            int caret = Math.Clamp(_input.CaretIndex, 0, text.Length);
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
            var tokens = ChatImageTokens.Find(_input.Text);
            if (tokens.Count == 0) return;
            int s = _input.SelectionStart, e = _input.SelectionEnd;
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
                var len = (_input.Text ?? "").Length;
                _syncingInput = true;
                try
                {
                    _input.SelectionStart = Math.Clamp(ns, 0, len);
                    _input.SelectionEnd = Math.Clamp(ne, 0, len);
                }
                finally
                {
                    _syncingInput = false;
                }
                UpdateChipLayer();
            });
        }

        /// <summary>
        /// 칩 자판: 커서 바로 뒤(Backspace)·앞(Delete)의 칩을 한 번에 지우고, 칩 앞뒤에서 왼쪽·오른쪽 화살표는 칩을 건너뛴다. 처리했으면 true.
        /// </summary>
        private bool HandleChipKey(KeyEventArgs e)
        {
            if (e.Key is not (Key.Back or Key.Delete or Key.Left or Key.Right)) return false;
            if (_input.SelectionStart != _input.SelectionEnd || (e.KeyModifiers & KeyModifiers.Shift) != 0) return false;
            var text = _input.Text ?? "";
            int caret = _input.CaretIndex;
            bool backward = e.Key is Key.Back or Key.Left;
            if (ChatImageTokens.At(text, caret, backward) is not { } t) return false;
            e.Handled = true;
            if (e.Key is Key.Left or Key.Right)
            {
                SetInputText(text, backward ? t.Start : t.End);
                return true;
            }
            DeleteChip(text, t);
            SyncInlineImages();
            return true;
        }

        /// <summary>칩 하나를 글에서 지운다(그 첨부도 빠짐 — 크게 보기의 "첨부 빼기").</summary>
        private void RemoveChip(int number)
        {
            var text = _input.Text ?? "";
            if (ChatImageTokens.Find(text).FirstOrDefault(t => t.Number == number) is not { Length: > 0 } t) return;
            DeleteChip(text, t);
            SyncInlineImages();
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
            var tokens = _presenter is null ? [] : ChatImageTokens.Find(_input.Text);
            while (_chipViews.Count > tokens.Count)
            {
                _chipLayer.Children.Remove(_chipViews[^1]);
                _chipViews.RemoveAt(_chipViews.Count - 1);
            }
            if (tokens.Count == 0 || _presenter is null) return;
            var layout = _presenter.TextLayout;
            int preedit = _presenter.PreeditText?.Length ?? 0;
            int caret = _input.CaretIndex;
            int selLo = Math.Min(_input.SelectionStart, _input.SelectionEnd), selHi = Math.Max(_input.SelectionStart, _input.SelectionEnd);
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
                var brush = selLo < selHi && selLo <= t.Start && selHi >= t.End ? ChipSelectedBrush : ChipBrush;
                if (!ReferenceEquals(chip.Background, brush)) chip.Background = brush;
                var a = _attachments[t.Number - 1];
                // 미리보기 한 변 = 칩 안쪽 높이(테두리 1px씩) − 위아래 1px.
                double side = Math.Max(MinChipThumb, Math.Round(line.Height) + 2 * ChipBleed - 4);
                if (chip.Tag is not (int n, Attachment shown, double shownSide) || n != t.Number || !ReferenceEquals(shown, a) || shownSide != side)
                {
                    chip.Tag = (t.Number, a, side);
                    chip.Child = ChipContent(t.Number, a.Thumb, side, _input.FontSize, a.PixelArt);
                    ToolTip.SetTip(chip, ChipTip(a.Thumb));
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
                Background = ChipBrush,
                BorderBrush = AppTheme.AccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            chip.PointerPressed += (_, e) => e.Handled = true;
            chip.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || chip.Tag is not (int number, Attachment a, double _)) return;
                e.Handled = true;
                ShowChipFlyout(chip, a.Thumb, () => RemoveChip(number));
            };
            return chip;
        }

        /// <summary>그림 파일을 칩으로 넣는다(읽지 못하면 알림). PNG이고 크지 않으면 그대로, 아니면 PNG로(긴 변이 크면 줄여서) 보낸다.</summary>
        private void AttachPath(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Ui.Error(_w, string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_AttachFile_FB523C7B58, ex.Message));
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
                Ui.Error(_w, UiStrings.S_ChatWindow_AttachFile_0469DC1AFF);
                return;
            }
            bool isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
            bool small = Math.Max(bmp.PixelSize.Width, bmp.PixelSize.Height) <= AttachMaxSide;
            AddAttachment(isPng && small ? bytes : ToPng(bmp), bmp, pixelArt: false);
        }
    }
}
