using System.Runtime.InteropServices;
using Avalonia.Platform;
using Chickxel.Core.Community;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 레벨 보상 픽셀 그림(<see cref="IPixelArt"/> — 배지·테두리·효과·카드)을 정수 배(<see cref="Scale"/>)로 또렷하게 보이는 컨트롤.
/// 컨트롤 크기는 그림의 대상 칸(<see cref="IPixelArt.Box"/>)만큼이고(<see cref="Fill"/>면 주어진 크기), 대상 칸이 컨트롤 가운데에 오게 그리며
/// 빛·날개·반짝이처럼 칸 밖으로 번지는 부분은 컨트롤 밖에 그린다(레이아웃을 밀지 않음 — 마우스는 통과).
/// 움직이는 그림은 모두 타이머 하나로 같은 프레임을 그리고(화면에 붙어 있고 보이는 것만), 마지막 것이 떨어지면 타이머가 선다.
/// </summary>
public sealed class PixelArtView : Control
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(RewardArt.FrameMs) };
    private static readonly List<PixelArtView> Running = new();
    private static int _frame;

    private readonly Color32[] _pixels;
    private readonly int[] _premul;
    private WriteableBitmap? _bitmap;

    static PixelArtView() => Timer.Tick += (_, _) =>
    {
        _frame++;
        foreach (var view in Running.ToArray())
            if (view.IsEffectivelyVisible) view.Draw();
    };

    public PixelArtView(IPixelArt art, int scale)
    {
        Art = art;
        Scale = Math.Max(1, scale);
        _pixels = new Color32[art.Width * art.Height];
        _premul = new int[_pixels.Length];
        IsHitTestVisible = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public IPixelArt Art { get; }
    public int Scale { get; }

    /// <summary>true면 주어진 크기를 다 쓰고(대상 칸은 가운데), false면 대상 칸 크기만 원한다.</summary>
    public bool Fill { get; init; }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
        => Fill ? new Avalonia.Size(0, 0) : new Avalonia.Size(Art.Box.Width * Scale, Art.Box.Height * Scale);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _bitmap ??= new WriteableBitmap(new PixelSize(Art.Width, Art.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        Draw();
        if (Art.Frames <= 1) return;
        Running.Add(this);
        Timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Running.Remove(this);
        if (Running.Count == 0) Timer.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void Draw()
    {
        if (_bitmap is null) return;
        Art.Render(_frame, _pixels);
        for (int i = 0; i < _pixels.Length; i++)
        {
            var c = _pixels[i];
            int a = c.A;
            _premul[i] = a << 24 | c.R * a / 255 << 16 | c.G * a / 255 << 8 | c.B * a / 255;
        }
        using (var fb = _bitmap.Lock())
            for (int y = 0; y < Art.Height; y++)
                Marshal.Copy(_premul, y * Art.Width, fb.Address + y * fb.RowBytes, Art.Width);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null) return;
        var box = Art.Box;
        // 대상 칸을 컨트롤 가운데에(정수 픽셀로)
        double left = Math.Round((Bounds.Width - box.Width * Scale) / 2) - box.X * Scale;
        double top = Math.Round((Bounds.Height - box.Height * Scale) / 2) - box.Y * Scale;
        context.DrawImage(_bitmap, new Rect(0, 0, Art.Width, Art.Height), new Rect(left, top, Art.Width * Scale, Art.Height * Scale));
    }
}
