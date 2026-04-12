using TermuiX;
using TermuiX.Widgets;
using Termesktop.Components;

namespace Termesktop.Apps;

public class StopWatch
{
    private static int _instanceCount;

    private readonly TermuiX.TermuiX _termui;
    private readonly int _instanceId;
    private readonly string _prefix;

    private Text? _bigTime;
    private Text? _modeLabel;
    private Text? _lapText;
    private Button? _mainBtn;
    private Button? _secondBtn;

    private bool _running;
    private DateTime _startTime;
    private TimeSpan _elapsed;
    private TimeSpan _pausedElapsed;
    private readonly List<(int num, TimeSpan time)> _laps = [];

    private bool _timerMode;
    private int _timerMinutes = 5;

    public StopWatch(TermuiX.TermuiX termui)
    {
        _termui = termui;
        _instanceId = _instanceCount++;
        _prefix = $"sw{_instanceId}";
    }

    public static string Title => "Clock";

    public void BuildContent(Container contentArea, TermuiX.TermuiX termui)
    {
        contentArea.Add($@"
            <StackPanel Direction='Vertical' Width='100%' Height='100%' BackgroundColor='Inherit'>

                <!-- Mode selector -->
                <StackPanel Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='{Theme.Subtle}' Justify='Center'>
                    <Button Name='{_prefix}_modeSw' Width='14ch' Height='1ch'
                        BackgroundColor='{Theme.Hover}' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#ffffff' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>⏱ Stopwatch</Button>
                    <Button Name='{_prefix}_modeTimer' Width='14ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#888888' FocusTextColor='#cccccc'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>⏰ Timer</Button>
                </StackPanel>

                <!-- Big time display -->
                <Text Name='{_prefix}_mode' Width='100%' Height='1ch'
                    ForegroundColor='#666666' BackgroundColor='Inherit'
                    TextAlign='Center'>STOPWATCH</Text>
                <Text Width='100%' Height='1ch' BackgroundColor='Inherit' />
                <Text Name='{_prefix}_bigTime' Width='100%' Height='1ch'
                    ForegroundColor='#ffffff' BackgroundColor='Inherit'
                    TextAlign='Center' Style='Bold'>00:00.0</Text>
                <Text Width='100%' Height='1ch' BackgroundColor='Inherit' />

                <!-- Timer duration selector (hidden for stopwatch) -->
                <StackPanel Name='{_prefix}_timerSetup' Direction='Horizontal' Width='100%' Height='1ch'
                    BackgroundColor='Inherit' Justify='Center' Align='Center' Visible='false'>
                    <Button Name='{_prefix}_minus' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>◀</Button>
                    <Text Name='{_prefix}_timerVal' Width='12ch' Height='1ch'
                        ForegroundColor='#cccccc' BackgroundColor='Inherit'
                        TextAlign='Center'>5 min</Text>
                    <Button Name='{_prefix}_plus' Width='4ch' Height='1ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='None' TextAlign='Center'
                        PaddingTop='0ch' PaddingBottom='0ch'>▶</Button>
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Controls - big centered buttons -->
                <StackPanel Direction='Horizontal' Width='100%' Height='3ch'
                    BackgroundColor='Inherit' Justify='Center' Align='Center'>
                    <Button Name='{_prefix}_main' Width='14ch'
                        BackgroundColor='{Theme.Hover}' FocusBackgroundColor='{Theme.Lighter}'
                        TextColor='#88cc88' FocusTextColor='#ffffff'
                        BorderStyle='Single' RoundedCorners='true'
                        TextAlign='Center'>▶  Start</Button>
                    <Button Name='{_prefix}_second' Width='14ch' MarginLeft='2ch'
                        BackgroundColor='Inherit' FocusBackgroundColor='{Theme.Hover}'
                        TextColor='#cccccc' FocusTextColor='#ffffff'
                        BorderStyle='Single' RoundedCorners='true'
                        TextAlign='Center'>↺  Reset</Button>
                </StackPanel>

                <Line Orientation='Horizontal' Type='Solid' Width='100%'
                    ForegroundColor='{Theme.Border}' BackgroundColor='Inherit' />

                <!-- Laps / Info area -->
                <Container Width='100%' Height='fill' ScrollY='true' BackgroundColor='Inherit'>
                    <Text Name='{_prefix}_laps' Width='100%' Height='auto'
                        ForegroundColor='#aaaaaa' BackgroundColor='Inherit'
                        PaddingLeft='2ch' />
                </Container>

            </StackPanel>");

        _bigTime = termui.GetWidget<Text>($"{_prefix}_bigTime");
        _modeLabel = termui.GetWidget<Text>($"{_prefix}_mode");
        _lapText = termui.GetWidget<Text>($"{_prefix}_laps");
        _mainBtn = termui.GetWidget<Button>($"{_prefix}_main");
        _secondBtn = termui.GetWidget<Button>($"{_prefix}_second");

        if (_mainBtn is not null) _mainBtn.Click += (_, _) => OnMainButton();
        if (_secondBtn is not null) _secondBtn.Click += (_, _) => OnSecondButton();

        // Mode tabs
        var modeSw = termui.GetWidget<Button>($"{_prefix}_modeSw");
        var modeTimer = termui.GetWidget<Button>($"{_prefix}_modeTimer");
        var timerSetup = termui.GetWidget<StackPanel>($"{_prefix}_timerSetup");

        if (modeSw is not null) modeSw.Click += (_, _) =>
        {
            _timerMode = false;
            Reset();
            modeSw.BackgroundColor = Color.Parse(Theme.Hover);
            modeSw.TextColor = Color.Parse("#ffffff");
            if (modeTimer is not null) { modeTimer.BackgroundColor = Color.Parse("Inherit"); modeTimer.TextColor = Color.Parse("#888888"); }
            if (timerSetup is not null) timerSetup.Visible = false;
            if (_modeLabel is not null) _modeLabel.Content = "STOPWATCH";
            if (_secondBtn is not null) _secondBtn.Text = "↺  Reset";
        };

        if (modeTimer is not null) modeTimer.Click += (_, _) =>
        {
            _timerMode = true;
            Reset();
            modeTimer.BackgroundColor = Color.Parse(Theme.Hover);
            modeTimer.TextColor = Color.Parse("#ffffff");
            if (modeSw is not null) { modeSw.BackgroundColor = Color.Parse("Inherit"); modeSw.TextColor = Color.Parse("#888888"); }
            if (timerSetup is not null) timerSetup.Visible = true;
            if (_modeLabel is not null) _modeLabel.Content = "TIMER";
            if (_secondBtn is not null) _secondBtn.Text = "↺  Reset";
            UpdateTimerDisplay();
        };

        var minusBtn = termui.GetWidget<Button>($"{_prefix}_minus");
        if (minusBtn is not null) minusBtn.Click += (_, _) =>
        {
            _timerMinutes = Math.Max(1, _timerMinutes - 1);
            UpdateTimerDisplay();
        };
        var plusBtn = termui.GetWidget<Button>($"{_prefix}_plus");
        if (plusBtn is not null) plusBtn.Click += (_, _) =>
        {
            _timerMinutes = Math.Min(999, _timerMinutes + 1);
            UpdateTimerDisplay();
        };
    }

    private void UpdateTimerDisplay()
    {
        var val = termui_GetWidget($"{_prefix}_timerVal");
        if (val is not null) val.Content = $"{_timerMinutes} min";
        if (_bigTime is not null && !_running)
            _bigTime.Content = $"{_timerMinutes:D2}:00.0";
    }

    private Text? termui_GetWidget(string name) => _termui.GetWidget<Text>(name);

    public void Update()
    {
        if (!_running || _bigTime is null) return;

        _elapsed = DateTime.Now - _startTime + _pausedElapsed;

        if (_timerMode)
        {
            var total = TimeSpan.FromMinutes(_timerMinutes);
            var remaining = total - _elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
                _running = false;
                _bigTime.Content = "00:00.0";
                _bigTime.ForegroundColor = Color.Parse("#ff5555");
                if (_modeLabel is not null) _modeLabel.Content = "⏰  TIME'S UP!";
                if (_mainBtn is not null) _mainBtn.Text = "▶  Start";
                return;
            }
            _bigTime.Content = FormatTime(remaining);
        }
        else
        {
            _bigTime.Content = FormatTime(_elapsed);
        }
    }

    private void OnMainButton()
    {
        if (_running)
        {
            // Pause
            _running = false;
            _pausedElapsed = _elapsed;
            if (_mainBtn is not null) _mainBtn.Text = "▶  Resume";
            if (_mainBtn is not null) _mainBtn.TextColor = Color.Parse("#88cc88");
        }
        else
        {
            // Start
            _startTime = DateTime.Now;
            _running = true;
            if (_bigTime is not null) _bigTime.ForegroundColor = Color.Parse("#ffffff");
            if (_modeLabel is not null) _modeLabel.Content = _timerMode ? "TIMER" : "STOPWATCH";
            if (_mainBtn is not null) _mainBtn.Text = "⏸  Pause";
            if (_mainBtn is not null) _mainBtn.TextColor = Color.Parse("#cccc88");
        }
    }

    private void OnSecondButton()
    {
        if (_running && !_timerMode)
        {
            // Lap
            _laps.Add((_laps.Count + 1, _elapsed));
            UpdateLaps();
        }
        else
        {
            Reset();
        }
    }

    private void Reset()
    {
        _running = false;
        _elapsed = TimeSpan.Zero;
        _pausedElapsed = TimeSpan.Zero;
        _laps.Clear();

        if (_bigTime is not null)
        {
            _bigTime.ForegroundColor = Color.Parse("#ffffff");
            if (_timerMode)
                _bigTime.Content = $"{_timerMinutes:D2}:00.0";
            else
                _bigTime.Content = "00:00.0";
        }
        if (_lapText is not null) _lapText.Content = "";
        if (_mainBtn is not null) { _mainBtn.Text = "▶  Start"; _mainBtn.TextColor = Color.Parse("#88cc88"); }
        if (_secondBtn is not null) _secondBtn.Text = _timerMode ? "↺  Reset" : "↺  Reset";
    }

    private void UpdateLaps()
    {
        if (_lapText is null) return;
        var sb = new System.Text.StringBuilder();
        for (int i = _laps.Count - 1; i >= 0; i--)
        {
            var (num, time) = _laps[i];
            var delta = i > 0 ? time - _laps[i - 1].time : time;
            sb.AppendLine($"  Lap {num,-3}  {FormatTime(time)}   (+{FormatTime(delta)})");
        }
        _lapText.Content = sb.ToString();

        // Switch second button to Lap while running
        if (_secondBtn is not null && _running)
            _secondBtn.Text = "🏁  Lap";
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100}";
        return $"{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100}";
    }
}
