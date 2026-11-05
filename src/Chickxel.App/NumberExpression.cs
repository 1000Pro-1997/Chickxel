using System.Globalization;
using Avalonia.Data.Converters;

namespace Chickxel.App;

/// <summary>Converts arithmetic entered in a numeric editor while leaving ordinary input and custom converters intact.</summary>
internal sealed class NumberExpression : IValueConverter
{
    private readonly NumericUpDown _box;
    private readonly IValueConverter? _original;

    public NumberExpression(NumericUpDown box)
    {
        _box = box;
        _original = box.TextConverter;
    }
    private static bool EndsInOperator(string text)
    {
        var trimmed = text.AsSpan().TrimEnd();
        return !trimmed.IsEmpty && trimmed[^1] is '+' or '-' or '*' or '/';
    }

    public bool IsExpression(string? text)
        => text is not null && new Parser(text, _box.NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat).TryEvaluate(out _);
    public bool IsArithmeticInput(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (EndsInOperator(text)) return true;
        if (_original is not null)
        {
            if (_original.Convert(text, typeof(decimal?), null, CultureInfo.CurrentCulture) is decimal) return false;
        }
        else if (decimal.TryParse(text, _box.ParsingNumberStyle, _box.NumberFormat, out _)) return false;
        for (int i = 0; i < text.Length; i++)
            if (text[i] is '*' or '/' or '+' or '(' or ')' || (text[i] == '-' && i > 0))
                return true;
        return false;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0) return _original?.Convert(value, targetType, parameter, culture);
        if (EndsInOperator(text)) throw new FormatException("Incomplete numeric expression.");

        // Let the editor (or its custom converter) handle literals, including grouping, percent suffixes and exponents.
        if (_original is not null)
        {
            var literal = _original.Convert(value, targetType, parameter, culture);
            if (literal is decimal) return literal;
        }
        else if (decimal.TryParse(text, _box.ParsingNumberStyle, _box.NumberFormat, out var literal))
        {
            return literal;
        }

        var parser = new Parser(text, _box.NumberFormat ?? culture.NumberFormat);
        if (!parser.TryEvaluate(out var result)) throw new FormatException("Invalid numeric expression.");

        int places = DecimalPlaces(_box.FormatString);
        // Division in an integer editor truncates toward zero; fractional editors round half away from zero.
        result = places == 0 ? decimal.Truncate(result) : Math.Round(result, places, MidpointRounding.AwayFromZero);
        return result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (_original is not null) return _original.ConvertBack(value, targetType, parameter, culture);
        if (_box.FormatString.Contains("{0", StringComparison.Ordinal))
            return string.Format(_box.NumberFormat, _box.FormatString, value);
        return (value as decimal?)?.ToString(_box.FormatString, _box.NumberFormat);
    }

    private static int DecimalPlaces(string format)
    {
        if (format.Length > 1 && (format[0] is 'F' or 'f' or 'N' or 'n') &&
            int.TryParse(format.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int digits))
            return Math.Clamp(digits, 0, 28);
        int point = format.IndexOf('.');
        if (point < 0) return 0;
        int count = 0;
        for (int i = point + 1; i < format.Length && format[i] is '0' or '#'; i++) count++;
        return Math.Min(count, 28);
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<char> _input;
        private readonly NumberFormatInfo _format;
        private int _index;
        private bool _hasOperator;

        public Parser(string text, NumberFormatInfo format)
        {
            _input = text.AsSpan();
            _format = format;
        }

        public bool TryEvaluate(out decimal result)
        {
            // A short input limit also bounds nesting depth in Factor (unary signs and parentheses).
            if (_input.Length > 256) { result = 0; return false; }
            try
            {
                result = Sum();
                SkipSpace();
                return _hasOperator && _index == _input.Length;
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            catch (DivideByZeroException) { }
            result = 0;
            return false;
        }

        private decimal Sum()
        {
            decimal value = Product();
            while (true)
            {
                SkipSpace();
                if (_index == _input.Length || _input[_index] is not ('+' or '-')) return value;
                char op = _input[_index++];
                _hasOperator = true;
                decimal right = Product();
                value = op == '+' ? checked(value + right) : checked(value - right);
            }
        }

        private decimal Product()
        {
            decimal value = Factor();
            while (true)
            {
                SkipSpace();
                if (_index == _input.Length || _input[_index] is not ('*' or '/')) return value;
                char op = _input[_index++];
                _hasOperator = true;
                decimal right = Factor();
                value = op == '*' ? checked(value * right) : checked(value / right);
            }
        }

        private decimal Factor()
        {
            SkipSpace();
            if (_index < _input.Length && _input[_index] is '+' or '-')
            {
                char sign = _input[_index++];
                decimal value = Factor();
                return sign == '-' ? checked(-value) : value;
            }
            if (_index < _input.Length && _input[_index] == '(')
            {
                _index++;
                _hasOperator = true;
                decimal value = Sum();
                SkipSpace();
                if (_index == _input.Length || _input[_index++] != ')') throw new FormatException();
                return value;
            }

            int start = _index;
            bool hasDigit = false;
            string decimalSeparator = _format.NumberDecimalSeparator;
            string groupSeparator = _format.NumberGroupSeparator;
            while (_index < _input.Length)
            {
                char c = _input[_index];
                if (char.IsDigit(c)) { hasDigit = true; _index++; }
                else if (decimalSeparator.Length > 0 && _input[_index..].StartsWith(decimalSeparator, StringComparison.Ordinal))
                    _index += decimalSeparator.Length;
                else if (groupSeparator.Length > 0 && !string.IsNullOrWhiteSpace(groupSeparator) &&
                    _input[_index..].StartsWith(groupSeparator, StringComparison.Ordinal))
                    _index += groupSeparator.Length;
                else break;
            }
            if (!hasDigit || !decimal.TryParse(_input[start.._index], NumberStyles.Number, _format, out var number))
                throw new FormatException();
            return number;
        }

        private void SkipSpace()
        {
            while (_index < _input.Length && char.IsWhiteSpace(_input[_index])) _index++;
        }
    }
}
