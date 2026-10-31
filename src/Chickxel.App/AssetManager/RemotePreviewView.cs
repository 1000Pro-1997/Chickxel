using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 둘러보기 상세의 원격 미리보기 PNG: 화면에 붙을 때만 <see cref="RemoteAssetClient.GetPreviewAsync"/>(디스크 cache 먼저)로 받아 decode하고,
/// 떨어지면 요청을 취소하고 비트맵을 버린다. 미리보기가 없거나 받지 못하면 빈 틀만 그린다.
/// </summary>
public sealed class RemotePreviewView : Control
{
    private readonly RemoteCatalogEntry _entry;
    private CancellationTokenSource? _load;
    private WriteableBitmap? _bitmap;

    public RemotePreviewView(RemoteCatalogEntry entry)
    {
        _entry = entry;
        Height = 160;
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var load = _load = new CancellationTokenSource();
        try
        {
            if (await AppAssets.Remote.GetPreviewAsync(_entry, load.Token) is not { } png || load.IsCancellationRequested) return;
            using var stream = new MemoryStream(png, writable: false);
            _bitmap = ImageUtil.ToBitmap(PngIO.Load(stream));
            InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException) { }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // 받는 중인 요청이 토큰을 아직 쓰므로 취소만 한다(타이머 없는 CTS라 Dispose가 필요 없음).
        _load?.Cancel();
        _load = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public override void Render(DrawingContext g)
    {
        var b = new Rect(Bounds.Size);
        g.FillRectangle(AppTheme.PanelBrush, b);
        g.DrawRectangle(new Pen(AppTheme.BorderBrush, 1), b.Deflate(0.5));
        if (_bitmap is not { } bmp) return;
        var area = b.Deflate(8);
        var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
        double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
        ImageUtil.DrawFitted(g, bmp, src, area, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
    }
}
