using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 픽셀 캔버스: 현재 프레임 한 장(아틀라스면 고른 스프라이트 하나, 보이는 레이어 합성)을 정수 배율(1~64)로 보여 주고,
/// 현재 레이어의 셀에 그린다. 체커 배경, 픽셀/타일 격자, 타일 모드(3×3 반복), 대칭 안내선, 피벗, 9-slice 안내선(아틀라스),
/// 선택 영역, 양파 껍질, H 누르는 동안 이전 프레임 보기.
/// </summary>
public sealed class PixelCanvas : Control
{
    public static readonly int[] ZoomLevels = [1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 32, 40, 48, 64];

    private enum DragMode { None, Pan, Stroke, Shape, Fill, Picker, Marquee, MoveSelection, MoveLayer, Pivot }

    private readonly EditorContext _ctx;
    private Bitmap? _floatingBmp;
    private int _zoom = 8;
    private Point _offset;
    private readonly System.Windows.Forms.Timer _antsTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer _peekTimer = new() { Interval = 40 };
    private int _antsPhase;

    // 드래그 상태
    private DragMode _drag;
    private MouseButtons _dragButton;
    private Point _dragStartScreen, _lastScreen;
    private Point _startPx, _lastPx;
    private PixelImage? _strokeBase;
    private PixelPerfectStroke? _ppStroke;
    private Color32 _strokeColor, _strokeColor2, _replaceTarget;
    private bool _strokeLighten;
    private HashSet<Point>? _visited;
    private ToolKind _strokeTool;
    private Pivot _pivotBefore;
    private bool _dragMoved;
    private bool _altUsed;
    private Point? _hoverPx;
    private Point? _lastStrokeEnd;

    public PixelCanvas(EditorContext ctx)
    {
        _ctx = ctx;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Theme.BackColor;
        _antsTimer.Tick += (_, _) =>
        {
            _antsPhase = (_antsPhase + 1) % 8;
            if (_ctx.Selection.HasSelection) Invalidate();
        };
        _antsTimer.Start();
        _peekTimer.Tick += (_, _) =>
        {
            if (NativeMethods.IsKeyDown(Keys.H)) return;
            _peekTimer.Stop();
            PeekPrevious = false;
        };

        ctx.ProjectReplaced += (_, _) =>
        {
            UpdateFloatingBitmap();
            _lastStrokeEnd = null;
            // 프로젝트가 열리며 캔버스가 막 보이게 된 경우 배치가 끝난 뒤 가운데 놓는다.
            if (IsHandleCreated) BeginInvoke(CenterImage);
            else CenterImage();
            Invalidate();
        };
        ctx.ViewChanged += (_, _) =>
        {
            // 아틀라스에서 크기가 다른 스프라이트로 바뀌면 가운데 다시 놓는다.
            var size = new Size(W, H);
            if (_ctx.HasProject && size != _shownSize)
            {
                _shownSize = size;
                CenterImage();
            }
            Invalidate();
        };
        ctx.Selection.Changed += (_, _) => { UpdateFloatingBitmap(); Invalidate(); };
        ctx.OnionSkinChanged += (_, _) => Invalidate();
        ctx.ToolChanged += (_, _) => { UpdateCursor(); Invalidate(); };
        ctx.ToolOptionsChanged += (_, _) => Invalidate();
    }

    // ───────────── 보기 옵션 ─────────────

    private bool _showPixelGrid = true, _showPivot = true, _showTileGrid, _tiledMode, _peekPrevious;
    private Size _shownSize;
    private Size _tileSize = new(16, 16);
    private Color _pixelGridColor = Color.FromArgb(64, 255, 255, 255), _tileGridColor = Color.FromArgb(150, 90, 200, 255);

    public bool ShowPixelGrid { get => _showPixelGrid; set { _showPixelGrid = value; Invalidate(); } }
    public bool ShowPivot { get => _showPivot; set { _showPivot = value; Invalidate(); } }
    public bool ShowTileGrid { get => _showTileGrid; set { _showTileGrid = value; Invalidate(); } }
    /// <summary>타일 모드: 그림을 3×3으로 반복해 보여 주고, 가장자리를 넘는 그리기는 반대편으로 이어진다.</summary>
    public bool TiledMode { get => _tiledMode; set { _tiledMode = value; Invalidate(); } }
    public Size TileSize { get => _tileSize; set { _tileSize = new Size(Math.Max(1, value.Width), Math.Max(1, value.Height)); Invalidate(); } }
    public Color PixelGridColor { get => _pixelGridColor; set { _pixelGridColor = value; Invalidate(); } }
    public Color TileGridColor { get => _tileGridColor; set { _tileGridColor = value; Invalidate(); } }

    /// <summary>H를 누르고 있는 동안 이전 프레임을 보여 준다.</summary>
    public bool PeekPrevious
    {
        get => _peekPrevious;
        set
        {
            if (_peekPrevious == value) return;
            _peekPrevious = value;
            if (value) _peekTimer.Start();
            Invalidate();
        }
    }

    public int Zoom => _zoom;

    /// <summary>확대율이 바뀜.</summary>
    public event EventHandler? ZoomChanged;
    /// <summary>커서 아래 픽셀 좌표가 바뀜(그림 밖이면 null).</summary>
    public event EventHandler<Point?>? HoverChanged;
    /// <summary>상태 표시줄에 보일 짧은 알림.</summary>
    public event EventHandler<string>? Message;

    private int W => _ctx.CanvasWidth;
    private int H => _ctx.CanvasHeight;

    // ───────────── 좌표·확대 ─────────────

    private Rectangle ImageScreenRect => new(_offset.X, _offset.Y, W * _zoom, H * _zoom);

    private Point ScreenToPixel(Point p) => new(FloorDiv(p.X - _offset.X, _zoom), FloorDiv(p.Y - _offset.Y, _zoom));

    private PointF ScreenToImageF(Point p) => new((p.X - _offset.X) / (float)_zoom, (p.Y - _offset.Y) / (float)_zoom);

    private PointF ImageToScreen(float x, float y) => new(_offset.X + x * _zoom, _offset.Y + y * _zoom);

    private Rectangle ImageToScreen(Rectangle r) => new(_offset.X + r.X * _zoom, _offset.Y + r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    /// <summary>타일 모드면 그림 밖 좌표를 안으로 감는다.</summary>
    private Point Wrap(Point p) => _tiledMode && W > 0 && H > 0 ? new Point(PixelOps.Mod(p.X, W), PixelOps.Mod(p.Y, H)) : p;

    public void SetZoom(int zoom, Point? anchor = null)
    {
        zoom = Math.Clamp(zoom, 1, 64);
        if (zoom == _zoom) return;
        var a = anchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        float ix = (a.X - _offset.X) / (float)_zoom, iy = (a.Y - _offset.Y) / (float)_zoom;
        _zoom = zoom;
        _offset = new Point((int)Math.Round(a.X - ix * zoom), (int)Math.Round(a.Y - iy * zoom));
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ZoomStep(int direction, Point? anchor = null)
    {
        int next = direction > 0
            ? ZoomLevels.FirstOrDefault(z => z > _zoom, 64)
            : ZoomLevels.LastOrDefault(z => z < _zoom, 1);
        SetZoom(next, anchor);
    }

    /// <summary>화면에 맞추기(Ctrl+0).</summary>
    public void FitToWindow()
    {
        if (!_ctx.HasProject) return;
        int n = _tiledMode ? 3 : 1;
        int fit = (int)Math.Floor(Math.Min((ClientSize.Width - 24) / (double)(W * n), (ClientSize.Height - 24) / (double)(H * n)));
        _zoom = Math.Clamp(fit, 1, 64);
        CenterOffset();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>프로젝트를 연 직후: 현재 확대율로 가운데 놓되 화면보다 크면 맞춘다.</summary>
    private void CenterImage()
    {
        if (!_ctx.HasProject) return;
        _shownSize = new Size(W, H);
        if (W * _zoom > ClientSize.Width - 24 || H * _zoom > ClientSize.Height - 24)
        {
            FitToWindow();
            return;
        }
        CenterOffset();
    }

    private void CenterOffset()
        => _offset = new Point((ClientSize.Width - W * _zoom) / 2, (ClientSize.Height - H * _zoom) / 2);

    /// <summary>설정에서 읽은 확대율을 그대로 적용한다(이벤트 없음).</summary>
    public void InitZoom(int zoom) => _zoom = Math.Clamp(zoom, 1, 64);

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _ctx.HasProject && IsHandleCreated) BeginInvoke(CenterImage);
    }

    // ───────────── 그리기 ─────────────

    private void UpdateFloatingBitmap()
    {
        _floatingBmp?.Dispose();
        _floatingBmp = _ctx.Selection.Floating is { } f ? ImageUtil.ToBitmap(f) : null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.BackColor);
        if (!_ctx.HasProject) return;
        var bmp = _ctx.DisplayBitmap!;
        if (_peekPrevious && _ctx.CurrentFrame > 0 && _ctx.FrameBitmap(_ctx.CurrentFrame - 1) is { } prev) bmp = prev;

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        if (_tiledMode)
        {
            for (int ty = -1; ty <= 1; ty++)
                for (int tx = -1; tx <= 1; tx++)
                {
                    if (tx == 0 && ty == 0) continue;
                    DrawImageAt(g, bmp, tx * W, ty * H);
                }
            // 가운데(편집하는 칸)가 구분되도록 둘레를 살짝 어둡게.
            using var dim = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
            var region = new Region(ImageToScreen(new Rectangle(-W, -H, W * 3, H * 3)));
            region.Exclude(ImageScreenRect);
            g.FillRegion(dim, region);
        }
        DrawImageAt(g, bmp, 0, 0);
        if (_floatingBmp is not null && !_peekPrevious)
            g.DrawImage(_floatingBmp, ImageToScreen(_ctx.Selection.Bounds), new Rectangle(0, 0, _floatingBmp.Width, _floatingBmp.Height), GraphicsUnit.Pixel);
        if (!_peekPrevious) DrawOnionSkin(g);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        var src = VisibleSourceRect(0, 0);
        if (_showPixelGrid && _zoom >= 6) DrawPixelGrid(g, src);
        if (_showTileGrid) DrawTileGrid(g, src);
        var imgRect = ImageScreenRect;
        using (var border = new Pen(Theme.BorderColor))
            g.DrawRectangle(border, imgRect.X - 1, imgRect.Y - 1, imgRect.Width + 1, imgRect.Height + 1);
        DrawSymmetryGuides(g);
        DrawSpriteBorder(g);
        if (_showPivot || _ctx.Tool == ToolKind.Pivot) DrawPivot(g);
        if (_ctx.Selection.HasSelection) DrawMarquee(g, ImageToScreen(_ctx.Selection.Bounds));
        DrawBrushCursor(g);
        if (_peekPrevious)
            TextRenderer.DrawText(g, _ctx.CurrentFrame > 0 ? $"이전 프레임 {_ctx.CurrentFrame} 보는 중 (H)" : "이전 프레임이 없습니다",
                Theme.BoldFont, new Point(8, 8), Theme.WarningColor);
        if (!_ctx.CanEdit)
            TextRenderer.DrawText(g, "현재 레이어가 숨김/잠금 상태라 그릴 수 없습니다", Theme.UiFont, new Point(8, Height - 24), Theme.WarningColor);
    }

    /// <summary>그림을 (ox,oy) 이미지 좌표만큼 옮긴 위치에 보이는 부분만 그린다.</summary>
    private void DrawImageAt(Graphics g, Bitmap bmp, int ox, int oy)
    {
        var src = VisibleSourceRect(ox, oy);
        if (src.Width <= 0 || src.Height <= 0) return;
        var dest = ImageToScreen(new Rectangle(src.X + ox, src.Y + oy, src.Width, src.Height));
        var oldOffset = g.PixelOffsetMode;
        g.PixelOffsetMode = PixelOffsetMode.Default;
        Theme.DrawChecker(g, Rectangle.Intersect(dest, ClientRectangle), 8);
        g.PixelOffsetMode = oldOffset;
        g.DrawImage(bmp, dest, src, GraphicsUnit.Pixel);
    }

    private Rectangle VisibleSourceRect(int ox, int oy)
    {
        var tl = ScreenToPixel(Point.Empty);
        var br = ScreenToPixel(new Point(ClientSize.Width, ClientSize.Height));
        return Rectangle.Intersect(Rectangle.FromLTRB(tl.X - ox, tl.Y - oy, br.X + 1 - ox, br.Y + 1 - oy), new Rectangle(0, 0, W, H));
    }

    private void DrawPixelGrid(Graphics g, Rectangle src)
    {
        var c = _pixelGridColor;
        using var pen = new Pen(_zoom >= 12 ? c : Color.FromArgb(Math.Max(16, c.A * 3 / 4), c));
        for (int x = src.Left; x <= src.Right; x++)
        {
            float sx = _offset.X + x * _zoom;
            g.DrawLine(pen, sx, _offset.Y + src.Top * _zoom, sx, _offset.Y + src.Bottom * _zoom);
        }
        for (int y = src.Top; y <= src.Bottom; y++)
        {
            float sy = _offset.Y + y * _zoom;
            g.DrawLine(pen, _offset.X + src.Left * _zoom, sy, _offset.X + src.Right * _zoom, sy);
        }
    }

    private void DrawTileGrid(Graphics g, Rectangle src)
    {
        using var pen = new Pen(_tileGridColor);
        int tw = _tileSize.Width, th = _tileSize.Height;
        for (int x = src.Left / tw * tw; x <= src.Right; x += tw)
            g.DrawLine(pen, _offset.X + x * _zoom, _offset.Y + src.Top * _zoom, _offset.X + x * _zoom, _offset.Y + src.Bottom * _zoom);
        for (int y = src.Top / th * th; y <= src.Bottom; y += th)
            g.DrawLine(pen, _offset.X + src.Left * _zoom, _offset.Y + y * _zoom, _offset.X + src.Right * _zoom, _offset.Y + y * _zoom);
    }

    private void DrawSymmetryGuides(Graphics g)
    {
        if (!_ctx.MirrorX && !_ctx.MirrorY) return;
        var r = ImageScreenRect;
        using var pen = new Pen(Color.FromArgb(200, 255, 90, 160), 1.5f) { DashStyle = DashStyle.Dash };
        if (_ctx.MirrorX)
        {
            float x = r.X + r.Width / 2f;
            g.DrawLine(pen, x, r.Top - 10, x, r.Bottom + 10);
        }
        if (_ctx.MirrorY)
        {
            float y = r.Y + r.Height / 2f;
            g.DrawLine(pen, r.Left - 10, y, r.Right + 10, y);
        }
    }

    /// <summary>아틀라스: 현재 스프라이트의 9-slice 테두리 안내선(점선).</summary>
    private void DrawSpriteBorder(Graphics g)
    {
        if (_ctx.CurrentAtlasSprite is not { } s || s.Border.IsNone) return;
        var r = ImageScreenRect;
        var b = s.Border;
        using var pen = new Pen(Color.FromArgb(220, 90, 220, 120), 1f) { DashStyle = DashStyle.Dash };
        if (b.Left > 0) g.DrawLine(pen, r.Left + b.Left * _zoom, r.Top, r.Left + b.Left * _zoom, r.Bottom);
        if (b.Right > 0) g.DrawLine(pen, r.Right - b.Right * _zoom, r.Top, r.Right - b.Right * _zoom, r.Bottom);
        if (b.Top > 0) g.DrawLine(pen, r.Left, r.Top + b.Top * _zoom, r.Right, r.Top + b.Top * _zoom);
        if (b.Bottom > 0) g.DrawLine(pen, r.Left, r.Bottom - b.Bottom * _zoom, r.Right, r.Bottom - b.Bottom * _zoom);
    }

    private void DrawPivot(Graphics g)
    {
        var (px, py) = _ctx.Pivot.ToLocalPixel(W, H);
        var p = ImageToScreen(px, py);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_ctx.Tool == ToolKind.Pivot ? Color.White : Color.FromArgb(255, 120, 80), 1.5f);
        using var back = new Pen(Color.FromArgb(160, 0, 0, 0), 3.5f);
        const float r = 5f;
        g.DrawEllipse(back, p.X - r, p.Y - r, r * 2, r * 2);
        g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
        g.DrawLine(pen, p.X - r - 4, p.Y, p.X + r + 4, p.Y);
        g.DrawLine(pen, p.X, p.Y - r - 4, p.X, p.Y + r + 4);
        g.SmoothingMode = old;
    }

    private void DrawMarquee(Graphics g, Rectangle r)
    {
        using var white = new Pen(Color.White);
        using var black = new Pen(Color.Black) { DashPattern = [4f, 4f], DashOffset = _antsPhase };
        g.DrawRectangle(white, r.X, r.Y, r.Width - 1, r.Height - 1);
        g.DrawRectangle(black, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    private static bool UsesBrush(ToolKind t)
        => t is ToolKind.Pencil or ToolKind.Eraser or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse
            or ToolKind.Dodge or ToolKind.Dither or ToolKind.ReplaceBrush;

    private void DrawBrushCursor(Graphics g)
    {
        if (_hoverPx is not { } p || _drag == DragMode.Pan || !UsesBrush(_ctx.Tool)) return;
        int size = _ctx.BrushSize, start = (size - 1) / 2;
        var r = ImageToScreen(new Rectangle(p.X - start, p.Y - start, size, size));
        using var pen = new Pen(Color.FromArgb(200, 255, 255, 255));
        using var pen2 = new Pen(Color.FromArgb(200, 0, 0, 0));
        g.DrawRectangle(pen2, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    private void DrawOnionSkin(Graphics g)
    {
        if (!_ctx.OnionSkin) return;
        int i = _ctx.CurrentFrame;
        if (i > 0 && _ctx.FrameBitmap(i - 1) is { } prev) DrawGhost(g, prev, 1f, 0.45f, 0.45f);
        if (i + 1 < _ctx.FrameCount && _ctx.FrameBitmap(i + 1) is { } next) DrawGhost(g, next, 0.45f, 0.75f, 1f);
    }

    private void DrawGhost(Graphics g, Bitmap bmp, float r, float gr, float b)
    {
        var src = VisibleSourceRect(0, 0);
        if (src.IsEmpty) return;
        var matrix = new ColorMatrix([
            [r, 0, 0, 0, 0],
            [0, gr, 0, 0, 0],
            [0, 0, b, 0, 0],
            [0, 0, 0, 0.3f, 0],
            [0, 0, 0, 0, 1],
        ]);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix);
        g.DrawImage(bmp, ImageToScreen(src), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
    }

    // ───────────── 입력 ─────────────

    protected override bool IsInputKey(Keys keyData)
        => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void WndProc(ref Message m)
    {
        const int WM_SYSKEYUP = 0x105;
        // Alt를 눌러 임시 스포이드를 쓴 뒤 Alt를 떼면 메뉴가 활성화되지 않도록 삼킨다.
        if (m.Msg == WM_SYSKEYUP && (int)m.WParam == (int)Keys.Menu && _altUsed)
        {
            _altUsed = false;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_ctx.HasProject) ZoomStep(Math.Sign(e.Delta), e.Location);
    }

    private static bool IsPaintTool(ToolKind t)
        => t is ToolKind.Pencil or ToolKind.Eraser or ToolKind.Fill or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse
            or ToolKind.Dodge or ToolKind.Dither or ToolKind.ReplaceBrush;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (!_ctx.HasProject || _drag != DragMode.None) return;
        _dragButton = e.Button;
        _dragStartScreen = _lastScreen = e.Location;
        _dragMoved = false;
        var px = ScreenToPixel(e.Location);
        _startPx = _lastPx = px;

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && NativeMethods.IsKeyDown(Keys.Space)))
        {
            _drag = DragMode.Pan;
            Cursor = Cursors.SizeAll;
            Capture = true;
            return;
        }
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;

        var tool = _ctx.Tool;
        var mods = ModifierKeys;
        if (IsPaintTool(tool) && (mods & (Keys.Alt | Keys.Control)) != 0)
        {
            // Alt 또는 Ctrl+클릭: 임시 스포이드
            tool = ToolKind.Picker;
            _altUsed = (mods & Keys.Alt) != 0;
        }
        if (IsPaintTool(tool) && !_ctx.CanEdit)
        {
            Message?.Invoke(this, "현재 레이어가 숨김/잠금 상태라 그릴 수 없습니다.");
            return;
        }
        Capture = true;
        switch (tool)
        {
            case ToolKind.Pencil:
            case ToolKind.Eraser:
            case ToolKind.Dodge:
            case ToolKind.Dither:
            case ToolKind.ReplaceBrush:
                BeginPixelStroke(tool, e.Button, Wrap(px));
                if ((mods & Keys.Shift) != 0 && _lastStrokeEnd is { } from)
                    foreach (var p in Raster.Line(from, px)) StampAt(p);   // Shift+클릭: 마지막 점에서 직선
                else StampAt(px);
                _ctx.NotifyPixels();
                _drag = DragMode.Stroke;
                break;
            case ToolKind.Line:
            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
                BeginPixelStroke(tool, e.Button, px);
                DrawShape(px);
                _drag = DragMode.Shape;
                break;
            case ToolKind.Fill:
                BeginPixelStroke(tool, e.Button, px);
                FillAt(Wrap(px));
                _drag = DragMode.Fill;
                break;
            case ToolKind.Picker:
                PickAt(Wrap(px), e.Button);
                _drag = DragMode.Picker;
                break;
            case ToolKind.Select:
                if (e.Button != MouseButtons.Left) break;
                if (_ctx.Selection.HasSelection && _ctx.Selection.Bounds.Contains(px)) BeginMoveSelection();
                else
                {
                    _ctx.CommitFloating();
                    _drag = DragMode.Marquee;
                }
                break;
            case ToolKind.Move:
                if (e.Button != MouseButtons.Left) break;
                if (_ctx.Selection.HasSelection) BeginMoveSelection();
                else BeginMoveLayer();
                break;
            case ToolKind.Pivot:
                if (e.Button != MouseButtons.Left) break;
                _pivotBefore = _ctx.Pivot;
                _drag = DragMode.Pivot;
                DragPivot(e.Location);
                break;
        }
        if (_drag == DragMode.None) Capture = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_ctx.HasProject) return;
        var px = ScreenToPixel(e.Location);
        var wrapped = Wrap(px);
        Point? hover = wrapped.X >= 0 && wrapped.Y >= 0 && wrapped.X < W && wrapped.Y < H ? wrapped : null;
        if (hover != _hoverPx)
        {
            _hoverPx = hover;
            HoverChanged?.Invoke(this, hover);
            if (_drag == DragMode.None) Invalidate();
        }
        if (Math.Abs(e.X - _dragStartScreen.X) + Math.Abs(e.Y - _dragStartScreen.Y) > 3) _dragMoved = true;

        switch (_drag)
        {
            case DragMode.None:
                UpdateCursor(px);
                return;
            case DragMode.Pan:
                _offset.Offset(e.X - _lastScreen.X, e.Y - _lastScreen.Y);
                _lastScreen = e.Location;
                Invalidate();
                return;
            case DragMode.Stroke:
                if (px == _lastPx) return;
                foreach (var p in Raster.Line(_lastPx, px).Skip(1)) StampAt(p);
                _ctx.NotifyPixels();
                break;
            case DragMode.Shape:
                if (px == _lastPx) return;
                DrawShape(px);
                break;
            case DragMode.Picker:
                PickAt(wrapped, _dragButton);
                break;
            case DragMode.Marquee:
                if (_dragMoved) _ctx.Selection.Select(_ctx.EditImageOrEmpty, Raster.RectFromPoints(_startPx, px, (ModifierKeys & Keys.Shift) != 0));
                break;
            case DragMode.MoveSelection:
                if (px == _lastPx) return;
                _ctx.Selection.MoveBy(_ctx.EditImageOrEmpty, px.X - _lastPx.X, px.Y - _lastPx.Y);
                _ctx.NotifyPixels();
                break;
            case DragMode.MoveLayer:
                if (px == _lastPx) return;
                PixelOps.Shift(_strokeBase!, _ctx.EditImage!, new Rectangle(0, 0, W, H), px.X - _startPx.X, px.Y - _startPx.Y, _ctx.MoveWrap);
                _ctx.NotifyPixels();
                break;
            case DragMode.Pivot:
                DragPivot(e.Location);
                break;
        }
        _lastPx = px;
        _lastScreen = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == DragMode.None || e.Button != _dragButton) return;
        var mode = _drag;
        _drag = DragMode.None;
        Capture = false;
        if (!_ctx.HasProject) return;
        switch (mode)
        {
            case DragMode.Pan:
                UpdateCursor(ScreenToPixel(e.Location));
                break;
            case DragMode.Stroke:
                _lastStrokeEnd = Wrap(_lastPx);
                EndPixelStroke();
                break;
            case DragMode.Shape:
            case DragMode.Fill:
                EndPixelStroke();
                break;
            case DragMode.MoveLayer:
                _ctx.CommitStroke("내용 이동");
                _strokeBase = null;
                break;
            case DragMode.Marquee:
                if (!_dragMoved)
                {
                    _ctx.Selection.Deselect(_ctx.EditImageOrEmpty);
                    Message?.Invoke(this, "선택 해제");
                }
                break;
            case DragMode.Pivot:
                _ctx.CommitPivot(_pivotBefore);
                break;
        }
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverPx is null) return;
        _hoverPx = null;
        HoverChanged?.Invoke(this, null);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (_drag == DragMode.None) return;
        // 드래그 중 포커스를 잃으면(대화상자 등) 지금까지를 확정한다.
        OnMouseUp(new MouseEventArgs(_dragButton, 0, _lastScreen.X, _lastScreen.Y, 0));
    }

    // ───────────── 그리기 도구 ─────────────

    private void BeginPixelStroke(ToolKind tool, MouseButtons button, Point start)
    {
        _ctx.CommitFloating();
        _ctx.BeginStroke();
        var img = _ctx.EditImage!;
        _strokeBase = img.Clone();
        _strokeTool = tool;
        bool right = button == MouseButtons.Right;
        _strokeColor = tool switch
        {
            ToolKind.Eraser => Color32.Transparent,
            _ when right && _ctx.RightButtonErases && tool is ToolKind.Pencil or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Fill
                => Color32.Transparent,
            _ => right ? _ctx.Secondary : _ctx.Primary,
        };
        _strokeColor2 = right ? _ctx.Primary : _ctx.Secondary;
        _strokeLighten = !right;
        _replaceTarget = img.Get(start.X, start.Y);
        _visited = tool == ToolKind.Dodge ? new HashSet<Point>() : null;
        _ppStroke = tool is ToolKind.Pencil or ToolKind.Eraser && _ctx.PixelPerfect && _ctx.BrushSize == 1 ? new PixelPerfectStroke() : null;
    }

    private void EndPixelStroke()
    {
        var label = _strokeTool switch
        {
            ToolKind.Pencil => "연필",
            ToolKind.Eraser => "지우개",
            ToolKind.Fill => "채우기",
            ToolKind.Line => "선",
            ToolKind.Rectangle => "사각형",
            ToolKind.Ellipse => "원",
            ToolKind.Dodge => _strokeLighten ? "밝게" : "어둡게",
            ToolKind.Dither => "디더링",
            _ => "색 바꾸기 브러시",
        };
        _ctx.CommitStroke(label);
        if (_strokeTool is not (ToolKind.Eraser or ToolKind.Dodge) && _strokeColor.A != 0) _ctx.PushRecentColor(_strokeColor);
        _strokeBase = null;
        _ppStroke = null;
        _visited = null;
    }

    /// <summary>점들을 타일 모드 감기 → 대칭 순으로 바꾼다.</summary>
    private List<Point> Targets(IEnumerable<Point> pts)
        => Raster.Mirror(pts.Select(Wrap), W, H, _ctx.MirrorX, _ctx.MirrorY);

    private void StampAt(Point p)
    {
        var img = _ctx.EditImage!;
        var clip = _ctx.Selection.Clip;
        switch (_strokeTool)
        {
            case ToolKind.Dither:
                foreach (var m in Targets(Raster.Brush(p.X, p.Y, _ctx.BrushSize)))
                    if (clip is null || clip.Value.Contains(m))
                        img.Set(m.X, m.Y, ((m.X + m.Y) & 1) == 0 ? _strokeColor : _strokeColor2);
                return;
            case ToolKind.Dodge:
                foreach (var m in Targets(Raster.Brush(p.X, p.Y, _ctx.BrushSize)))
                    if ((clip is null || clip.Value.Contains(m)) && img.Contains(m.X, m.Y) && _visited!.Add(m))
                        img.Set(m.X, m.Y, PixelOps.Dodge(img.Get(m.X, m.Y), _strokeLighten));
                return;
            case ToolKind.ReplaceBrush:
                foreach (var m in Targets(Raster.Brush(p.X, p.Y, _ctx.BrushSize)))
                    if ((clip is null || clip.Value.Contains(m)) && img.Contains(m.X, m.Y)
                        && Color32.SameVisible(_strokeBase!.Get(m.X, m.Y), _replaceTarget))
                        img.Set(m.X, m.Y, _strokeColor);
                return;
        }
        if (_ppStroke is not null)
        {
            var (draw, restore) = _ppStroke.Add(p);
            if (restore is { } r)
                foreach (var m in Targets([r]))
                    if (clip is null || clip.Value.Contains(m)) img.Set(m.X, m.Y, _strokeBase!.Get(m.X, m.Y));
            if (draw is { } d) Raster.Plot(img, Targets([d]), _strokeColor, clip);
            return;
        }
        Raster.Plot(img, Targets(Raster.Brush(p.X, p.Y, _ctx.BrushSize)), _strokeColor, clip);
    }

    private void DrawShape(Point end)
    {
        var img = _ctx.EditImage!;
        img.CopyFrom(_strokeBase!);
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        List<Point> pts;
        switch (_strokeTool)
        {
            case ToolKind.Line:
                if (shift) end = SnapAngle(_startPx, end);
                pts = WithBrush(Raster.Line(_startPx, end));
                break;
            case ToolKind.Rectangle:
            {
                var r = Raster.RectFromPoints(_startPx, end, shift);
                pts = _ctx.ShapeFilled ? Raster.RectFill(r) : WithBrush(Raster.RectOutline(r));
                break;
            }
            default:
            {
                var r = Raster.RectFromPoints(_startPx, end, shift);
                pts = _ctx.ShapeFilled ? Raster.EllipseFill(r) : WithBrush(Raster.EllipseOutline(r));
                break;
            }
        }
        Raster.Plot(img, Targets(pts), _strokeColor, _ctx.Selection.Clip);
        _ctx.NotifyPixels();
    }

    /// <summary>선 점들에 브러시 크기를 입힌다.</summary>
    private List<Point> WithBrush(List<Point> pts)
    {
        if (_ctx.BrushSize <= 1) return pts;
        var set = new HashSet<Point>();
        var list = new List<Point>();
        foreach (var p in pts)
            foreach (var b in Raster.Brush(p.X, p.Y, _ctx.BrushSize))
                if (set.Add(b)) list.Add(b);
        return list;
    }

    /// <summary>Shift: 0°/45°/90° 방향으로 맞춘다.</summary>
    private static Point SnapAngle(Point a, Point b)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        if (adx > ady * 2) return new Point(b.X, a.Y);
        if (ady > adx * 2) return new Point(a.X, b.Y);
        int d = Math.Max(adx, ady);
        return new Point(a.X + Math.Sign(dx) * d, a.Y + Math.Sign(dy) * d);
    }

    private void FillAt(Point p)
    {
        var img = _ctx.EditImage!;
        if (!img.Contains(p.X, p.Y)) return;
        int changed = 0;
        foreach (var m in Raster.Mirror([p], W, H, _ctx.MirrorX, _ctx.MirrorY))
            changed += Raster.FloodFill(img, m.X, m.Y, _strokeColor, _ctx.FillContiguous, _ctx.Selection.Clip);
        if (changed > 0) _ctx.NotifyPixels();
    }

    /// <summary>보이는 색(합성 결과)을 집는다.</summary>
    private void PickAt(Point p, MouseButtons button)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= W || p.Y >= H) return;
        var c = _ctx.CompositeColorAt(p.X, p.Y);
        if (_ctx.Selection.Floating is { } f && _ctx.Selection.Bounds.Contains(p))
        {
            var fc = f.Get(p.X - _ctx.Selection.Bounds.X, p.Y - _ctx.Selection.Bounds.Y);
            if (fc.A != 0) c = fc;
        }
        if (button == MouseButtons.Right) _ctx.Secondary = c;
        else _ctx.Primary = c;
    }

    // ───────────── 선택 영역·이동 ─────────────

    private void BeginMoveSelection()
    {
        if (!_ctx.CanEdit)
        {
            Message?.Invoke(this, "현재 레이어가 숨김/잠금 상태라 옮길 수 없습니다.");
            return;
        }
        _ctx.BeginSelectionEdit();
        _drag = DragMode.MoveSelection;
    }

    /// <summary>선택이 없을 때 이동 도구: 현재 셀 전체를 민다(순환 옵션).</summary>
    private void BeginMoveLayer()
    {
        if (!_ctx.CanEdit)
        {
            Message?.Invoke(this, "현재 레이어가 숨김/잠금 상태라 옮길 수 없습니다.");
            return;
        }
        _ctx.CommitFloating();
        _ctx.BeginStroke();
        _strokeBase = _ctx.EditImage!.Clone();
        _drag = DragMode.MoveLayer;
    }

    /// <summary>방향키로 선택 내용(없으면 이동 도구에서 레이어 내용)을 옮긴다.</summary>
    public bool Nudge(int dx, int dy)
    {
        if (!_ctx.HasProject || !_ctx.CanEdit) return false;
        if (_ctx.Selection.HasSelection && _ctx.Tool is ToolKind.Select or ToolKind.Move)
        {
            _ctx.BeginSelectionEdit();
            _ctx.Selection.MoveBy(_ctx.EditImage!, dx, dy);
            _ctx.NotifyPixels();
            return true;
        }
        if (_ctx.Tool != ToolKind.Move) return false;
        _ctx.BeginStroke();
        var img = _ctx.EditImage!;
        var copy = img.Clone();
        PixelOps.Shift(copy, img, new Rectangle(0, 0, W, H), dx, dy, _ctx.MoveWrap);
        _ctx.NotifyPixels();
        _ctx.CommitStroke("내용 이동");
        return true;
    }

    /// <summary>붙여넣을 위치: 선택이 있으면 그 자리, 없으면 화면에 보이는 영역의 왼쪽 위.</summary>
    public Point PasteLocation()
    {
        if (!_ctx.HasProject) return Point.Empty;
        if (_ctx.Selection.HasSelection) return _ctx.Selection.Bounds.Location;
        var src = VisibleSourceRect(0, 0);
        return src.IsEmpty ? Point.Empty : src.Location;
    }

    private void DragPivot(Point screen)
    {
        var f = ScreenToImageF(screen);
        // 반 픽셀 단위로 맞춘다.
        float fx = MathF.Round(f.X * 2) / 2, fy = MathF.Round(f.Y * 2) / 2;
        _ctx.PreviewPivot(Pivot.FromLocalPixel(fx, fy, W, H));
    }

    // ───────────── 커서 ─────────────

    private void UpdateCursor() => UpdateCursor(ScreenToPixel(PointToClient(MousePosition)));

    private void UpdateCursor(Point px)
    {
        if (!_ctx.HasProject) { Cursor = Cursors.Default; return; }
        if (NativeMethods.IsKeyDown(Keys.Space)) { Cursor = Cursors.Hand; return; }
        Cursor = _ctx.Tool switch
        {
            ToolKind.Move => Cursors.SizeAll,
            ToolKind.Select when _ctx.Selection.HasSelection && _ctx.Selection.Bounds.Contains(px) => Cursors.SizeAll,
            ToolKind.Pivot => Cursors.Hand,
            _ => Cursors.Cross,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _antsTimer.Dispose();
            _peekTimer.Dispose();
            _floatingBmp?.Dispose();
        }
        base.Dispose(disposing);
    }
}
