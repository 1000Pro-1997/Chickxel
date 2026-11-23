using System.Runtime.CompilerServices;
using Avalonia.Media.Immutable;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 메뉴 줄 오른쪽, 계정 단추 바로 왼쪽의 갤러리 단추(가게 아이콘): 누르면 파일 > 갤러리에 올리기와 같다(문서가 없으면 꺼짐).
/// 그림이 있는 문서를 손으로 저장하면(자동 저장은 아님) 약 6초 동안 반짝여 공유를 권한다 — 공용 강조 반짝임(<see cref="Sparkle"/>)으로
/// 아이콘 뒤에 강조색 빛이 숨 쉬듯 퍼지고 작은 별이 번갈아 반짝이며, 아이콘도 그 세기만큼 강조색으로 물든다.
/// 누르면 바로 멈추고, 한 문서는 이번 실행에서 한 번만 반짝인다(갤러리에 올린 뒤 다시 저장하면 또).
/// </summary>
internal sealed class GalleryButton : Button
{
    /// <summary>반짝일 때 아이콘이 물드는 색.</summary>
    private static readonly Color TintColor = Color.FromRgb(150, 205, 255);

    private readonly IconView _icon = new(VectorIcon.Store) { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Sparkle _sparkle;
    /// <summary>이번 실행에서 이미 반짝여 권한 문서(올리면 빠짐).</summary>
    private readonly ConditionalWeakTable<SpriteProject, object> _invited = new();

    public GalleryButton(Action upload)
    {
        _sparkle = new Sparkle(_icon) { Width = 32, Height = 22 };
        _sparkle.Pulsed += Tint;
        _sparkle.Stopped += () => this.Tip(UiStrings.Gallery_ButtonTip);
        Content = _sparkle;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(4, 1);
        Margin = new Thickness(0, 0, 2, 0);
        MinHeight = 0;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        this.Tip(UiStrings.Gallery_ButtonTip);
        Click += (_, _) =>
        {
            Stop();
            upload();
        };
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>반짝이는 중인지(가이드 그림·시험용).</summary>
    public bool IsSparkling => _sparkle.IsSparkling;

    /// <summary>
    /// 문서 <paramref name="project"/>를 손으로 저장했다: 지금 문서이고 그림이 있으면, 이번 실행에서 그 문서로 처음일 때(올린 뒤 다시 저장했으면 또) 반짝인다.
    /// </summary>
    public void Saved(SpriteProject project, bool current)
    {
        if (!current || !IsEnabled || _invited.TryGetValue(project, out _) || !HasPicture(project)) return;
        _invited.AddOrUpdate(project, this);
        _sparkle.Start();
        this.Tip(UiStrings.Gallery_ButtonSparkleTip);
    }

    /// <summary>문서 <paramref name="project"/>를 갤러리에 올렸다: 다음에 저장하면 다시 권할 수 있다.</summary>
    public void Uploaded(SpriteProject project) => _invited.Remove(project);

    /// <summary>반짝임을 바로 멈춘다(문서를 닫았을 때 등).</summary>
    public void Stop() => _sparkle.Stop();

    /// <summary>반짝임 세기만큼 아이콘을 글자색에서 <see cref="TintColor"/> 쪽으로(멈추면 글자색 그대로).</summary>
    private void Tint(double? amount)
    {
        if (amount is not { } a)
        {
            _icon.ClearValue(IconView.ForegroundProperty);
            return;
        }
        var baseColor = Foreground is ISolidColorBrush solid ? solid.Color : AppTheme.TextColor;
        a = Math.Clamp(a, 0, 1);
        _icon.Foreground = new ImmutableSolidColorBrush(Color.FromArgb(baseColor.A,
            (byte)(baseColor.R + (TintColor.R - baseColor.R) * a),
            (byte)(baseColor.G + (TintColor.G - baseColor.G) * a),
            (byte)(baseColor.B + (TintColor.B - baseColor.B) * a)));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty && !IsEnabled) Stop();
    }

    /// <summary>보이는 칸 하나라도 그림이 있다(빈 새 문서는 권하지 않음 — 처음 투명이 아닌 픽셀에서 멈춤).</summary>
    private static bool HasPicture(SpriteProject project)
        => project.StoredCels.Any(c => !c.Cel.IsRegionEmpty(c.Cel.Bounds));
}
