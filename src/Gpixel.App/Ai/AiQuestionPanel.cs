using Avalonia.Automation;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

/// <summary>An inline, single-answer question displayed above the chat input.</summary>
internal sealed class AiQuestionPanel : Border
{
    private readonly Action<string?> _answer;
    private readonly TextBox _otherInput;
    private readonly Button _submit;
    private bool _answered;

    internal AiQuestionPanel(string question, IReadOnlyList<string> options, Action<string?> answer)
    {
        _answer = answer;
        Background = AppTheme.BackBrush;
        BorderBrush = AppTheme.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(5);
        Padding = new Thickness(10, 8);
        Margin = new Thickness(8, 4);

        var questionText = new TextBlock
        {
            Text = question,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold,
            Foreground = AppTheme.TextBrush,
        };
        AutomationProperties.SetName(questionText, question);

        var choices = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var option in options)
        {
            var label = option;
            choices.Children.Add(Ui.Button(label, (_, _) => Answer(label)));
        }

        _otherInput = new TextBox
        {
            Watermark = UiStrings.AiQuestion_OtherInput,
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(_otherInput, UiStrings.AiQuestion_OtherInput);
        _otherInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Submit();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Answer(null);
            }
        };

        _submit = Ui.Button(UiStrings.AiQuestion_Submit, (_, _) => Submit());
        _submit.IsEnabled = false;
        _otherInput.TextChanged += (_, _) => _submit.IsEnabled = !string.IsNullOrWhiteSpace(_otherInput.Text);
        var otherRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            IsVisible = false,
        };
        otherRow.Children.Add(_otherInput);
        Grid.SetColumn(_submit, 1);
        otherRow.Children.Add(_submit);

        choices.Children.Add(Ui.Button(UiStrings.AiQuestion_Other, (_, _) =>
        {
            otherRow.IsVisible = true;
            _otherInput.Focus();
        }));
        choices.Children.Add(Ui.Button(UiStrings.AiQuestion_Cancel, (_, _) => Answer(null)));
        Child = new StackPanel { Spacing = 6, Children = { questionText, choices, otherRow } };
    }

    private void Submit()
    {
        var text = _otherInput.Text?.Trim();
        if (!string.IsNullOrEmpty(text)) Answer(text);
    }

    private void Answer(string? value)
    {
        if (_answered) return;
        _answered = true;
        IsEnabled = false;
        _answer(value);
    }
}
