using System.Globalization;
using System.Text;

namespace DestNote;

/// <summary>
/// A small, safe arithmetic evaluator for the amount fields.
/// Supports + - * /, parentheses, decimals and common full-width symbols.
/// </summary>
public static class ExpressionEvaluator
{
    private const decimal MaxAmount = 9_999_999.99m;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static bool IsAllowedInput(string? raw)
    {
        var text = Normalize(raw);
        if (text.Length > 60) return false;
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9' or '.' or '+' or '-' or '*' or '/' or '(' or ')')
                continue;
            return false;
        }
        return true;
    }

    public static bool TryEvaluate(string? raw, out decimal amount, out bool hasOperator)
    {
        amount = 0;
        hasOperator = false;
        var expression = Normalize(raw);
        if (expression.Length == 0 || expression.Length > 60)
            return false;

        hasOperator = expression.IndexOfAny(new[] { '+', '-', '*', '/', '(', ')' }) >= 0;
        var position = 0;
        try
        {
            var value = ParseExpression(expression, ref position, 0);
            if (position != expression.Length) return false;
            if (value <= 0 || value > MaxAmount) return false;
            amount = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return amount > 0 && amount <= MaxAmount;
        }
        catch
        {
            return false;
        }
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            var c = ch switch
            {
                >= '０' and <= '９' => (char)('0' + (ch - '０')),
                '．' => '.',
                '＋' => '+',
                '－' or '−' or '–' => '-',
                '×' or 'x' or 'X' => '*',
                '÷' => '/',
                '（' => '(',
                '）' => ')',
                '¥' or '￥' or ',' or '，' or '=' => '\0',
                ' ' or '\t' or '\r' or '\n' or '\u00A0' => '\0',
                _ => ch
            };
            if (c != '\0')
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static decimal ParseExpression(string s, ref int pos, int depth)
    {
        if (depth > 16) throw new FormatException();
        var value = ParseTerm(s, ref pos, depth);
        while (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
        {
            var op = s[pos++];
            var rhs = ParseTerm(s, ref pos, depth);
            value = op == '+' ? value + rhs : value - rhs;
        }
        return value;
    }

    private static decimal ParseTerm(string s, ref int pos, int depth)
    {
        var value = ParseFactor(s, ref pos, depth);
        while (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
        {
            var op = s[pos++];
            var rhs = ParseFactor(s, ref pos, depth);
            if (op == '/' && rhs == 0) throw new DivideByZeroException();
            value = op == '*' ? value * rhs : value / rhs;
        }
        return value;
    }

    private static decimal ParseFactor(string s, ref int pos, int depth)
    {
        if (pos >= s.Length) throw new FormatException();
        if (s[pos] == '+' || s[pos] == '-')
        {
            var negative = s[pos++] == '-';
            var value = ParseFactor(s, ref pos, depth + 1);
            return negative ? -value : value;
        }
        if (s[pos] == '(')
        {
            pos++;
            var value = ParseExpression(s, ref pos, depth + 1);
            if (pos >= s.Length || s[pos] != ')') throw new FormatException();
            pos++;
            return value;
        }

        var start = pos;
        while (pos < s.Length && char.IsAsciiDigit(s[pos])) pos++;
        if (pos < s.Length && s[pos] == '.')
        {
            pos++;
            while (pos < s.Length && char.IsAsciiDigit(s[pos])) pos++;
        }
        if (pos == start) throw new FormatException();
        var token = s[start..pos];
        if (!decimal.TryParse(token, NumberStyles.AllowDecimalPoint, Inv, out var number))
            throw new FormatException();
        return number;
    }
}
