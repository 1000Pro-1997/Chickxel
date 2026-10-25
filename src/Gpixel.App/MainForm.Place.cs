using Gpixel.App.Localization;
using Gpixel.Core.Edit;

namespace Gpixel.App;

// 지금 문서에 그림을 넣은 뒤(캔버스에 끌어 놓기·AI 그림 픽셀로 정리의 적용) 바로 변형 모드(Ctrl+T)로: 넣은 그림 전체를 잘리기 전 그대로 띄워 들고
// 손잡이로 옮기기·크기·회전, Enter = 그 자리에 적용(캔버스 밖은 그때 잘림), Esc = 넣은 그대로. 넣기와 변형은 실행취소 두 단계.
public sealed partial class MainForm
{
    /// <summary>
    /// <see cref="ImageDrop.Apply"/>로 넣은 마지막 그림을 변형 모드로 든다(<paramref name="images"/> = 넣은 그림들, 같은 순서). 넣은 레이어를 고르고
    /// 넣기 시작한 프레임(<paramref name="frame"/>)으로 간 뒤 잘리기 전 원본을 띄운다. 프레임이 여럿인 그림(움직이는 GIF·영상)은 셀 하나만 옮길 수 없으므로
    /// 변형 없이 그대로 둔다. 변형 모드에 들어갔는지와 상태 표시줄 문구 뒤에 붙일 안내를 돌려준다.
    /// </summary>
    private (bool Entered, string Hint) TransformDropped(ImageDropResult result, IReadOnlyList<DroppedImage> images, int frame)
    {
        if (result.Layers.Count == 0 || images.Count != result.Layers.Count || _ctx.Project is not { } p) return (false, "");
        var image = images[^1];
        if (image.Frames.Count > 1) return (false, MainStrings.Place_MultiFrameHint);
        int layer = p.IndexOf(result.Layers[^1]);
        if (layer < 0) return (false, "");
        _ctx.SetCurrent(layer, frame);
        if (_ctx.BeginPlacedTransform(image.Frames[0], result.Locations[^1]) is { } error) return (false, " · " + error);
        _canvas.Focus();
        return (true, MainStrings.Place_TransformHint);
    }
}
