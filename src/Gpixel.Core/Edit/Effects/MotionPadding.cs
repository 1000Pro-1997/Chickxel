using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>움직이는 효과(흔들기·둥실둥실·녹아내리기·회전·충격파 고리)가 그림 밖으로 넓히는 여백.</summary>
internal static class MotionPadding
{
    /// <summary>
    /// 지금까지 넓힌 크기 <paramref name="width"/>×<paramref name="height"/>에 더할 여백(왼쪽·위·오른쪽·아래).
    /// 흔들기·둥실둥실 = 그 축으로 세기·높이, 녹아내리기 = 아래로 거리, 충격파 = 사방으로 최대 반지름(피벗·직접 중심은 +1 — 중심이 그림 끝에 걸쳐도),
    /// 회전 = 기준점을 중심으로 돌린 그림이 닿을 수 있는 만큼(가운데: 가로 ⌈높이/2⌉·세로 ⌈너비/2⌉, 아래 가운데: 가로 높이·위 ⌈너비/2⌉·아래 √((너비/2)² + 높이²),
    /// 피벗: 사방 대각선 길이 — 각 +1 반올림 몫). 회전 각이 0이면 없음.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) Of(LayerEffect effect, int width, int height)
    {
        switch (effect)
        {
            case ShakeEffect s:
            {
                int x = s.Axis == EffectAxis.Vertical ? 0 : s.Amount, y = s.Axis == EffectAxis.Horizontal ? 0 : s.Amount;
                return (x, y, x, y);
            }
            case BobEffect o:
                return o.Axis == EffectAxis.Horizontal ? (o.Height, 0, o.Height, 0) : (0, o.Height, 0, o.Height);
            case MeltEffect m:
                return (0, 0, 0, m.Distance);
            case ShockwaveEffect w:
            {
                int r = w.Radius + (w.Center is ExplodeCenter.Pivot or ExplodeCenter.Custom ? 1 : 0);
                return (r, r, r, r);
            }
            case RotateEffect q when q.Angle > 0:
            {
                double w2 = width / 2.0;
                switch (q.Anchor)
                {
                    case SquashAnchor.Middle:
                    {
                        int x = (int)Math.Ceiling(height / 2.0) + 1, y = (int)Math.Ceiling(w2) + 1;
                        return (x, y, x, y);
                    }
                    case SquashAnchor.BottomCenter:
                    {
                        int x = height + 1;
                        return (x, (int)Math.Ceiling(w2) + 1, x, (int)Math.Ceiling(Math.Sqrt(w2 * w2 + (double)height * height)) + 1);
                    }
                    default:
                    {
                        int d = (int)Math.Ceiling(Math.Sqrt((double)width * width + (double)height * height)) + 1;
                        return (d, d, d, d);
                    }
                }
            }
            default:
                return (0, 0, 0, 0);
        }
    }
}
