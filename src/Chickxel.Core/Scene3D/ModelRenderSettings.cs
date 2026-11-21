using System.Numerics;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

public enum ModelProjection
{
    /// <summary>정사영(멀어도 크기가 같음 — 게임 스프라이트에 흔함).</summary>
    Orthographic,
    Perspective,
}

/// <summary>
/// 3D 모델을 그림 한 장으로 그리는 설정(<see cref="ModelRenderer.Render"/>). 각도는 도 단위.
/// 카메라는 맞춤 범위(<see cref="ModelBounds"/>)의 가운데를 보고, <see cref="Yaw"/>만큼 +Y축 둘레로(양수 = 모델 오른쪽 면이 보이게), <see cref="Pitch"/>만큼
/// 위에서 내려다보게 돈다(0, 0 = +Z 쪽 정면). <see cref="Zoom"/> 1이면 맞춤 범위의 둘레 구가 그림의 짧은 변에 꼭 맞는다.
/// </summary>
public sealed record ModelRenderSettings
{
    public int Width { get; init; } = 128;
    public int Height { get; init; } = 128;
    public float Yaw { get; init; } = 30;
    public float Pitch { get; init; } = 20;
    /// <summary>카메라의 시선 축 주위 회전(도, 양수 = 그림이 시계 방향으로 회전).</summary>
    public float Roll { get; init; }
    public ModelProjection Projection { get; init; } = ModelProjection.Orthographic;
    /// <summary>원근일 때 세로 시야각.</summary>
    public float FieldOfView { get; init; } = 35;
    /// <summary>확대(1 = 맞춤, 2 = 두 배).</summary>
    public float Zoom { get; init; } = 1;
    /// <summary>화면에서 옮기기(그림 짧은 변에 대한 비율, +X 오른쪽, +Y 아래).</summary>
    public float PanX { get; init; }
    public float PanY { get; init; }
    /// <summary>빛이 오는 방향: 카메라 기준 가로(양수 = 오른쪽에서)·세로(양수 = 위에서) 각도.</summary>
    public float LightYaw { get; init; } = -40;
    public float LightPitch { get; init; } = 45;
    /// <summary>빛을 카메라 뒤쪽 반구로 옮긴다(화면상의 위치는 그대로, 깊이 방향만 뒤집음).</summary>
    public bool LightBack { get; init; }
    /// <summary>주변광(0~1): 빛을 받지 않는 면의 밝기.</summary>
    public float Ambient { get; init; } = 0.4f;
    /// <summary>빛 세기(0~2).</summary>
    public float LightStrength { get; init; } = 0.8f;
    /// <summary>명암 단계(0 = 부드럽게, 2~8 = 그 수의 밝기 단계로 끊어 셀 셰이딩처럼).</summary>
    public int ShadeSteps { get; init; }
    /// <summary>픽셀마다 표본 수의 한 변(1 = 가장자리가 계단처럼 또렷, 2~8 = 그 제곱만큼 나눠 그려 평균 — 부드러운 가장자리).</summary>
    public int Supersample { get; init; } = 4;
    /// <summary>텍스처를 쓸지(끄면 재질 바탕 색만).</summary>
    public bool Textures { get; init; } = true;
    /// <summary>바탕(기본 투명).</summary>
    public Color32 Background { get; init; }

    // ── 픽셀 그림 다듬기(그린 뒤 결과 픽셀에서 — 외곽선은 결과 1px 단위) ──

    /// <summary>반투명 없애기: 덮인 비율이 절반 이상인 픽셀만 불투명(255), 나머지는 투명. 가장자리 다듬기로 색은 부드럽게, 알파는 또렷하게.</summary>
    public bool HardAlpha { get; init; }
    /// <summary>색 수(0 = 그대로, 2~64 = 그 수로 줄임 — 프레임이 여럿이면 모든 프레임이 한 팔레트). 외곽선 색은 따로(줄인 뒤 그림).</summary>
    public int Colors { get; init; }
    /// <summary>겉 외곽선: 모델과 빈 곳 사이.</summary>
    public bool OutlineOuter { get; init; }
    /// <summary>
    /// 겉 외곽선을 모델 바깥 빈 픽셀에 그릴지(모델이 선 두께만큼 커 보임 — 그만큼 둘레 구를 짧은 변 − 2 × 두께에 맞춰 확대 1에서도 잘리지 않음).
    /// 끄면 모델 가장자리 픽셀을 선으로 바꾼다. 이웃은 상하좌우(대각선 모서리 칸은 칠하지 않음).
    /// </summary>
    public bool OutlineOutside { get; init; } = true;
    /// <summary>안쪽 겹침선: 앞에 있는 부분과 그 뒤 부분 사이(깊이가 크게 다른 이웃 — 몸 앞의 팔 등). 뒤쪽 픽셀에 1px로 그려 앞 부분 윤곽은 그대로.</summary>
    public bool OutlineInner { get; init; }
    /// <summary>각진 곳 선: 면이 이 각도(도)보다 크게 꺾인 곳(0 = 끔, 상자 모서리는 90°). 카메라를 덜 보는 면 쪽 픽셀에 1px.</summary>
    public float OutlineCrease { get; init; }
    /// <summary>겉 외곽선 두께(결과 px, 1~3). 안쪽 겹침선·각진 곳 선은 늘 1px.</summary>
    public int OutlineWidth { get; init; } = 1;
    public ModelOutlineColor OutlineColorMode { get; init; } = ModelOutlineColor.Fixed;
    /// <summary>고정 색 외곽선의 색.</summary>
    public Color32 OutlineColor { get; init; } = new(24, 20, 28);
    /// <summary>어둡게 외곽선: 선 자리(바깥 선이면 붙은 모델 픽셀) 색의 밝기(OKLab L)를 이 비율만큼 줄인다(0~1, 0.5 = 절반).</summary>
    public float OutlineDarken { get; init; } = 0.5f;

    /// <summary>외곽선을 하나라도 그리는지.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasOutline => OutlineOuter || OutlineInner || OutlineCrease > 0;
}

/// <summary>외곽선 색을 정하는 방식.</summary>
public enum ModelOutlineColor
{
    /// <summary>한 가지 색(<see cref="ModelRenderSettings.OutlineColor"/>).</summary>
    Fixed,
    /// <summary>선 자리 모델 색을 어둡게(셀아웃 — 밝은 면은 옅은 선, 어두운 면은 짙은 선).</summary>
    Darken,
}

/// <summary>카메라를 맞출 범위(월드 좌표 축 정렬 상자). 여러 자세(돌려 보기·애니메이션 프레임)를 합쳐 쓰면 프레임마다 크기·자리가 흔들리지 않는다.</summary>
public readonly record struct ModelBounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
    /// <summary>둘레 구 반지름(상자 대각선의 절반, 적어도 아주 작은 값).</summary>
    public float Radius => MathF.Max(1e-6f, Vector3.Distance(Min, Max) * 0.5f);
    public bool IsEmpty => Min.X > Max.X;
    public static ModelBounds Empty => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));
    public ModelBounds Union(ModelBounds o) => IsEmpty ? o : o.IsEmpty ? this : new(Vector3.Min(Min, o.Min), Vector3.Max(Max, o.Max));
}
