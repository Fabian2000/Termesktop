using System.Security;
using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class Calculator
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private Text? _displayText;
    private Text? _historyText;
    private string _current = "0";
    private string _expression = "";
    private bool _newNumber = true;
    private bool _justEvaluated;

    public Calculator(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"calc{_instanceId}";
    }

    public static string Title => "Calc";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- History -->
                <Text Name='{_prefix}_history' Width='100%' Height='1ch'
                    ForegroundColor='#666666' BackgroundColor='Inherit'
                    TextAlign='Right' PaddingRight='1ch' />

                <!-- Display -->
                <Text Name='{_prefix}_display' Width='100%' Height='2ch'
                    ForegroundColor='#ffffff' BackgroundColor='{Theme.Darker}'
                    TextAlign='Right' PaddingRight='1ch' Style='Bold'>0</Text>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Button grid -->
                {ButtonRow("C", "±", "%", "÷", "clr", "sign", "pct", "div")}
                {ButtonRow("7", "8", "9", "×", "n7", "n8", "n9", "mul")}
                {ButtonRow("4", "5", "6", "−", "n4", "n5", "n6", "sub")}
                {ButtonRow("1", "2", "3", "+", "n1", "n2", "n3", "add")}
                {ButtonRow("0", ".", "⌫", "=", "n0", "dot", "bsp", "eq")}

            </StackPanel>");

        _displayText = termui.GetWidget<Text>($"{_prefix}_display");
        _historyText = termui.GetWidget<Text>($"{_prefix}_history");

        foreach (var n in new[] { "n0", "n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8", "n9" })
        {
            var btn = termui.GetWidget<Button>($"{_prefix}_{n}");
            var digit = n[1..];
            if (btn is not null) btn.Click += (_, _) => AppendDigit(digit);
        }

        BindBtn(termui, "dot", AppendDot);
        BindBtn(termui, "bsp", Backspace);
        BindBtn(termui, "clr", Clear);
        BindBtn(termui, "sign", ToggleSign);
        BindBtn(termui, "pct", Percent);
        BindBtn(termui, "add", () => AppendOperator("+"));
        BindBtn(termui, "sub", () => AppendOperator("-"));
        BindBtn(termui, "mul", () => AppendOperator("*"));
        BindBtn(termui, "div", () => AppendOperator("/"));
        BindBtn(termui, "eq", Evaluate);
    }

    private void BindBtn(TermuiX.TermuiX termui, string id, Action action)
    {
        var btn = termui.GetWidget<Button>($"{_prefix}_{id}");
        if (btn is not null) btn.Click += (_, _) => action();
    }

    private string ButtonRow(string l1, string l2, string l3, string l4,
        string id1, string id2, string id3, string id4)
    {
        var opColor = id4 is "div" or "mul" or "sub" or "add" or "eq" ? "#ccaa44" : "#cccccc";
        var eqBg = id4 == "eq" ? Theme.Hover : "Inherit";

        return $@"
            <StackPanel Direction='Horizontal' Width='100%' Height='fill'
                BackgroundColor='Inherit' Justify='SpaceEvenly'>
                <Button Name='{_prefix}_{id1}' Width='fill' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch'>{SecurityElement.Escape(l1)}</Button>
                <Button Name='{_prefix}_{id2}' Width='fill' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch'>{SecurityElement.Escape(l2)}</Button>
                <Button Name='{_prefix}_{id3}' Width='fill' Height='100%'
                    BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='#cccccc' FocusTextColor='#ffffff'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch'>{SecurityElement.Escape(l3)}</Button>
                <Button Name='{_prefix}_{id4}' Width='fill' Height='100%'
                    BackgroundColor='{eqBg}' FocusBackgroundColor='{Theme.Hover}'
                    TextColor='{opColor}' FocusTextColor='#ffffff'
                    BorderStyle='None' TextAlign='Center'
                    PaddingTop='0ch' PaddingBottom='0ch'>{SecurityElement.Escape(l4)}</Button>
            </StackPanel>";
    }

    private void UpdateDisplay()
    {
        if (_displayText is not null) _displayText.Content = _current;
        if (_historyText is not null) _historyText.Content = _expression;
    }

    private void AppendDigit(string digit)
    {
        if (_justEvaluated) { _expression = ""; _justEvaluated = false; }
        if (_newNumber) { _current = digit; _newNumber = false; }
        else if (_current == "0") _current = digit;
        else _current += digit;
        UpdateDisplay();
    }

    private void AppendDot()
    {
        if (_justEvaluated) { _expression = ""; _justEvaluated = false; }
        if (_newNumber) { _current = "0."; _newNumber = false; }
        else if (!_current.Contains('.')) _current += ".";
        UpdateDisplay();
    }

    private void Backspace()
    {
        if (_current.Length > 1) _current = _current[..^1];
        else _current = "0";
        UpdateDisplay();
    }

    private void Clear()
    {
        _current = "0";
        _expression = "";
        _newNumber = true;
        _justEvaluated = false;
        UpdateDisplay();
    }

    private void ToggleSign()
    {
        if (_current.StartsWith('-')) _current = _current[1..];
        else if (_current != "0") _current = "-" + _current;
        UpdateDisplay();
    }

    private void Percent()
    {
        if (double.TryParse(_current, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            _current = (val / 100).ToString("G");
            UpdateDisplay();
        }
    }

    private void AppendOperator(string op)
    {
        if (_justEvaluated) _justEvaluated = false;

        // Add current number to expression
        _expression += _current + " " + op + " ";
        _newNumber = true;
        UpdateDisplay();
    }

    private void Evaluate()
    {
        if (string.IsNullOrEmpty(_expression)) return;

        var fullExpr = _expression + _current;
        try
        {
            var result = EvalExpression(fullExpr);
            var resultStr = double.IsNaN(result) || double.IsInfinity(result) ? "Error" : result.ToString("G10");

            _expression = fullExpr + " =";
            _current = resultStr;
            _newNumber = true;
            _justEvaluated = true;
            UpdateDisplay();
        }
        catch
        {
            _current = "Error";
            _expression = "";
            _newNumber = true;
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Evaluates a math expression with proper operator precedence.
    /// Supports +, -, *, / with standard precedence (PEMDAS without parentheses).
    /// </summary>
    private static double EvalExpression(string expr)
    {
        var tokens = Tokenize(expr);
        if (tokens.Count == 0) return double.NaN;

        // Shunting-yard: first handle * and /, then + and -
        // Pass 1: evaluate all * and /
        var simplified = new List<(double val, char op)>();
        double acc = tokens[0].val;

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            var op = tokens[i].op;
            var next = tokens[i + 1].val;

            if (op == '*')
                acc *= next;
            else if (op == '/')
                acc = next != 0 ? acc / next : double.NaN;
            else
            {
                // + or -: save accumulated value and start fresh
                simplified.Add((acc, op));
                acc = next;
            }
        }
        simplified.Add((acc, ' '));

        // Pass 2: evaluate all + and -
        double result = simplified[0].val;
        for (int i = 0; i < simplified.Count - 1; i++)
        {
            if (simplified[i].op == '+')
                result += simplified[i + 1].val;
            else if (simplified[i].op == '-')
                result -= simplified[i + 1].val;
        }

        return result;
    }

    private static List<(double val, char op)> Tokenize(string expr)
    {
        var result = new List<(double val, char op)>();
        var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i += 2)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                continue;

            char op = ' ';
            if (i + 1 < parts.Length)
            {
                op = parts[i + 1] switch
                {
                    "+" => '+',
                    "-" => '-',
                    "*" => '*',
                    "/" => '/',
                    "=" => ' ',
                    _ => ' ',
                };
            }

            result.Add((val, op));
        }

        return result;
    }
}
