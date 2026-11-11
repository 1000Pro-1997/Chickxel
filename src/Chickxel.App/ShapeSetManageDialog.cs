using Avalonia.Controls.Documents;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>모양 펜 세트 관리 창에서 고른 동작(창을 닫은 뒤 도구 설정이 한다).</summary>
internal enum ShapeSetManageAction
{
    BrowseStore,
    Installed,
    Publish,
    Export,
    Import,
    Copy,
}

/// <summary>
/// 모양 펜 세트 관리: 고른 세트(첫 모양 썸네일·이름·내 세트/기본 세트/받은 세트)와 동작 줄 — 상점 둘러보기·설치된 세트(에셋 관리자)·상점에 올리기(기본 세트가 아닌 내 세트만)·
/// 세트 내보내기·세트 가져오기·내 세트로 복사(받은 세트만). 동작 단추를 누르면 그 동작으로 창이 닫히고(<see cref="Ask"/>가 돌려줌), 닫기·Esc는 아무것도 하지 않는다.
/// </summary>
internal sealed class ShapeSetManageDialog : DialogBase
{
    private const string HelpPage = "tools-shape-pen";
    private ShapeSetManageAction? _action;

    private ShapeSetManageDialog(StampSet? set, bool installed) : base(UiStrings.ShapePen_ManageTitle)
    {
        this.HelpAt(HelpPage, anchor: UiStrings.ShapePen_ExportSet);
        CloseOnly();
        bool builtIn = set is not null && StampBuiltIns.Ids.Contains(set.Id);

        var name = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(Ui.Label(set?.DisplayName ?? UiStrings.ShapePen_ManageNoSet, bold: true));
        if (set is not null)
        {
            var kind = Ui.Label(installed ? UiStrings.ShapePen_ManageReceived : builtIn ? UiStrings.ShapePen_ManageBuiltIn : UiStrings.ShapePen_ManageMine);
            kind.Foreground = AppTheme.DimTextBrush;
            kind.FontSize = AppTheme.SmallFontSize;
            name.Children.Add(kind);
        }
        var header = Ui.Row(new ToolOptionsPanel.StampView { Image = set?.Shapes.FirstOrDefault(), Width = 32, Height = 32 }, name);
        header.Spacing = 8;
        header.Margin = new Thickness(3, 0, 3, 8);
        Body.Children.Add(header);

        Body.Children.Add(Action(ShapeSetManageAction.BrowseStore, VectorIcon.Store, UiStrings.ShapePen_ManageStore, UiStrings.ShapePen_ManageStoreDesc, null));
        Body.Children.Add(Action(ShapeSetManageAction.Installed, VectorIcon.Package, UiStrings.ShapePen_ManageInstalled, UiStrings.ShapePen_ManageInstalledDesc,
            UiStrings.ShapePen_ManagerTip));
        Body.Children.Add(Action(ShapeSetManageAction.Publish, VectorIcon.Globe, UiStrings.ShapePen_ManagePublish, UiStrings.ShapePen_ManagePublishDesc,
            UiStrings.ShapePen_PublishSetTip, enabled: set is not null && !installed && !builtIn));
        Body.Children.Add(Action(ShapeSetManageAction.Export, VectorIcon.Upload, UiStrings.ShapePen_ExportSet, UiStrings.ShapePen_ManageExportDesc,
            UiStrings.ShapePen_ExportSetTip, enabled: set is not null).HelpAt(HelpPage, anchor: UiStrings.ShapePen_ExportSet));
        Body.Children.Add(Action(ShapeSetManageAction.Import, VectorIcon.Download, UiStrings.ShapePen_ImportSet, UiStrings.ShapePen_ManageImportDesc,
            UiStrings.ShapePen_ImportSetTip).HelpAt(HelpPage, anchor: UiStrings.ShapePen_ImportSet));
        if (installed)
            Body.Children.Add(Action(ShapeSetManageAction.Copy, VectorIcon.Copy, UiStrings.ShapePen_ManageCopy, UiStrings.ShapePen_ManageCopyDesc, UiStrings.ShapePen_CopySetTip));
    }

    /// <summary>동작 줄: 아이콘 단추(누르면 그 동작으로 닫힘) + 한 줄 설명(굵은 이름 — 하는 일). 꺼진 줄은 설명도 흐리고, 단추 풍선은 꺼져 있어도 보인다(까닭).</summary>
    private StackPanel Action(ShapeSetManageAction action, VectorIcon icon, string title, string description, string? tip, bool enabled = true)
    {
        var button = Ui.IconButton(icon, tip ?? title, (_, _) =>
        {
            _action = action;
            DialogResult = DialogResult.OK;
        }, width: 32, size: 16);
        button.IsEnabled = enabled;
        ToolTip.SetShowOnDisabled(button, true);
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = enabled ? AppTheme.TextBrush : AppTheme.DimTextBrush };
        text.Inlines!.Add(new Run(title) { FontWeight = FontWeight.Bold });
        text.Inlines.Add(new Run(" — " + description));
        var row = Ui.Row(button, text);
        row.Spacing = 6;
        return row;
    }

    /// <summary>창을 띄워 고른 동작을 돌려준다(닫기·Esc면 null). 창은 돌려주기 전에 닫혀 있다.</summary>
    public static ShapeSetManageAction? Ask(Visual owner, StampSet? set, bool installed)
    {
        var dialog = new ShapeSetManageDialog(set, installed);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._action : null;
    }
}
