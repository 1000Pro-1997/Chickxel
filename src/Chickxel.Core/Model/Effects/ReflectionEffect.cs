namespace Chickxel.Core.Model;

/// <summary>
/// 반사: 그 프레임 그림의 보이는 영역을 뒤집은 복사본을 <see cref="Direction"/> 쪽(아래·위는 위아래로, 왼쪽·오른쪽은 좌우로 뒤집음)에 <see cref="Gap"/>px 띄워 그림 뒤에 깐다.
/// 그림 높이(왼쪽·오른쪽이면 너비)의 <see cref="Height"/>%만큼(가까운 줄부터) 비치고, 불투명도는 가까운 줄 <see cref="OpacityStart"/>% → 먼 줄 <see cref="OpacityEnd"/>%.
/// <see cref="DitherFade"/>면 알파를 낮추는 대신 4×4 Bayer 무늬로 픽셀을 덜어 낸다(남은 픽셀은 원래 알파, 효과 원본 왼쪽 위 기준 무늬).
/// <see cref="Tint"/>는 비친 색을 그 색 쪽으로 알파/255만큼 물들이고(0 = 안 물들임), <see cref="Wave"/>는 줄마다 ±px 흔든다(물결, 고정 무늬 —
/// 아래·위면 줄마다 가로로, 왼쪽·오른쪽이면 열마다 세로로).
/// </summary>
public sealed record ReflectionEffect : LayerEffect
{
    public const int MaxGap = 32, MaxWave = 3;

    /// <summary>물결 한 번(오르내림)의 줄 수.</summary>
    public const int WavePeriod = 6;

    private readonly int _gap, _height = 50, _start = 50, _end, _wave;

    /// <summary>그림과 반사 사이 px(0~<see cref="MaxGap"/>).</summary>
    public int Gap { get => _gap; init => _gap = Math.Clamp(value, 0, MaxGap); }

    /// <summary>비칠 길이 1~100(아래·위면 그림 높이, 왼쪽·오른쪽이면 그림 너비의 %).</summary>
    public int Height { get => _height; init => _height = Math.Clamp(value, 1, 100); }

    /// <summary>그림 쪽 줄의 불투명도 0~100(%).</summary>
    public int OpacityStart { get => _start; init => _start = Math.Clamp(value, 0, 100); }

    /// <summary>끝 줄의 불투명도 0~100(%).</summary>
    public int OpacityEnd { get => _end; init => _end = Math.Clamp(value, 0, 100); }

    public bool DitherFade { get; init; }

    /// <summary>물들일 색(알파 = 세기, 0 = 안 물들임).</summary>
    public Color32 Tint { get; init; } = new(70, 130, 230, 0);

    /// <summary>물결 폭 0~<see cref="MaxWave"/>px(0 = 끔).</summary>
    public int Wave { get => _wave; init => _wave = Math.Clamp(value, 0, MaxWave); }

    /// <summary>비치는 쪽(예전 파일의 위/아래는 <see cref="ReflectionDirection.Up"/>/<see cref="ReflectionDirection.Down"/>).</summary>
    public ReflectionDirection Direction { get; init; }

    /// <summary>왼쪽·오른쪽(벽) 반사 — 좌우로 뒤집고 길이·물결 축이 바뀐다.</summary>
    public bool Horizontal => Direction is ReflectionDirection.Left or ReflectionDirection.Right;

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_Reflection;
}

/// <summary><see cref="ReflectionEffect"/>가 비치는 쪽.</summary>
public enum ReflectionDirection
{
    /// <summary>아래(바닥·물).</summary>
    Down,
    /// <summary>위(천장).</summary>
    Up,
    /// <summary>왼쪽(벽).</summary>
    Left,
    /// <summary>오른쪽(벽).</summary>
    Right,
}
