using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>모양 펜 상태: 세트 모음, 고른 세트, 다음에 찍을 모양, 지금 색으로 물들이기, 끌기 간격(도구 설정 — 실행취소 없음).</summary>
public sealed partial class EditorContext
{
    /// <summary>끌기 간격 최댓값(px, 최솟값은 1 — 1이면 모든 픽셀에 찍어 이어진 자국).</summary>
    public const int MaxShapePenSpacing = 256;

    private StampLibrary _shapeSets = StampLibrary.Defaults();
    private string? _shapePenSetId;
    private int _shapePenIndex;
    private bool _shapePenTint;
    private int _shapePenSpacing = 1;
    private bool _shapePenOptimizedSpacing = true;
    private readonly StampPicker _shapePenPicker = new();

    /// <summary>세트 모음이 바뀜(세트·모양 더하기·지우기, 이름·순서, 기본 세트 되돌리기) — 메인 창이 파일에 저장한다.</summary>
    public event EventHandler? ShapeSetsChanged;
    /// <summary>고른 세트나 다음 모양이 바뀜(도장을 찍을 때마다 포함).</summary>
    public event EventHandler? ShapePenChanged;

    /// <summary>모양 펜 세트 모음(메인 창이 파일에서 읽어 넣음). 바꾸면 고른 세트를 다시 맞춘다.</summary>
    public StampLibrary ShapeSets
    {
        get => _shapeSets;
        set
        {
            _shapeSets = value;
            ShapePenSetId = _shapePenSetId;
        }
    }

    /// <summary>고른 세트(없거나 지워졌으면 첫 세트, 세트가 하나도 없으면 null).</summary>
    public StampSet? ShapePenSet => _shapeSets.Find(_shapePenSetId) ?? (_shapeSets.Sets.Count > 0 ? _shapeSets.Sets[0] : null);

    /// <summary>고른 세트 id. 고르면 첫 모양(차례대로 = 처음, 무작위 = 아무거나)부터.</summary>
    public string? ShapePenSetId
    {
        get => ShapePenSet?.Id;
        set
        {
            _shapePenSetId = value;
            var set = ShapePenSet;
            _shapePenIndex = set is null ? 0 : _shapePenPicker.First(set.Shapes.Count, set.Order);
            ShapePenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>다음에 찍을 모양 번호(고른 세트 안, 도구 설정에서 눌러 고를 수 있음).</summary>
    public int ShapePenIndex
    {
        get => ShapePenSet is { Shapes.Count: > 0 } set ? Math.Clamp(_shapePenIndex, 0, set.Shapes.Count - 1) : 0;
        set
        {
            if (_shapePenIndex == value) return;
            _shapePenIndex = value;
            ShapePenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>다음에 찍을 모양(세트가 비었으면 null).</summary>
    public PixelImage? ShapePenShape => ShapePenSet is { Shapes.Count: > 0 } set ? set.Shapes[ShapePenIndex] : null;

    /// <summary>도장을 찍은 뒤: 세트 순서(무작위·차례대로)대로 다음 모양으로 넘긴다.</summary>
    public void AdvanceShapePen()
    {
        if (ShapePenSet is not { Shapes.Count: > 1 } set) return;
        _shapePenIndex = _shapePenPicker.Next(ShapePenIndex, set.Shapes.Count, set.Order);
        ShapePenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>모양 펜: 모양의 명암·불투명도는 두고 색만 지금 색(왼쪽 = 기본색, 오른쪽 = 보조색)으로 물들인다.</summary>
    public bool ShapePenTint { get => _shapePenTint; set { if (_shapePenTint == value) return; _shapePenTint = value; OptionsChanged(); } }

    /// <summary>모양 펜 끌기 간격(px, 1~<see cref="MaxShapePenSpacing"/>). 최적화 간격을 켜면 쓰지 않는다.</summary>
    public int ShapePenSpacing
    {
        get => _shapePenSpacing;
        set { value = Math.Clamp(value, 1, MaxShapePenSpacing); if (_shapePenSpacing == value) return; _shapePenSpacing = value; OptionsChanged(); }
    }

    /// <summary>최적화 간격: 끌 때 다음 모양이 앞 도장과 겹치지 않는 첫 자리마다 찍는다(투명한 둘레는 셈하지 않음 — 붙어 이어지되 겹치지 않음).</summary>
    public bool ShapePenOptimizedSpacing
    {
        get => _shapePenOptimizedSpacing;
        set { if (_shapePenOptimizedSpacing == value) return; _shapePenOptimizedSpacing = value; OptionsChanged(); }
    }

    /// <summary>세트 모음을 고친 뒤 부른다: 고른 세트·다음 모양을 맞추고 저장·화면을 알린다.</summary>
    public void NotifyShapeSetsEdited()
    {
        ShapeSetsChanged?.Invoke(this, EventArgs.Empty);
        ShapePenChanged?.Invoke(this, EventArgs.Empty);
    }
}
