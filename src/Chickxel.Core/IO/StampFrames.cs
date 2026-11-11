using Chickxel.Core.Assets;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 모양 펜 세트 ↔ 프레임: 캔버스의 프레임마다 합성한 그림을 모양 하나로(투명한 둘레를 잘라 냄, 빈 프레임은 건너뜀), 세트를 프레임마다 모양 하나씩 가운데 둔 문서로.
/// "캔버스에서 새 세트"와 "캔버스에서 고치기"(고친 문서를 세트에 저장)가 쓴다.
/// </summary>
public static class StampFrames
{
    /// <summary>캔버스에서 만드는 세트 하나의 모양 최대 개수(올릴 수 있는 세트와 같은 한도 — <see cref="AssetPackageLimits.MaxShapesPerSet"/>).</summary>
    public static int MaxShapes => AssetPackageLimits.Default.MaxShapesPerSet;

    /// <summary>빈 세트를 캔버스에서 고칠 때의 캔버스 한 변(px).</summary>
    public const int EmptyCanvasSize = 32;

    /// <summary>한 변이 <see cref="StampLibrary.MaxShapeSize"/>를 넘는 모양이 나온 프레임(0부터)과 잘라 낸 크기.</summary>
    public readonly record struct TooBigFrame(int Frame, int Width, int Height);

    /// <summary>
    /// 프레임 그림들을 모양으로 바꾼 결과: 모양(프레임 차례), 건너뛴 빈 프레임 수, 너무 큰 모양의 프레임.
    /// <see cref="IsValid"/>가 아니면(너무 큰 모양이 있거나 모양이 <see cref="MaxShapes"/>개를 넘음) 세트로 쓰지 않는다.
    /// </summary>
    public sealed record Result(IReadOnlyList<PixelImage> Shapes, int EmptyFrames, IReadOnlyList<TooBigFrame> TooBig)
    {
        /// <summary>그림이 있는 프레임(너무 큰 것 포함)이 <see cref="MaxShapes"/>개를 넘음.</summary>
        public bool TooMany => Shapes.Count + TooBig.Count > MaxShapes;

        /// <summary>세트로 쓸 수 있음(너무 큰 모양이 없고 개수가 한도 안).</summary>
        public bool IsValid => TooBig.Count == 0 && !TooMany;
    }

    /// <summary>프레임 그림마다 투명한 둘레를 잘라 모양으로 만든다(모두 투명이면 건너뜀). 너무 큰 모양은 모양 목록에 넣지 않고 <see cref="Result.TooBig"/>에 적는다.</summary>
    public static Result ToShapes(IEnumerable<PixelImage> frames)
    {
        var shapes = new List<PixelImage>();
        var tooBig = new List<TooBigFrame>();
        int empty = 0, index = 0;
        foreach (var frame in frames)
        {
            if (StampLibrary.Trim(frame) is not { } shape) empty++;
            else if (StampLibrary.TooBig(shape)) tooBig.Add(new TooBigFrame(index, shape.Width, shape.Height));
            else shapes.Add(shape);
            index++;
        }
        return new Result(shapes, empty, tooBig);
    }

    /// <summary>
    /// 세트의 모양들을 고칠 문서: 캔버스 = 가장 넓은 모양 폭 × 가장 높은 모양 높이(모양이 없으면 <see cref="EmptyCanvasSize"/>), 레이어 하나,
    /// 모양마다 프레임 하나(가운데 — 홀수 남는 칸은 오른쪽·아래로). 모양이 없으면 빈 프레임 하나. 실행취소 기록이 없고 수정됨이 아니다.
    /// </summary>
    public static SpriteProject ToProject(IReadOnlyList<PixelImage> shapes)
    {
        int width = shapes.Count == 0 ? EmptyCanvasSize : shapes.Max(s => s.Width);
        int height = shapes.Count == 0 ? EmptyCanvasSize : shapes.Max(s => s.Height);
        var project = new SpriteProject(width, height);
        for (int f = 1; f < shapes.Count; f++) project.AddFrame();
        for (int f = 0; f < shapes.Count; f++)
        {
            var shape = shapes[f];
            var cel = new PixelImage(width, height);
            cel.Blit(shape, (width - shape.Width) / 2, (height - shape.Height) / 2);
            project.SetCel(0, f, cel);
        }
        project.History.Clear();
        project.IsDirty = false;
        return project;
    }
}
