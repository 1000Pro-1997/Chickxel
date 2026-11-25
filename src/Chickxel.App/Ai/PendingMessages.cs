using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App.Ai;

/// <summary>
/// AI 차례가 도는 동안 보낸 메시지의 대기 줄(AI 채팅·효과 만들기 AI): 입력란 바로 위에 메시지마다 한 줄
/// (끌기 손잡이 · 글 첫 줄 · 그림 수 · 고치기 · 지우기 · 지금 보내기)로 보이고, 차례가 끝나면 부르는 쪽이 <see cref="TakeNext"/>로 하나씩 꺼내 보낸다.
/// 손잡이를 끌어 보낼 차례를 바꾸고, 고치기는 그 메시지를 입력란으로 가져온다(입력란에 쓰던 글은 그 자리에 대기),
/// 지금 보내기는 그 메시지를 맨 앞으로 옮긴 뒤 <see cref="SendNowRequested"/>를 부른다(부르는 쪽이 도는 차례를 멈추고 바로 보냄).
/// </summary>
internal sealed class PendingMessages : StackPanel
{
    /// <summary>대기 중인 메시지: 저장 모양의 글(칩 이름 "Image")과 첨부(칩 번호 순서).</summary>
    public sealed record Message(string Text, IReadOnlyList<ChatAttachment> Attachments);

    private readonly ChatImageInput _input;
    private readonly List<Message> _items = [];
    private readonly DragReorder _drag = new();

    public PendingMessages(ChatImageInput input)
    {
        _input = input;
        Spacing = 3;
        Margin = new Thickness(0, 0, 0, 4);
        IsVisible = false;
        _drag.Moved += (from, to) => Dispatcher.UIThread.Post(() => Move(from, to));
    }

    /// <summary>지금 보내기: 그 메시지가 대기 줄 맨 앞에 와 있다. 부르는 쪽이 도는 차례를 멈추고 맨 앞 메시지를 보낸다.</summary>
    public event Action? SendNowRequested;

    public int Count => _items.Count;

    /// <summary>입력란의 지금 글·첨부를 대기 줄 끝에 넣고 입력란을 비운다. 보낼 것이 없으면 false.</summary>
    public bool AddFromInput()
    {
        var text = _input.StoredText();
        var attachments = _input.Attachments.ToList();
        if (text.Length == 0 && attachments.Count == 0) return false;
        _items.Add(new Message(text, attachments));
        Rebuild();
        _input.Clear();
        return true;
    }

    /// <summary>메시지 하나를 대기 줄 끝에 넣는다(그림 표시 창에서 보낸 것 — 입력란은 그대로).</summary>
    public void Add(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        _items.Add(new Message(text, attachments));
        Rebuild();
    }

    /// <summary>맨 앞 메시지를 꺼낸다(줄도 지움). 없으면 null.</summary>
    public Message? TakeNext()
    {
        if (_items.Count == 0) return null;
        var message = _items[0];
        _items.RemoveAt(0);
        Rebuild();
        return message;
    }

    /// <summary>대기 줄을 비운다(새 대화·창 닫기).</summary>
    public void ClearAll()
    {
        _items.Clear();
        Rebuild();
    }

    /// <summary>줄을 메시지 차례대로 다시 만든다(끌기 번호가 줄 차례라 넣기·빼기·옮기기마다).</summary>
    private void Rebuild()
    {
        _drag.Clear();
        Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            var row = Row(_items[i], i, out var grip);
            _drag.Attach(row, grip);
            Children.Add(row);
        }
        IsVisible = _items.Count > 0;
    }

    private void Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to) return;
        var message = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, message);
        Rebuild();
    }

    private Control Row(Message message, int index, out Control grip)
    {
        string text = ChatImageTokens.WithoutTokens(message.Text).ReplaceLineEndings(" ");
        if (message.Attachments.Count > 0)
            text = (text.Length > 0 ? text + " · " : "") + string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Chat_Pending_Images, message.Attachments.Count);
        var label = new TextBlock
        {
            Text = text, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(label, UiStrings.Chat_PendingTip);
        // 손잡이: 투명 바탕이라 아이콘 사이 빈 곳을 눌러도 끌린다.
        grip = new Border
        {
            Background = Brushes.Transparent, Padding = new Thickness(2, 0, 6, 0), Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            Child = new IconView(VectorIcon.Grip, AppTheme.DimTextBrush) { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(grip, UiStrings.Chat_Pending_DragTip);
        var edit = Small(Ui.IconButton(VectorIcon.Pencil, UiStrings.Chat_Pending_EditTip, (_, _) => Edit(index), size: 12));
        var remove = Small(Ui.IconButton(VectorIcon.Trash, UiStrings.Chat_Pending_RemoveTip, (_, _) => Remove(index), size: 12));
        var now = Small(Ui.Button(VectorIcon.CornerDownRight, UiStrings.Chat_Pending_SendNow, (_, _) => SendNow(index)).Tip(UiStrings.Chat_Pending_SendNowTip));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 0, 0, 0), Children = { edit, remove, now } };
        var line = new DockPanel();
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.AddRange([grip, buttons, label]);
        return new Border
        {
            Background = AppTheme.PanelBrush, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 3, 3, 3), Child = line,
        };
    }

    private static Button Small(Button b)
    {
        b.Padding = new Thickness(5, 2);
        b.MinHeight = 0;
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    /// <summary>입력란으로 가져와 고친다: 입력란에 쓰던 글·그림이 있으면 그것이 이 자리에 대기한다(어느 쪽도 버리지 않음).</summary>
    private void Edit(int index)
    {
        if (index >= _items.Count) return;
        var message = _items[index];
        var draft = new Message(_input.StoredText(), _input.Attachments.ToList());
        if (draft.Text.Length > 0 || draft.Attachments.Count > 0) _items[index] = draft;
        else _items.RemoveAt(index);
        Rebuild();
        _input.Restore(message.Text, message.Attachments);
        _input.FocusInput();
    }

    private void Remove(int index)
    {
        if (index >= _items.Count) return;
        _items.RemoveAt(index);
        Rebuild();
    }

    private void SendNow(int index)
    {
        if (index >= _items.Count) return;
        Move(index, 0);
        SendNowRequested?.Invoke();
    }
}
