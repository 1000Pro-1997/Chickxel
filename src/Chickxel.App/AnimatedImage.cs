using Avalonia.Platform;
using SkiaSharp;

namespace Chickxel.App;

/// <summary>
/// 움직이는 WebP(사용 가이드 움짤)를 계속 되풀이해 보여 준다. 프레임을 미리 다 풀어 두지 않고
/// 비트맵 하나에 앞 프레임 위로 다음 프레임을 그때그때 풀어 넣는다(800×430 × 200장이면 수백 MB라서).
/// 화면(비주얼 트리)에 붙어 있고 보일 때만 돌고, 떨어지면 타이머를 멈추고 코덱·스트림을 닫는다.
/// 크기는 원본 폭까지만 넓어지고 비율을 지킨다.
/// </summary>
public sealed class AnimatedImage : Control
{
    private readonly Func<Stream?> _open;
    private readonly PixelSize _size;
    private readonly DispatcherTimer _timer = new();
    private SKCodec? _codec;
    private SKCodecFrameInfo[] _frames = [];
    private WriteableBitmap? _bitmap;
    /// <summary>비트맵에 지금 들어 있는 프레임(-1 = 아직 없음).</summary>
    private int _shown = -1;

    private AnimatedImage(Func<Stream?> open, PixelSize size)
    {
        _open = open;
        _size = size;
        MaxWidth = size.Width;
        _timer.Tick += (_, _) => Advance();
    }

    /// <summary><paramref name="open"/>이 여는 WebP를 보여 줄 컨트롤. 스트림이 없거나 풀 수 없으면 null.</summary>
    public static AnimatedImage? TryCreate(Func<Stream?> open)
    {
        using var codec = OpenCodec(open);
        if (codec is null) return null;
        var info = codec.Info;
        return info.Width > 0 && info.Height > 0 ? new AnimatedImage(open, new PixelSize(info.Width, info.Height)) : null;
    }

    private static SKCodec? OpenCodec(Func<Stream?> open)
    {
        var stream = open();
        if (stream is null) return null;
        // SKCodec이 스트림을 맡아 함께 닫는다(실패하면 여기서 닫음).
        var codec = SKCodec.Create(stream);
        if (codec is null) stream.Dispose();
        return codec;
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        double w = Math.Min(_size.Width, double.IsFinite(availableSize.Width) ? availableSize.Width : _size.Width);
        return new Avalonia.Size(w, w * _size.Height / _size.Width);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateRunning();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) UpdateRunning();
    }

    private void UpdateRunning()
    {
        if (VisualRoot is null || !IsVisible)
        {
            Stop();
            return;
        }
        if (_codec is not null) return;
        _codec = OpenCodec(_open);
        if (_codec is null) return;
        _frames = _codec.FrameInfo;
        _bitmap ??= new WriteableBitmap(_size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        _shown = -1;
        Advance();
    }

    /// <summary>다음 프레임을 풀어 넣고 그 프레임 길이 뒤에 다시 부른다(끝이면 처음으로).</summary>
    private void Advance()
    {
        if (_codec is null || _bitmap is null) return;
        int count = Math.Max(1, _frames.Length);
        int next = (_shown + 1) % count;
        if (!Decode(next)) return;
        InvalidateVisual();
        if (count < 2)
        {
            _timer.Stop();
            return;
        }
        int ms = _frames[next].Duration;
        _timer.Interval = TimeSpan.FromMilliseconds(ms > 10 ? ms : 100);
        _timer.Start();
    }

    private bool Decode(int index)
    {
        var codec = _codec!;
        var info = new SKImageInfo(_size.Width, _size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        // 비트맵에 든 프레임이 이 프레임의 바탕으로 쓸 수 있으면 그 위에만 풀고, 아니면 코덱이 필요한 앞 프레임부터 다시 푼다.
        int required = index < _frames.Length ? _frames[index].RequiredFrame : -1;
        int prior = required >= 0 && _shown >= required && _shown < index ? _shown : -1;
        using var fb = _bitmap!.Lock();
        var result = codec.GetPixels(info, fb.Address, fb.RowBytes, new SKCodecOptions(index, prior));
        if (result == SKCodecResult.InvalidParameters && prior >= 0)
            result = codec.GetPixels(info, fb.Address, fb.RowBytes, new SKCodecOptions(index, -1));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            Stop();
            return false;
        }
        _shown = index;
        return true;
    }

    private void Stop()
    {
        _timer.Stop();
        _codec?.Dispose();
        _codec = null;
        _frames = [];
        _bitmap?.Dispose();
        _bitmap = null;
        _shown = -1;
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null || _shown < 0) return;
        // 비율을 지켜 가운데에(Stretch.Uniform).
        double scale = Math.Min(Bounds.Width / _size.Width, Bounds.Height / _size.Height);
        double w = _size.Width * scale, h = _size.Height * scale;
        var dest = new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        context.DrawImage(_bitmap, new Rect(0, 0, _size.Width, _size.Height), dest);
    }
}
