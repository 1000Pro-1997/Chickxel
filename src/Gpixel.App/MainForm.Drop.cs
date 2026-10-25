using Avalonia.Platform.Storage;
using Gpixel.App.Localization;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

// 끌어 놓기(탐색기·Finder 또는 파일 브라우저에서).
// 캔버스 위: 문서가 열려 있으면 그림(PNG·JPG·BMP·GIF·WebP·APNG …)·영상마다 새 레이어(ImageDrop — 놓은 곳 가운데, 움직이는 그림·영상은 현재 프레임부터,
// 한 번에 실행취소 한 단계; 영상은 가져오기 설정을 먼저 묻는다). 넣을 그림이 없으면 처리하지 않고 창으로 넘긴다.
// 캔버스 밖(파일 목록·시작 화면·탭 줄·패널): 파일마다 새 탭으로 연다(MainForm.OpenFormats.cs의 창 끌어 놓기).
public sealed partial class MainForm
{
    /// <summary>끄는 중인 내용(끌기마다 한 번 읽음): 새 레이어로 넣을 파일(없으면 캔버스가 처리하지 않음), 그림 크기(읽을 수 없거나 영상이면 null).</summary>
    private sealed record CanvasDrag(IDataTransfer Data, IReadOnlyList<string> Files, IReadOnlyList<Size?> Sizes);

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

    /// <summary>새 레이어로 넣을 수 있는 파일(그림·영상 — 프로젝트 파일은 아님).</summary>
    private static bool IsLayerMedia(string path) => MediaFormats.KindOf(path) is MediaKind.Image or MediaKind.Video;

    /// <summary>끄는 곳이 문서를 보이는 캔버스 위인지(탭 줄·시작 화면·다른 묶음 칸은 아님).</summary>
    private bool IsOverCanvas(DragEventArgs e)
    {
        if (!_ctx.HasProject || _start.IsVisible || !_canvas.IsEffectivelyVisible) return false;
        var pt = e.GetPosition(_canvas);
        return new Rect(_canvas.Bounds.Size).Contains(pt);
    }

    private CanvasDrag DescribeDrag(IDataTransfer data)
    {
        if (_canvasDrag is { } known && ReferenceEquals(known.Data, data)) return known;
        var paths = data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        var drag = new CanvasDrag(data, [], []);
        // 캔버스 크기 모드 중에는 레이어를 넣지 않는다(끝낸 뒤 다시 끌어 놓기).
        if (!_ctx.IsCanvasResizing && paths.Where(IsLayerMedia).ToList() is { Count: > 0 } media)
            drag = drag with { Files = media, Sizes = media.Select(p => MediaFormats.NeedsFfmpeg(p) ? null : ImageUtil.ReadImageSize(p)).ToList() };
        return _canvasDrag = drag;
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        if (!IsOverCanvas(e) || DescribeDrag(e.DataTransfer) is not { Files.Count: > 0 } drag || _ctx.Project is not { } p)
        {
            // 캔버스가 처리하지 않으면 창(새 탭으로 열기)으로 넘긴다.
            _canvas.SetDropHint(null);
            return;
        }
        e.Handled = true;
        e.DragEffects = DragDropEffects.Copy;
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
        bool over = IsOverCanvas(e);
        var drag = DescribeDrag(e.DataTransfer);
        var at = _canvas.FramePixelAt(e.GetPosition(_canvas));
        EndCanvasDrag();
        if (!over || drag.Files.Count == 0) return;
        e.Handled = true;
        e.DragEffects = DragDropEffects.Copy;
        // 끌어 놓기가 끝난 뒤에 처리한다(설정·오류 창이 끌기 도중에 뜨지 않게).
        Dispatcher.UIThread.Post(() => AddImageLayers(drag.Files, at));
    }

    /// <summary>그림·영상 파일마다 새 레이어(현재 레이어 위, 놓은 순서대로 쌓임 — 마지막 파일이 맨 위·현재 레이어). 실행취소 한 단계.</summary>
    private void AddImageLayers(IReadOnlyList<string> paths, Point at)
    {
        if (_ctx.Project is not { } p || _ctx.IsCanvasResizing) return;
        var images = new List<DroppedImage>(paths.Count);
        var unreadable = new List<string>();
        foreach (var path in paths)
        {
            if (DecodeMedia(path, interactive: true, out _, out bool cancelled) is { } anim)
                images.Add(new DroppedImage(Path.GetFileNameWithoutExtension(path), anim.Images, anim.DurationsMs));
            else if (!cancelled) unreadable.Add(Path.GetFileName(path));
        }
        // 묻는 동안 문서가 바뀌었으면(탭을 닫음 등) 넣지 않는다.
        if (!ReferenceEquals(_ctx.Project, p) || _ctx.IsCanvasResizing) return;
        var notes = unreadable.Count > 0 ? MainStrings.FormatDrop_Unreadable(names: string.Join(", ", unreadable)) : "";
        if (images.Count == 0)
        {
            if (notes.Length > 0) ShowMessage(notes);
            return;
        }
        int frame = _ctx.CurrentFrame;
        ImageDropResult? result = null;
        _ctx.Structural(() => result = ImageDrop.Apply(p, images, _ctx.CurrentLayer, frame, at, MainStrings.Drop_UndoLabel));
        if (result is not { Layers.Count: > 0 }) return;
        _ctx.SetCurrentLayer(p.IndexOf(result.Layers[^1]));
        var message = MainStrings.FormatDrop_Added(names: string.Join(", ", result.Layers.Select(l => l.Name)));
        if (result.AddedFrames > 0) message += MainStrings.FormatDrop_FramesAdded(count: result.AddedFrames);
        var (entered, hint) = TransformDropped(result, images, frame);
        if (result.Oversized && !entered) message += MainStrings.Drop_Oversized;
        message += hint;
        if (notes.Length > 0) message += " · " + notes;
        ShowMessage(message);
    }
}
