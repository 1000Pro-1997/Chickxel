using Avalonia.Platform.Storage;
using Gpixel.App.Localization;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

// 캔버스 자리에 파일 끌어 놓기(탐색기·Finder 또는 파일 브라우저에서).
// 문서가 열려 있으면 그림(PNG·JPG·BMP·GIF·WebP)마다 새 레이어(ImageDrop — 놓은 곳 가운데, 움직이는 GIF는 현재 프레임부터, 한 번에 실행취소 한 단계),
// 그림 없이 .gpixel이면 연다. 문서가 없으면(시작 화면) .gpixel·PNG를 연다(파일 > 열기와 같음).
public sealed partial class MainForm
{
    private enum CanvasDropKind { None, Open, Layers }

    /// <summary>새 레이어로 넣을 수 있는 그림 확장자(소문자).</summary>
    private static readonly string[] LayerImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    /// <summary>끄는 중인 내용(끌기마다 한 번 읽음): 무엇을 할지, 대상 파일, 그림 크기(레이어로 넣을 때 — 읽을 수 없으면 null).</summary>
    private sealed record CanvasDrag(IDataTransfer Data, CanvasDropKind Kind, IReadOnlyList<string> Files, IReadOnlyList<Size?> Sizes);

    private CanvasDrag? _canvasDrag;

    private void AttachCanvasDrop(Control host)
    {
        DragDrop.SetAllowDrop(host, true);
        host.AddHandler(DragDrop.DragEnterEvent, OnCanvasDragOver);
        host.AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
        host.AddHandler(DragDrop.DragLeaveEvent, (_, _) => EndCanvasDrag());
        host.AddHandler(DragDrop.DropEvent, OnCanvasDrop);
    }

    private static bool HasExtension(string path, string ext) => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);

    private static bool IsLayerImage(string path) => LayerImageExtensions.Any(ext => HasExtension(path, ext));

    private CanvasDrag DescribeDrag(IDataTransfer data)
    {
        if (_canvasDrag is { } known && ReferenceEquals(known.Data, data)) return known;
        var paths = data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        var drag = new CanvasDrag(data, CanvasDropKind.None, [], []);
        if (!_ctx.HasProject)
        {
            if (paths.FirstOrDefault(p => HasExtension(p, SpriteProject.FileExtension) || HasExtension(p, ".png")) is { } open)
                drag = drag with { Kind = CanvasDropKind.Open, Files = [open] };
        }
        else if (paths.Where(IsLayerImage).ToList() is { Count: > 0 } images)
        {
            // 캔버스 크기 모드 중에는 레이어를 넣지 않는다(끝낸 뒤 다시 끌어 놓기).
            if (!_ctx.IsCanvasResizing)
                drag = drag with { Kind = CanvasDropKind.Layers, Files = images, Sizes = images.Select(ImageUtil.ReadImageSize).ToList() };
        }
        else if (paths.FirstOrDefault(p => HasExtension(p, SpriteProject.FileExtension)) is { } project)
            drag = drag with { Kind = CanvasDropKind.Open, Files = [project] };
        return _canvasDrag = drag;
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        var drag = DescribeDrag(e.DataTransfer);
        e.Handled = true;
        e.DragEffects = drag.Kind == CanvasDropKind.None ? DragDropEffects.None : DragDropEffects.Copy;
        if (drag.Kind != CanvasDropKind.Layers || _ctx.Project is not { } p)
        {
            _canvas.SetDropHint(null);
            return;
        }
        var at = _canvas.FramePixelAt(e.GetPosition(_canvas));
        var ghosts = new List<Rectangle>(drag.Sizes.Count);
        foreach (var size in drag.Sizes)
            if (size is { Width: > 0, Height: > 0 } s) ghosts.Add(new Rectangle(ImageDrop.Place(p.Size, s, at).Location, s));
        _canvas.SetDropHint(ghosts);
    }

    private void EndCanvasDrag()
    {
        _canvasDrag = null;
        _canvas.SetDropHint(null);
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        var drag = DescribeDrag(e.DataTransfer);
        var at = _canvas.FramePixelAt(e.GetPosition(_canvas));
        EndCanvasDrag();
        e.Handled = true;
        if (drag.Kind == CanvasDropKind.None)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        // 끌어 놓기가 끝난 뒤에 처리한다(파일 열기 대화상자·오류 창이 끌기 도중에 뜨지 않게).
        Dispatcher.UIThread.Post(() =>
        {
            if (drag.Kind == CanvasDropKind.Open) OpenFile(drag.Files[0]);
            else AddImageLayers(drag.Files, at);
        });
    }

    /// <summary>그림 파일마다 새 레이어(현재 레이어 위, 놓은 순서대로 쌓임 — 마지막 파일이 맨 위·현재 레이어). 실행취소 한 단계.</summary>
    private void AddImageLayers(IReadOnlyList<string> paths, Point at)
    {
        if (_ctx.Project is not { } p || _ctx.IsCanvasResizing) return;
        var images = new List<DroppedImage>(paths.Count);
        var unreadable = new List<string>();
        foreach (var path in paths)
        {
            if (DecodeDropped(path) is { } image) images.Add(image);
            else unreadable.Add(Path.GetFileName(path));
        }
        var notes = unreadable.Count > 0 ? MainStrings.FormatDrop_Unreadable(names: string.Join(", ", unreadable)) : "";
        if (images.Count == 0)
        {
            ShowMessage(notes);
            return;
        }
        ImageDropResult? result = null;
        _ctx.Structural(() => result = ImageDrop.Apply(p, images, _ctx.CurrentLayer, _ctx.CurrentFrame, at, MainStrings.Drop_UndoLabel));
        if (result is not { Layers.Count: > 0 }) return;
        _ctx.SetCurrentLayer(p.IndexOf(result.Layers[^1]));
        var message = MainStrings.FormatDrop_Added(names: string.Join(", ", result.Layers.Select(l => l.Name)));
        if (result.AddedFrames > 0) message += MainStrings.FormatDrop_FramesAdded(count: result.AddedFrames);
        if (result.Oversized) message += MainStrings.Drop_Oversized;
        if (notes.Length > 0) message += " · " + notes;
        ShowMessage(message);
    }

    /// <summary>끌어 놓은 그림을 푼다(PNG는 열기와 같은 PNG 읽기, 나머지는 SkiaSharp — 움직이는 GIF·WebP는 프레임마다). 못 풀면 null.</summary>
    private static DroppedImage? DecodeDropped(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (HasExtension(path, ".png")) return new DroppedImage(name, [PngIO.Load(path)], [Frame.DefaultDurationMs]);
            return ImageUtil.DecodeFrames(path) is { } f ? new DroppedImage(name, f.Images, f.DurationsMs) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}
