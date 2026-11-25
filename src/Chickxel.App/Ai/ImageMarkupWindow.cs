using System.Globalization;
using System.Text;
using Avalonia.VisualTree;
using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App.Ai;

/// <summary>
/// 대화 속 그림에 표시해 AI에게 고쳐 달라고 하는 창(모달이 아님): 위 도구 막대(그리기 · 댓글 · 지우기, 되돌리기), 가운데 그림(그 위에 부드러운 선과
/// 번호 붙은 댓글 표시를 그림), 아래 설명 입력 칸과 보내기 단추. 보내면 원본 그림과(표시가 있으면) 표시를 얹은 그림을 첨부하고,
/// 두 그림을 가리키는 칩과 댓글 목록(번호 · 그림 기준 위치 % · 글)을 설명 글 뒤에 붙인다.
/// 입력 칸 안 Enter = 보내기, Shift+Enter = 줄바꿈, Esc = 창 닫기(댓글을 쓰는 중이면 그 편집만 취소).
/// </summary>
internal sealed class ImageMarkupWindow : Window
{
    /// <summary>이 길이(긴 변, px) 이하면 픽셀 아트로 본다(최근접으로 그리고, 픽셀 아트 첨부로 보냄).</summary>
    private const int PixelArtSide = 256;

    private readonly Bitmap _image;
    private readonly Func<string, IReadOnlyList<ChatAttachment>, bool> _send;
    private readonly MarkupBoard _board;
    private readonly TextBox _input;
    /// <summary>도구 단추(<see cref="MarkupTool"/> 순서).</summary>
    private readonly ToggleButton[] _tools;
    private readonly Button _undo;

    /// <param name="image">대화 속 그림(픽셀 아트 작은 그림일 수도, 1024px 생성 원본일 수도).</param>
    /// <param name="send">보내기: (저장 모양 글 — 그림 칩 토큰 포함, 첨부 목록) → 보냈으면(또는 대기 줄에 넣었으면) true(창을 닫음), 아니면 false(창 유지).</param>
    public ImageMarkupWindow(Bitmap image, Func<string, IReadOnlyList<ChatAttachment>, bool> send)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(send);
        _image = image;
        _send = send;
        Title = UiStrings.ImageMarkup_Title;
        Width = 780;
        Height = 760;
        MinWidth = 460;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _board = new MarkupBoard(image, IsPixelArt(image));
        _tools =
        [
            ToolButton(VectorIcon.Pencil, UiStrings.ImageMarkup_Draw, UiStrings.ImageMarkup_DrawTip, MarkupTool.Draw),
            ToolButton(VectorIcon.MessageSquare, UiStrings.ImageMarkup_Comment, UiStrings.ImageMarkup_CommentTip, MarkupTool.Comment),
            ToolButton(VectorIcon.Eraser, UiStrings.ImageMarkup_Erase, UiStrings.ImageMarkup_EraseTip, MarkupTool.Erase),
        ];
        _undo = Ui.IconButton(VectorIcon.Undo2, UiStrings.ImageMarkup_UndoTip, (_, _) => _board.Undo());
        _undo.CornerRadius = new CornerRadius(12);
        _undo.IsEnabled = false;
        _board.Changed += () => _undo.IsEnabled = _board.CanUndo;
        SelectTool(MarkupTool.Draw);

        var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        toolRow.Children.AddRange(_tools);
        toolRow.Children.Add(new Border { Width = 1, Background = AppTheme.BorderBrush, Margin = new Thickness(4, 3) });
        toolRow.Children.Add(_undo);
        var info = Ui.InfoBadge(UiStrings.ImageMarkup_Info);
        info.VerticalAlignment = VerticalAlignment.Center;
        info.Margin = new Thickness(4, 0, 2, 0);
        toolRow.Children.Add(info);
        var toolbar = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = toolRow,
        };
        // 도구 막대 오른쪽 빈자리: 원본 그림 복사·저장(아이콘 단추 — 표시는 넣지 않음, 대화 속 그림 우클릭과 같은 그림).
        var copy = Ui.IconButton(VectorIcon.Copy, UiStrings.Preview_Menu_CopyImage, (_, _) =>
        {
            if (ChatImages.ToPixelImage(_image) is { } img) ImageActions.Copy(this, img);
        });
        var save = Ui.IconButton(VectorIcon.Download, UiStrings.Image_SaveMenu, (_, _) =>
        {
            if (ChatImages.ToPixelImage(_image) is { } img) ImageActions.SaveAs(this, img, "chickxel-chat.png");
        });
        var fileButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { copy, save } };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumn(toolbar, 1);
        Grid.SetColumn(fileButtons, 2);
        top.Children.Add(toolbar);
        top.Children.Add(fileButtons);
        DockPanel.SetDock(top, Dock.Top);

        _input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            MaxHeight = 140,
            Watermark = UiStrings.ImageMarkup_Watermark,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_input, ScrollBarVisibility.Auto);
        _input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        var sendButton = Ui.Button(VectorIcon.ArrowUp, UiStrings.ImageMarkup_Send, (_, _) => Send()).Tip(UiStrings.ImageMarkup_SendTip);
        sendButton.Background = AppTheme.AccentBrush;
        sendButton.Foreground = Brushes.White;
        sendButton.VerticalAlignment = VerticalAlignment.Bottom;
        sendButton.Margin = new Thickness(6, 0, 0, 0);
        Ui.KeepFillOnHover(sendButton);
        Grid.SetColumn(sendButton, 1);
        var bottom = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
            Children = { _input, sendButton },
        };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var boardFrame = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = _board,
        };
        Content = new DockPanel { Margin = new Thickness(10), Children = { top, bottom, boardFrame } };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Background);
    }

    /// <summary>긴 변이 <see cref="PixelArtSide"/> 이하인 작은 그림(픽셀 아트로 봄).</summary>
    private static bool IsPixelArt(Bitmap image) => Math.Max(image.PixelSize.Width, image.PixelSize.Height) <= PixelArtSide;

    /// <summary>도구 막대의 도구 단추(한 번에 하나만 켜짐).</summary>
    private ToggleButton ToolButton(VectorIcon icon, string text, string tip, MarkupTool tool)
    {
        var button = new ToggleButton
        {
            Content = Ui.IconLabel(icon, text),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        }.Tip(tip);
        button.Click += (_, _) => SelectTool(tool);
        return button;
    }

    /// <summary>도구를 고르고 그 단추만 켠다(켜진 단추를 다시 눌러도 꺼지지 않음).</summary>
    private void SelectTool(MarkupTool tool)
    {
        for (int i = 0; i < _tools.Length; i++) _tools[i].IsChecked = i == (int)tool;
        _board.Tool = tool;
    }

    /// <summary>입력 칸 Enter = 보내기(Shift+Enter = 줄바꿈 — 입력 칸이 처리).</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Shift) != 0) return;
        e.Handled = true;
        // 한글 조합 중이던 글자가 먼저 들어가도록 한 박자 늦게 보낸다.
        Dispatcher.UIThread.Post(Send, DispatcherPriority.Background);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        // 입력 칸 밖에서 Ctrl+Z = 표시 되돌리기(입력 칸 안은 글 되돌리기).
        else if (e.Key == Key.Z && Ui.HasCommand(e.KeyModifiers) && (e.KeyModifiers & KeyModifiers.Shift) == 0
                 && FocusManager?.GetFocusedElement() is not TextBox)
        {
            e.Handled = true;
            _board.Undo();
        }
    }

    /// <summary>
    /// 보내기: 표시도 글도 없으면 아무것도 안 함. 첨부 = 원본(+ 표시가 있으면 표시를 얹은 그림), 글 = 사용자 글 + (표시가 있으면) 두 그림 칩 줄과 댓글 목록,
    /// 표시가 없으면 원본 칩 + 사용자 글. 받는 쪽이 받으면 창을 닫는다.
    /// </summary>
    private void Send()
    {
        _board.CommitEdit();
        var note = (_input.Text ?? "").Trim();
        bool marked = _board.HasMarks;
        if (!marked && note.Length == 0) return;
        var original = ChatImageTokens.Token(ChatImageTokens.StoredLabel, 1);
        var attachments = new List<ChatAttachment> { OriginalAttachment() };
        string text;
        if (!marked) text = original + " " + note;
        else
        {
            attachments.Add(_board.RenderMarked());
            var sb = new StringBuilder();
            if (note.Length > 0) sb.Append(note).Append("\n\n");
            sb.Append(string.Format(CultureInfo.CurrentCulture, UiStrings.ImageMarkup_ImagesLine,
                original, ChatImageTokens.Token(ChatImageTokens.StoredLabel, 2)));
            var comments = _board.Comments;
            if (comments.Count > 0)
            {
                sb.Append("\n\n").Append(UiStrings.ImageMarkup_CommentsHeader);
                for (int i = 0; i < comments.Count; i++)
                    sb.Append('\n').Append(string.Format(CultureInfo.CurrentCulture, UiStrings.ImageMarkup_CommentLine,
                        i + 1, Percent(comments[i].At.X), Percent(comments[i].At.Y), comments[i].Text));
            }
            text = sb.ToString();
        }
        if (_send(text, attachments))
        {
            Close();
            return;
        }
        // 받지 않았으면 이번에 만든 미리보기를 놓는다(원본 비트맵은 대화 쪽 것이라 두고).
        foreach (var a in attachments)
            if (!ReferenceEquals(a.Thumb, _image)) a.Thumb.Dispose();
    }

    /// <summary>그림 기준 0~1 좌표 → 정수 %(반올림).</summary>
    private static int Percent(double v) => (int)Math.Round(v * 100, MidpointRounding.AwayFromZero);

    /// <summary>원본 그림 첨부(픽셀 그림으로 읽지 못하면 비트맵을 그대로 PNG로).</summary>
    private ChatAttachment OriginalAttachment()
    {
        bool pixelArt = IsPixelArt(_image);
        return ChatImages.ToPixelImage(_image) is { } img
            ? ChatImages.Attachment(img, pixelArt)
            : new ChatAttachment(ChatImages.ToPng(_image), _image, pixelArt);
    }

    private enum MarkupTool
    {
        Draw,
        Comment,
        Erase,
    }

    /// <summary>댓글 표시 하나: 자리(그림 기준 0~1)와 글. 번호는 목록 순서(1부터).</summary>
    private sealed class MarkupComment(Avalonia.Point at)
    {
        public Avalonia.Point At { get; } = at;
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// 그림 칸: 창 크기에 맞춰 비율 그대로 맞춘 그림(체커 바탕, 작은 그림은 최근접 정수 배) 위에 선과 번호 원을 직접 그리고, 끌기·누르기로 표시를 더하거나 지운다.
    /// 선 점과 댓글 자리는 그림 기준 0~1 좌표라 창 크기가 바뀌어도 제자리에 있다. 그리기는 맨 아래 자식 <see cref="Surface"/>가 하고(Panel의 Render는 봉인됨),
    /// 댓글 입력 칸은 그 위 자식(표시 옆에 놓음)이다.
    /// </summary>
    private sealed class MarkupBoard : Panel
    {
        /// <summary>판의 그림을 그리는 자식(판 전체 크기).</summary>
        private sealed class Surface(MarkupBoard board) : Control
        {
            public override void Render(DrawingContext g) => board.Paint(g);
        }

        /// <summary>그림 둘레 여백, 화면 기준 선 굵기·번호 원 반지름·번호 글자 크기·지우개 반지름·댓글 풍선 최대 폭(DIP).</summary>
        private const double Pad = 10, StrokeWidth = 4, MarkerRadius = 11, MarkerFont = 12, EraseRadius = 8, BubbleMaxWidth = 260;
        /// <summary>표시를 얹은 그림: 작은 그림을 키울 긴 변 목표, 선 굵기·원 크기가 화면의 1배가 되는 긴 변.</summary>
        private const double ExportSide = 1024, ExportUnitSide = 640;
        /// <summary>새 점을 더할 최소 이동(DIP).</summary>
        private const double MinStep = 1.5;

        private static readonly IBrush MarkBrush = AppTheme.Brush(Color.FromRgb(255, 59, 48));
        private static readonly IPen ScreenStrokePen = StrokePen(1);
        private static readonly IPen ScreenOutlinePen = OutlinePen(1);
        private static readonly IPen ErasePen = AppTheme.Pen(AppTheme.TextColor, 1);
        private static readonly IPen BubblePen = AppTheme.Pen(AppTheme.BorderColor, 1);

        private readonly Bitmap _image;
        private readonly bool _pixelArt;
        /// <summary>선(점 = 그림 기준 0~1, 그린 순서).</summary>
        private readonly List<List<Avalonia.Point>> _strokes = [];
        private readonly List<MarkupComment> _comments = [];
        /// <summary>되돌리기 동작(마지막 것이 위).</summary>
        private readonly Stack<Action> _undo = new();
        private readonly Border _editor;
        private readonly Surface _surface;
        private readonly TextBox _editorText;
        /// <summary>지금 긋는 선.</summary>
        private List<Avalonia.Point>? _drawing;
        /// <summary>지우개로 끄는 중: 이번 끌기에서 지운 것을 되살리는 동작(지운 순서).</summary>
        private List<Action>? _erased;
        private Avalonia.Point _lastErase;
        /// <summary>글을 쓰는 댓글과, 이번에 새로 놓은 것인지.</summary>
        private MarkupComment? _editing;
        private bool _editingIsNew;
        /// <summary>마우스가 올라간 댓글(없으면 −1)과 마우스 자리.</summary>
        private int _hover = -1;
        private Avalonia.Point? _pointer;
        private MarkupTool _tool;

        public MarkupBoard(Bitmap image, bool pixelArt)
        {
            _image = image;
            _pixelArt = pixelArt;
            Focusable = true;
            ClipToBounds = true;
            MinHeight = 160;
            _editorText = new TextBox { Width = 220, Watermark = UiStrings.ImageMarkup_CommentWatermark };
            _editorText.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
            _editorText.LostFocus += (_, _) =>
            {
                // 다른 곳을 누르면 확정. 누른 자리에서 새 편집이 시작됐을 수 있으니 한 박자 뒤에 같은 댓글일 때만.
                var editing = _editing;
                Dispatcher.UIThread.Post(() =>
                {
                    if (editing is not null && ReferenceEquals(_editing, editing) && !_editorText.IsFocused) CommitEdit();
                }, DispatcherPriority.Background);
            };
            _editor = new Border
            {
                Background = AppTheme.PanelBrush,
                BorderBrush = MarkBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Cursor = new Cursor(StandardCursorType.Arrow),
                IsVisible = false,
                Child = _editorText,
            };
            _surface = new Surface(this);
            Children.Add(_surface);
            Children.Add(_editor);
            Tool = MarkupTool.Draw;
        }

        /// <summary>표시가 바뀜(되돌리기 단추 켜고 끄기).</summary>
        public event Action? Changed;

        public bool CanUndo => _undo.Count > 0;

        public bool HasMarks => _strokes.Count > 0 || _comments.Count > 0;

        public IReadOnlyList<MarkupComment> Comments => _comments;

        public MarkupTool Tool
        {
            get => _tool;
            set
            {
                CommitEdit();
                _tool = value;
                Cursor = new Cursor(value == MarkupTool.Comment ? StandardCursorType.Hand : StandardCursorType.Cross);
                _surface.InvalidateVisual();
            }
        }

        private static IPen StrokePen(double unit)
            => new Pen(MarkBrush, StrokeWidth * unit) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }.ToImmutable();

        private static IPen OutlinePen(double unit) => new Pen(Brushes.White, 2 * unit).ToImmutable();

        // ───────────── 자리 ─────────────

        /// <summary>그림이 놓이는 자리: 여백 안에 비율 그대로 맞춤(작은 그림을 키울 때는 정수 배 — 픽셀 크기가 고르게).</summary>
        private Rect ImageArea(Avalonia.Size size)
        {
            var px = _image.PixelSize;
            if (px.Width <= 0 || px.Height <= 0) return default;
            double fit = Math.Min((size.Width - 2 * Pad) / px.Width, (size.Height - 2 * Pad) / px.Height);
            if (fit <= 0) return default;
            if (_pixelArt && fit >= 1) fit = Math.Floor(fit);
            double w = px.Width * fit, h = px.Height * fit;
            return new Rect(Math.Round((size.Width - w) / 2), Math.Round((size.Height - h) / 2), w, h);
        }

        private static Avalonia.Point ToView(Rect area, Avalonia.Point n) => new(area.X + n.X * area.Width, area.Y + n.Y * area.Height);

        private static Avalonia.Point ToImage(Rect area, Avalonia.Point v)
            => new(Math.Clamp((v.X - area.X) / area.Width, 0, 1), Math.Clamp((v.Y - area.Y) / area.Height, 0, 1));

        private static double Distance(Avalonia.Point a, Avalonia.Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>점 <paramref name="p"/>에서 선분 a–b까지 거리.</summary>
        private static double DistanceToSegment(Avalonia.Point p, Avalonia.Point a, Avalonia.Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 <= 0) return Distance(p, a);
            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
            return Distance(p, new Avalonia.Point(a.X + t * dx, a.Y + t * dy));
        }

        /// <summary>화면 자리 <paramref name="at"/>의 댓글 번호 원(위에 그린 것 먼저, 없으면 −1).</summary>
        private int CommentAt(Rect area, Avalonia.Point at)
        {
            for (int i = _comments.Count - 1; i >= 0; i--)
                if (Distance(ToView(area, _comments[i].At), at) <= MarkerRadius + 2) return i;
            return -1;
        }

        private bool IsInEditor(object? source) => source is Visual v && (ReferenceEquals(v, _editor) || _editor.IsVisualAncestorOf(v));

        // ───────────── 배치 ─────────────

        protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
        {
            _surface.Measure(availableSize);
            _editor.Measure(Avalonia.Size.Infinity);
            return default;
        }

        /// <summary>댓글 입력 칸은 쓰는 댓글 원 오른쪽(모자라면 왼쪽)에, 패널 밖으로 나가지 않게 둔다.</summary>
        protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
        {
            _surface.Arrange(new Rect(finalSize));
            if (_editing is { } c && _editor.IsVisible)
            {
                var at = ToView(ImageArea(finalSize), c.At);
                var d = _editor.DesiredSize;
                double x = at.X + MarkerRadius + 6;
                if (x + d.Width > finalSize.Width) x = at.X - MarkerRadius - 6 - d.Width;
                x = Math.Clamp(x, 0, Math.Max(0, finalSize.Width - d.Width));
                double y = Math.Clamp(at.Y - d.Height / 2, 0, Math.Max(0, finalSize.Height - d.Height));
                _editor.Arrange(new Rect(x, y, d.Width, d.Height));
            }
            return finalSize;
        }

        // ───────────── 그리기 ─────────────

        private void Paint(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, bounds);
            var area = ImageArea(Bounds.Size);
            if (area.Width <= 0 || area.Height <= 0) return;
            AppTheme.DrawChecker(g, area, 6);
            using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _pixelArt ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                g.DrawImage(_image, new Rect(_image.Size), area);
            DrawMarks(g, area, 1, ScreenStrokePen, ScreenOutlinePen);
            if (_tool == MarkupTool.Erase && _pointer is { } p) g.DrawEllipse(null, ErasePen, p, EraseRadius, EraseRadius);
            if (_hover >= 0 && _hover < _comments.Count && !ReferenceEquals(_comments[_hover], _editing) && _comments[_hover].Text.Length > 0)
                DrawBubble(g, bounds, ToView(area, _comments[_hover].At), _comments[_hover].Text);
        }

        /// <summary>선과 번호 원을 <paramref name="area"/>(그림 자리)에 그린다. <paramref name="unit"/> = 화면 기준 크기의 배수.</summary>
        private void DrawMarks(DrawingContext g, Rect area, double unit, IPen strokePen, IPen outlinePen)
        {
            foreach (var stroke in _strokes) DrawStroke(g, area, stroke, strokePen, StrokeWidth * unit);
            double r = MarkerRadius * unit;
            for (int i = 0; i < _comments.Count; i++)
            {
                var center = ToView(area, _comments[i].At);
                g.DrawEllipse(MarkBrush, outlinePen, center, r, r);
                var number = AppTheme.Text((i + 1).ToString(CultureInfo.InvariantCulture), Brushes.White, MarkerFont * unit, bold: true);
                g.DrawText(number, new Avalonia.Point(center.X - number.Width / 2, center.Y - number.Height / 2));
            }
        }

        /// <summary>선 하나: 점 사이를 중점 2차 곡선으로 이어 부드럽게(점 하나면 동그란 점).</summary>
        private static void DrawStroke(DrawingContext g, Rect area, List<Avalonia.Point> points, IPen pen, double width)
        {
            var first = ToView(area, points[0]);
            if (points.Count == 1)
            {
                g.DrawEllipse(MarkBrush, null, first, width / 2, width / 2);
                return;
            }
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(first, false);
                for (int i = 1; i < points.Count - 1; i++)
                {
                    var p = ToView(area, points[i]);
                    var next = ToView(area, points[i + 1]);
                    ctx.QuadraticBezierTo(p, new Avalonia.Point((p.X + next.X) / 2, (p.Y + next.Y) / 2));
                }
                ctx.LineTo(ToView(area, points[^1]));
                ctx.EndFigure(false);
            }
            g.DrawGeometry(null, pen, geometry);
        }

        /// <summary>마우스를 올린 댓글의 글 풍선(원 오른쪽, 모자라면 왼쪽).</summary>
        private static void DrawBubble(DrawingContext g, Rect bounds, Avalonia.Point at, string text)
        {
            var ft = AppTheme.Text(text, AppTheme.TextBrush);
            ft.MaxTextWidth = BubbleMaxWidth;
            double w = ft.Width + 12, h = ft.Height + 8;
            double x = at.X + MarkerRadius + 6;
            if (x + w > bounds.Right - 2) x = at.X - MarkerRadius - 6 - w;
            x = Math.Clamp(x, 2, Math.Max(2, bounds.Right - w - 2));
            double y = Math.Clamp(at.Y - h / 2, 2, Math.Max(2, bounds.Bottom - h - 2));
            g.DrawRectangle(AppTheme.PanelBrush, BubblePen, new Rect(x, y, w, h), 4, 4);
            g.DrawText(ft, new Avalonia.Point(x + 6, y + 4));
        }

        /// <summary>
        /// 표시를 얹은 그림 첨부: 작은 그림은 긴 변이 <see cref="ExportSide"/> 안팎이 되게 정수 배 최근접으로 키우고, 큰 그림은 그대로(긴 변이
        /// <see cref="ChatImages.AttachMaxSide"/>를 넘으면 줄임) 그 위에 그 크기에 맞춘 굵기로 선과 번호 원을 그린다.
        /// </summary>
        public ChatAttachment RenderMarked()
        {
            var px = _image.PixelSize;
            int longSide = Math.Max(1, Math.Max(px.Width, px.Height));
            double k = _pixelArt ? Math.Max(1, Math.Round(ExportSide / longSide))
                : longSide > ChatImages.AttachMaxSide ? (double)ChatImages.AttachMaxSide / longSide : 1;
            int w = Math.Max(1, (int)Math.Round(px.Width * k)), h = Math.Max(1, (int)Math.Round(px.Height * k));
            var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            using (var g = bitmap.CreateDrawingContext())
            {
                var area = new Rect(0, 0, w, h);
                using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _pixelArt ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                    g.DrawImage(_image, new Rect(_image.Size), area);
                double unit = Math.Max(1, Math.Max(w, h) / ExportUnitSide);
                DrawMarks(g, area, unit, StrokePen(unit), OutlinePen(unit));
            }
            using var ms = new MemoryStream();
            bitmap.Save(ms);
            return new ChatAttachment(ms.ToArray(), bitmap, PixelArt: false);
        }

        // ───────────── 마우스 ─────────────

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.Handled || IsInEditor(e.Source)) return;
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            CommitEdit();
            var area = ImageArea(Bounds.Size);
            if (area.Width <= 0 || area.Height <= 0) return;
            var at = point.Position;
            e.Handled = true;
            Focus();
            switch (_tool)
            {
                case MarkupTool.Draw:
                    if (!area.Contains(at)) return;
                    _drawing = [ToImage(area, at)];
                    _strokes.Add(_drawing);
                    e.Pointer.Capture(this);
                    break;
                case MarkupTool.Comment:
                    int hit = CommentAt(area, at);
                    if (hit >= 0) BeginEdit(_comments[hit], isNew: false);
                    else if (area.Contains(at))
                    {
                        var comment = new MarkupComment(ToImage(area, at));
                        _comments.Add(comment);
                        BeginEdit(comment, isNew: true);
                    }
                    break;
                case MarkupTool.Erase:
                    _erased = [];
                    _lastErase = at;
                    EraseAt(area, at);
                    e.Pointer.Capture(this);
                    break;
            }
            _hover = -1;
            _surface.InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var area = ImageArea(Bounds.Size);
            var at = e.GetPosition(this);
            _pointer = at;
            bool redraw = _tool == MarkupTool.Erase;
            if (area.Width > 0 && area.Height > 0)
            {
                if (_drawing is not null)
                {
                    if (Distance(ToView(area, _drawing[^1]), at) >= MinStep)
                    {
                        _drawing.Add(ToImage(area, at));
                        redraw = true;
                    }
                }
                else if (_erased is not null)
                {
                    // 빠르게 끌어도 지나간 자리를 빠뜨리지 않게 지난 자리부터 잘게 나눠 지운다.
                    double length = Distance(_lastErase, at);
                    int steps = Math.Max(1, (int)Math.Ceiling(length / (EraseRadius / 2)));
                    for (int i = 1; i <= steps; i++)
                    {
                        double t = (double)i / steps;
                        EraseAt(area, new Avalonia.Point(_lastErase.X + (at.X - _lastErase.X) * t, _lastErase.Y + (at.Y - _lastErase.Y) * t));
                    }
                    _lastErase = at;
                }
                int hover = _drawing is null && _erased is null && !IsInEditor(e.Source) ? CommentAt(area, at) : -1;
                if (hover != _hover)
                {
                    _hover = hover;
                    redraw = true;
                }
            }
            if (redraw) _surface.InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_drawing is null && _erased is null) return;
            FinishGesture();
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            FinishGesture();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _pointer = null;
            _hover = -1;
            _surface.InvalidateVisual();
        }

        /// <summary>긋기·지우기 끌기를 끝내고 되돌리기에 넣는다.</summary>
        private void FinishGesture()
        {
            if (_drawing is { } stroke)
            {
                _drawing = null;
                Push(() => _strokes.Remove(stroke));
            }
            else if (_erased is { } restore)
            {
                _erased = null;
                // 지운 순서의 거꾸로 되살리면 저마다 지울 때의 자리로 돌아간다.
                if (restore.Count > 0) Push(() => { for (int i = restore.Count - 1; i >= 0; i--) restore[i](); });
            }
            _surface.InvalidateVisual();
        }

        /// <summary>지우개 자리에 걸린 선(선 전체)과 댓글 표시를 지운다.</summary>
        private void EraseAt(Rect area, Avalonia.Point at)
        {
            if (_erased is null) return;
            double reach = StrokeWidth / 2 + EraseRadius;
            for (int i = _strokes.Count - 1; i >= 0; i--)
            {
                var stroke = _strokes[i];
                if (!StrokeHit(area, stroke, at, reach)) continue;
                int index = i;
                _strokes.RemoveAt(i);
                _erased.Add(() => _strokes.Insert(index, stroke));
            }
            for (int i = _comments.Count - 1; i >= 0; i--)
            {
                if (Distance(ToView(area, _comments[i].At), at) > MarkerRadius + EraseRadius / 2) continue;
                var comment = _comments[i];
                int index = i;
                _comments.RemoveAt(i);
                _erased.Add(() => _comments.Insert(index, comment));
            }
        }

        /// <summary>선(점을 이은 꺾은선)이 화면 자리 <paramref name="at"/>에서 <paramref name="reach"/> 안에 지나가는지.</summary>
        private static bool StrokeHit(Rect area, List<Avalonia.Point> stroke, Avalonia.Point at, double reach)
        {
            var prev = ToView(area, stroke[0]);
            if (stroke.Count == 1) return Distance(prev, at) <= reach;
            for (int j = 1; j < stroke.Count; j++)
            {
                var next = ToView(area, stroke[j]);
                if (DistanceToSegment(at, prev, next) <= reach) return true;
                prev = next;
            }
            return false;
        }

        // ───────────── 되돌리기 ─────────────

        private void Push(Action undo)
        {
            _undo.Push(undo);
            Changed?.Invoke();
        }

        /// <summary>마지막 선·댓글 추가나 지우기를 되돌린다(댓글을 쓰는 중이면 그 편집만 취소).</summary>
        public void Undo()
        {
            if (_editing is not null)
            {
                CancelEdit();
                return;
            }
            if (_drawing is not null || _erased is not null || _undo.Count == 0) return;
            _undo.Pop()();
            _hover = -1;
            _surface.InvalidateVisual();
            Changed?.Invoke();
        }

        // ───────────── 댓글 편집 ─────────────

        /// <summary>댓글 원 옆에 입력 칸을 띄워 글을 쓰게 한다.</summary>
        private void BeginEdit(MarkupComment comment, bool isNew)
        {
            _editing = comment;
            _editingIsNew = isNew;
            _editorText.Text = comment.Text;
            _editor.IsVisible = true;
            InvalidateArrange();
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_editing, comment)) return;
                _editorText.Focus();
                _editorText.SelectAll();
            }, DispatcherPriority.Background);
        }

        /// <summary>입력 칸 Enter = 확정(한글 조합이 끝나게 한 박자 늦게), Esc = 취소.</summary>
        private void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelEdit();
                Focus();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var editing = _editing;
                Dispatcher.UIThread.Post(() =>
                {
                    if (editing is null || !ReferenceEquals(_editing, editing)) return;
                    CommitEdit();
                    Focus();
                }, DispatcherPriority.Background);
            }
        }

        private void EndEdit()
        {
            _editing = null;
            _editor.IsVisible = false;
        }

        /// <summary>쓰던 댓글을 확정한다: 글이 있으면 그 글로(새 댓글이면 되돌리기에 넣음), 비었으면 표시를 없앤다(있던 댓글이면 되돌리기로 되살림).</summary>
        public void CommitEdit()
        {
            if (_editing is not { } comment) return;
            var text = (_editorText.Text ?? "").ReplaceLineEndings(" ").Trim();
            bool isNew = _editingIsNew;
            EndEdit();
            if (text.Length > 0)
            {
                comment.Text = text;
                if (isNew) Push(() => _comments.Remove(comment));
            }
            else
            {
                int index = _comments.IndexOf(comment);
                if (index >= 0)
                {
                    _comments.RemoveAt(index);
                    if (!isNew) Push(() => _comments.Insert(index, comment));
                }
            }
            _surface.InvalidateVisual();
        }

        /// <summary>쓰던 댓글을 취소한다: 새로 놓은 것이면 표시도 없애고, 있던 것이면 글을 그대로 둔다.</summary>
        private void CancelEdit()
        {
            if (_editing is not { } comment) return;
            bool isNew = _editingIsNew;
            EndEdit();
            if (isNew) _comments.Remove(comment);
            _surface.InvalidateVisual();
        }
    }
}
