using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HyprWin.App;

/// <summary>
/// Scrollable calendar popup that opens when the user clicks the clock in the top bar.
/// Navigate months with the arrow buttons or the mouse wheel.
/// Click the month/year label to jump back to today.
/// </summary>
public partial class CalendarPopupWindow : Window
{
    private DateTime _currentMonth;
    private readonly DateTime _today;
    private bool _closing;

    // Catppuccin Mocha palette
    private static readonly SolidColorBrush _fgBrush        = Frozen(0xcd, 0xd6, 0xf4);
    private static readonly SolidColorBrush _mutedbBrush    = Frozen(0x7f, 0x84, 0x9c);
    private static readonly SolidColorBrush _accentBrush    = Frozen(0x89, 0xb4, 0xfa); // blue
    private static readonly SolidColorBrush _greenBrush     = Frozen(0xa6, 0xe3, 0xa1); // Sa
    private static readonly SolidColorBrush _redBrush       = Frozen(0xf3, 0x8b, 0xa8); // So
    private static readonly SolidColorBrush _hoverBrush     = Frozen(0x31, 0x32, 0x44);
    private static readonly SolidColorBrush _transparentBg  = new(Colors.Transparent);

    public CalendarPopupWindow()
    {
        InitializeComponent();
        _today        = DateTime.Today;
        _currentMonth = new DateTime(_today.Year, _today.Month, 1);
        BuildCalendar();
    }

    // ──────────────── Calendar rendering ────────────────

    private void BuildCalendar()
    {
        // Format month/year in the current UI culture
        MonthYearLabel.Text = _currentMonth.ToString("MMMM yyyy");

        DaysGrid.Children.Clear();

        // Monday-first: offset = 0 for Mon, 1 for Tue, … 6 for Sun
        int startOffset = DayOfWeekOffset(_currentMonth.DayOfWeek);
        int daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);

        // Empty leading cells
        for (int i = 0; i < startOffset; i++)
            DaysGrid.Children.Add(EmptyCell());

        // Day cells
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date    = new DateTime(_currentMonth.Year, _currentMonth.Month, day);
            bool isTod  = date == _today;
            bool isSat  = date.DayOfWeek == DayOfWeek.Saturday;
            bool isSun  = date.DayOfWeek == DayOfWeek.Sunday;

            SolidColorBrush fg = isTod ? Brushes.White
                               : isSun ? _redBrush
                               : isSat ? _greenBrush
                               : _fgBrush;

            var tb = new TextBlock
            {
                Text                = day.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                FontSize            = 12,
                Foreground          = fg,
            };

            var cell = new Border
            {
                Width        = 30,
                Height       = 26,
                CornerRadius = new CornerRadius(6),
                Background   = isTod ? _accentBrush : _transparentBg,
                Child        = tb,
                Margin       = new Thickness(1),
                Cursor       = Cursors.Arrow,
            };

            // Hover highlight for non-today cells
            if (!isTod)
            {
                cell.MouseEnter += (s, _) => ((Border)s!).Background = _hoverBrush;
                cell.MouseLeave += (s, _) => ((Border)s!).Background = _transparentBg;
            }

            DaysGrid.Children.Add(cell);
        }

        // Fill trailing empty cells to complete 6 rows (42 total)
        int filled   = startOffset + daysInMonth;
        int trailing = 42 - filled;
        for (int i = 0; i < trailing; i++)
            DaysGrid.Children.Add(EmptyCell());
    }

    /// <summary>Monday-first day-of-week column index (0 = Monday … 6 = Sunday).</summary>
    private static int DayOfWeekOffset(DayOfWeek dow)
        => dow == DayOfWeek.Sunday ? 6 : (int)dow - 1;

    private static Border EmptyCell() => new() { Width = 30, Height = 26, Margin = new Thickness(1) };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // ──────────────── Navigation ────────────────

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(1);
        BuildCalendar();
    }

    private void MonthYearLabel_Click(object sender, MouseButtonEventArgs e)
    {
        // Jump back to today's month
        _currentMonth = new DateTime(_today.Year, _today.Month, 1);
        BuildCalendar();
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(e.Delta < 0 ? 1 : -1);
        BuildCalendar();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Close when the user clicks outside the calendar
        if (!_closing) { _closing = true; Close(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        base.OnClosed(e);
    }

    // ──────────────── Window chrome via Win32 ────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Make WS_EX_TOOLWINDOW so it doesn't show in Alt+Tab
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex   = HyprWin.Core.Interop.NativeMethods.GetWindowLong(hwnd, HyprWin.Core.Interop.NativeMethods.GWL_EXSTYLE);
        HyprWin.Core.Interop.NativeMethods.SetWindowLong(hwnd, HyprWin.Core.Interop.NativeMethods.GWL_EXSTYLE,
            ex | (int)HyprWin.Core.Interop.NativeMethods.WS_EX_TOOLWINDOW);
    }
}
