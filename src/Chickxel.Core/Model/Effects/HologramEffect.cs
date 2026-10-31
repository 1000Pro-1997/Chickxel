namespace Chickxel.Core.Model;

/// <summary>홀로그램 주사선이 줄을 어떻게 덜어 내는지.</summary>
public enum HologramScanStyle
{
    /// <summary>어둡게: 색 × (1 − 세기).</summary>
    Darken,
    /// <summary>투명하게: 알파 × (1 − 세기).</summary>
    Fade,
}

/// <summary>
/// 홀로그램: 보이는 픽셀을 한 색의 투사 영상처럼(그림 모양·명암은 그대로) 바꾼다. 밖으로는 테두리 빛 1px과 글리치 밀림만 번진다.
/// <list type="bullet">
/// <item>색: 밝기 L = 0.299R + 0.587G + 0.114B에서 목표 색 = 색 × (0.3 + 0.7 × L/192)(L ≤ 192), 그 위는 흰색 쪽으로 최대 60% — 채널마다 L에 단조라 명암 순서가 그대로다.
/// 그 쪽으로 <see cref="TintStrength"/>%.</item>
/// <item>주사선: 원본 위 기준 줄 y가 (y − 흐름) mod 간격 = 0이면 어둡게·투명하게 <see cref="ScanStrength"/>%. 흐르기면 흐름 = ⌊진행 × 간격⌋(한 주기에 한 간격 = 이음매 없이 되풀이).</item>
/// <item>밝은 띠: 두께 줄의 띠가 진행 0(보이는 그림 바로 위) → 1(바로 아래)로 내려가며 밝게(스크린, 색과 흰색 사이 색 쪽 <see cref="BandStrength"/>%). 띠 줄엔 주사선이 없다.</item>
/// <item>투명도·깜빡임: 알파 × 투명도 × 깜빡임(깜빡이는 프레임이면 1 − 세기 × (0.5~1)). 디더면 알파 대신 4×4 Bayer 칸만 남긴다.</item>
/// <item>글리치: 글리치 프레임이면 보이는 그림 높이 안에서 1~3개 가로 조각(1~3px)이 좌우로 1~<see cref="GlitchShift"/>px 밀리고, RGB 분리면 빨강은 왼쪽·파랑은 오른쪽 1px 이웃 값을 쓴다.</item>
/// <item>테두리 빛: 그림 밖 4방향 이웃 1px에 색 × 테두리 세기(× 투명도·깜빡임).</item>
/// </list>
/// 깜빡임·글리치는 (시드, 범위 안 프레임 번호)로 정해지고 빠르기·반복과 관계없다. 같은 프레임·설정·시드면 늘 같은 그림.
/// </summary>
public sealed record HologramEffect : AnimatedEffect
{
    public const int MinScanGap = 2, MaxScanGap = 6, MaxBandSize = 16, MaxGlitchShift = 8, MaxSeed = 9999;

    /// <summary>기본 홀로그램 색(하늘색).</summary>
    public static readonly Color32 DefaultColor = new(80, 220, 255);

    private readonly int _tint = 85, _gap = 2, _scan = 40, _band = 2, _bandStrength = 60, _opacity = 75;
    private readonly int _flickerRate = 15, _flicker = 40, _glitchRate = 20, _shift = 2, _edge = 50, _seed = 2;

    /// <summary>홀로그램 단색(단색 알파는 쓰지 않음; 색 그라데이션의 알파는 색 세기·띠·테두리 빛에 곱한다).</summary>
    public Color32 Color { get; init; } = DefaultColor;

    /// <summary>색 세기 0~100%(0 = 원래 색).</summary>
    public int TintStrength { get => _tint; init => _tint = Math.Clamp(value, 0, 100); }

    /// <summary>주사선 간격 <see cref="MinScanGap"/>~<see cref="MaxScanGap"/>줄(이 줄마다 하나).</summary>
    public int ScanGap { get => _gap; init => _gap = Math.Clamp(value, MinScanGap, MaxScanGap); }

    /// <summary>주사선 세기 0~100%(0 = 주사선 없음).</summary>
    public int ScanStrength { get => _scan; init => _scan = Math.Clamp(value, 0, 100); }

    public HologramScanStyle ScanStyle { get; init; } = HologramScanStyle.Darken;

    /// <summary>흐르기: 주사선이 진행에 따라 아래로 흐른다(한 주기에 한 간격).</summary>
    public bool ScanScroll { get; init; } = true;

    /// <summary>밝은 띠가 위에서 아래로 지나간다.</summary>
    public bool Band { get; init; } = true;

    /// <summary>띠 두께 1~<see cref="MaxBandSize"/>px.</summary>
    public int BandSize { get => _band; init => _band = Math.Clamp(value, 1, MaxBandSize); }

    /// <summary>띠 세기 0~100%.</summary>
    public int BandStrength { get => _bandStrength; init => _bandStrength = Math.Clamp(value, 0, 100); }

    /// <summary>투명도(불투명한 정도) 0~100%.</summary>
    public int Opacity { get => _opacity; init => _opacity = Math.Clamp(value, 0, 100); }

    /// <summary>디더: 반투명 대신 4×4 Bayer 무늬 칸만 남긴다(원본 왼쪽 위 기준).</summary>
    public bool Dither { get; init; }

    /// <summary>깜빡임 빈도 0~100%(프레임마다 깜빡일 확률, 0 = 끔).</summary>
    public int FlickerRate { get => _flickerRate; init => _flickerRate = Math.Clamp(value, 0, 100); }

    /// <summary>깜빡임 세기 0~100%(가장 많이 덜어 내는 불투명도).</summary>
    public int FlickerStrength { get => _flicker; init => _flicker = Math.Clamp(value, 0, 100); }

    /// <summary>글리치 빈도 0~100%(프레임마다 글리치일 확률, 0 = 끔 — 여백도 없음).</summary>
    public int GlitchRate { get => _glitchRate; init => _glitchRate = Math.Clamp(value, 0, 100); }

    /// <summary>글리치 최대 밀림 1~<see cref="MaxGlitchShift"/>px(좌우 여백).</summary>
    public int GlitchShift { get => _shift; init => _shift = Math.Clamp(value, 1, MaxGlitchShift); }

    /// <summary>글리치 프레임에 RGB 분리(빨강·파랑 1px 어긋남).</summary>
    public bool RgbSplit { get; init; } = true;

    /// <summary>테두리 빛: 그림 밖 1px 둘레(사방 1px 여백).</summary>
    public bool EdgeGlow { get; init; } = true;

    /// <summary>테두리 빛 세기 0~100%.</summary>
    public int EdgeOpacity { get => _edge; init => _edge = Math.Clamp(value, 0, 100); }

    public int Seed { get => _seed; init => _seed = Math.Clamp(value, 0, MaxSeed); }

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_Hologram;

    /// <summary>좌우로 번지는 px(글리치 최대 밀림, 글리치가 꺼져 있으면 0).</summary>
    public int GlitchReach => GlitchRate > 0 ? GlitchShift : 0;

    /// <summary>진행 <paramref name="u"/>의 주사선 흐름(0~간격−1줄, 아래로).</summary>
    public int ScanOffset(double u) => ScanScroll ? (int)Math.Floor(Math.Clamp(u, 0, 1) * ScanGap) % ScanGap : 0;

    /// <summary>범위 안 <paramref name="step"/>번째 프레임에 남는 불투명도 배율(깜빡이지 않으면 1).</summary>
    public double FlickerAt(int step)
    {
        if (FlickerRate == 0 || FlickerStrength == 0 || Edit.LayerEffects.Random(Seed, step, 0, 41) >= FlickerRate / 100.0) return 1;
        return 1 - FlickerStrength / 100.0 * (0.5 + 0.5 * Edit.LayerEffects.Random(Seed, step, 1, 41));
    }

    /// <summary>범위 안 <paramref name="step"/>번째 프레임이 글리치 프레임인지.</summary>
    public bool GlitchAt(int step) => GlitchRate > 0 && Edit.LayerEffects.Random(Seed, step, 0, 43) < GlitchRate / 100.0;

    /// <summary>
    /// 글리치 프레임의 밀리는 가로 조각들: 위 = 높이 <paramref name="height"/>인 보이는 그림 위 끝에서 몇 줄째(0~높이−1), 조각 높이 1~3줄, 밀림 ±1~<see cref="GlitchShift"/>px.
    /// 글리치 프레임이 아니면 빈 목록.
    /// </summary>
    public IReadOnlyList<(int Top, int Height, int Shift)> GlitchSlices(int step, int height)
    {
        if (height <= 0 || !GlitchAt(step)) return [];
        double R(int k) => Edit.LayerEffects.Random(Seed, step, k, 44);
        int n = 1 + (int)(R(0) * 3);
        var slices = new (int, int, int)[n];
        for (int j = 0; j < n; j++)
        {
            int top = (int)(R(1 + j * 4) * height), h = 1 + (int)(R(2 + j * 4) * 3), d = 1 + (int)(R(3 + j * 4) * GlitchShift);
            slices[j] = (top, h, R(4 + j * 4) < 0.5 ? -d : d);
        }
        return slices;
    }
}
