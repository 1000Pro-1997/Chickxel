using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App.Ai;

/// <summary>
/// AI 차례가 도는 동안 보낸 메시지의 대기 줄(AI 채팅·효과 만들기 AI): 입력란 바로 위에 메시지마다 한 줄("대기 중" · 글 첫 줄 · 그림 수 · 빼기 단추)로
/// 보이고, 차례가 끝나면 부르는 쪽이 <see cref="TakeNext"/>로 하나씩 꺼내 보낸다. 빼기 단추는 입력란이 비어 있으면 그 메시지를 입력란으로 되돌리고
/// (고쳐 다시 보낼 수 있게), 아니면 버린다.
/// </summary>
internal sealed class PendingMessages : StackPanel
{
    /// <summary>대기 중인 메시지: 저장 모양의 글(칩 이름 "Image")과 첨부(칩 번호 순서).</summary>
    public sealed record Message(string Text, IReadOnlyList<ChatAttachment> Attachments);

    private readonly ChatImageInput _input;
    private readonly List<(Message Message, Control View)> _items = [];

    public PendingMessages(ChatImageInput input)
    {
        _input = input;
        Spacing = 3;
        Margin = new Thickness(0, 0, 0, 4);
        IsVisible = false;
    }

    public int Count => _items.Count;

    /// <summary>입력란의 지금 글·첨부를 대기 줄 끝에 넣고 입력란을 비운다. 보낼 것이 없으면 false.</summary>
    public bool AddFromInput()
    {
        var text = _input.StoredText();
        var attachments = _input.Attachments.ToList();
        if (text.Length == 0 && attachments.Count == 0) return false;
        var message = new Message(text, attachments);
        var view = Row(message);
        _items.Add((message, view));
        Children.Add(view);
        IsVisible = true;
        _input.Clear();
        return true;
    }

    /// <summary>맨 앞 메시지를 꺼낸다(줄도 지움). 없으면 null.</summary>
    public Message? TakeNext()
    {
        if (_items.Count == 0) return null;
        var (message, view) = _items[0];
        Remove(0, view);
        return message;
    }

    /// <summary>대기 줄을 비운다(새 대화·창 닫기).</summary>
    public void ClearAll()
    {
        _items.Clear();
        Children.Clear();
        IsVisible = false;
    }

    private void Remove(int index, Control view)
    {
        _items.RemoveAt(index);
        Children.Remove(view);
        IsVisible = _items.Count > 0;
    }

    private Control Row(Message message)
    {
        string text = ChatImageTokens.WithoutTokens(message.Text).ReplaceLineEndings(" ");
        if (message.Attachments.Count > 0)
            text = (text.Length > 0 ? text + " · " : "") + string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Chat_Pending_Images, message.Attachments.Count);
        var label = new TextBlock
        {
            Text = text, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var state = new TextBlock
        {
            Text = UiStrings.Chat_Pending, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Border? row = null;
        var remove = Ui.IconButton(VectorIcon.X, UiStrings.Chat_Pending_RemoveTip, (_, _) => Withdraw(row!), size: 11);
        remove.Padding = new Thickness(4, 2);
        remove.MinHeight = 0;
        remove.VerticalAlignment = VerticalAlignment.Center;
        var line = new DockPanel();
        DockPanel.SetDock(state, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        line.Children.AddRange([state, remove, label]);
        row = new Border
        {
            Background = AppTheme.PanelBrush, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 3, 3), Child = line,
        };
        ToolTip.SetTip(row, UiStrings.Chat_PendingTip);
        return row;
    }

    /// <summary>대기 줄에서 뺀다: 입력란이 비어 있으면 그 메시지를 입력란으로 되돌린다.</summary>
    private void Withdraw(Control view)
    {
        int index = _items.FindIndex(item => ReferenceEquals(item.View, view));
        if (index < 0) return;
        var message = _items[index].Message;
        Remove(index, view);
        if (_input.StoredText().Length == 0 && _input.Attachments.Count == 0) _input.Restore(message.Text, message.Attachments);
    }
}
