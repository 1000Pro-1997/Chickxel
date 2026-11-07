using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>
/// 가져오는 에셋이 이미 있는 에셋과 겹칠 때 묻는 창(<see cref="AppAssets.InstallFile"/>·<see cref="AppAssets.InstallRemoteAsync"/>):
/// 가져오는 에셋(이름·종류·작성자·상점 버전)과 있는 에셋(작성자·버전, 고쳤는지, 내용이 같은지)을 보이고 바꾸기·건너뛰기·둘 다 두기를 고른다.
/// "남은 항목에도 적용"을 켜면 같은 파일의 나머지 겹침에 같은 답을 쓴다. 창을 닫으면 건너뛰기.
/// <see cref="AssetStore.Install"/>이 UI 스레드에서 동기로 묻으므로 닫힐 때까지 기다리는 모달(<see cref="DialogBase.ShowDialog"/>)이다.
/// </summary>
public sealed class AssetImportPrompt : DialogBase
{
    private readonly CheckBox _applyToRest = Ui.Check(UiStrings.AssetImport_ApplyToRest);

    private AssetImportPrompt(AssetImportItem item) : base(UiStrings.AssetImport_Title)
    {
        Body.MinWidth = 380;
        Body.MaxWidth = 520;
        Body.Spacing = 6;
        Body.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, UiStrings.AssetImport_Question, item.DisplayName),
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.TextBrush,
        });
        Body.Children.Add(Side(UiStrings.AssetImport_Incoming, item.Kind, item.DisplayName, item.Author, item.Store));
        if (item.Existing is { } existing)
        {
            Body.Children.Add(Side(UiStrings.AssetImport_Existing, existing.Kind, existing.DisplayName, existing.Author, existing.Store));
            if (existing.Modified) Body.Children.Add(Note(UiStrings.AssetImport_Modified, AppTheme.WarningBrush));
        }
        if (item.SameContent) Body.Children.Add(Note(UiStrings.AssetImport_SameContent, AppTheme.DimTextBrush));
        Body.Children.Add(_applyToRest);

        // 바꾸기(확인 단추) · 둘 다 두기 · 건너뛰기(취소 단추 — 창 닫기와 같음)
        Labelled(OkButton, VectorIcon.Swap, UiStrings.AssetImport_Replace);
        SetButtonResult(OkButton, DialogResult.Yes);
        Labelled(AddButton("", DialogResult.No), VectorIcon.Copy, UiStrings.AssetImport_KeepBoth);
        Labelled(CancelButtonControl, VectorIcon.ArrowRight, UiStrings.AssetImport_Skip);
    }

    /// <summary>
    /// <paramref name="item"/>을 어떻게 할지 묻는다(창을 닫으면 건너뛰기). <c>ApplyToRest</c>는 "남은 항목에도 적용"을 켰는지.
    /// </summary>
    public static (ImportChoice Choice, bool ApplyToRest) Ask(Visual? owner, AssetImportItem item)
    {
        var dialog = new AssetImportPrompt(item);
        var choice = dialog.ShowDialog(owner) switch
        {
            DialogResult.Yes => ImportChoice.Replace,
            DialogResult.No => ImportChoice.KeepBoth,
            _ => ImportChoice.Skip,
        };
        return (choice, dialog._applyToRest.On());
    }

    private static void Labelled(Button button, VectorIcon icon, string text)
    {
        button.Content = Ui.IconLabel(icon, text);
        button.Width = double.NaN;
        button.MinWidth = 90;
    }

    /// <summary>
    /// 한쪽(가져오는 것·있는 것) 묶음: 제목, 종류 아이콘과 이름, 작성자 배지 · 종류 · 버전.
    /// 상점에서 받은 에셋(<paramref name="store"/>가 있음)만 작성자 프로필 그림을 쓰고, 나머지는 사람 아이콘(에셋 관리자 목록과 같음).
    /// </summary>
    private static Control Side(string header, string kind, string name, AssetAuthor? author, AssetStoreLink? store)
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(Ui.Icon(AssetManagerWindow.KindIcon(kind), 13));
        title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis });
        string facts = string.Join(" · ", new[]
        {
            AssetManagerWindow.KindText(kind),
            store is null ? null : UiStrings.Assets_Version + " " + store.Version,
        }.Where(s => !string.IsNullOrEmpty(s)));
        var by = IdentityBadge.Author(store is null ? null : author, 16, fallback: author?.Name ?? UiStrings.Assets_SourceLocal, dim: true, caption: facts);
        return new Border
        {
            Background = AppTheme.InputBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = header, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize },
                    title,
                    by,
                },
            },
        };
    }

    private static TextBlock Note(string text, IBrush brush) => new() { Text = text, Foreground = brush, TextWrapping = TextWrapping.Wrap };
}
