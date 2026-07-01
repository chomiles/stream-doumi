using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StreamDoumi;

public sealed class ScreenSelectionOverlayWindow : Window
{
    private const int EdgeSize = 12;
    private const int MinSelectionSize = 80;

    private readonly Canvas _canvas = new();
    private readonly Border _selection = new();
    private readonly TextBlock _label = new();
    private readonly Button _applyButton = new();
    private readonly Rect _virtualScreen;
    private DragMode _dragMode = DragMode.None;
    private Point _dragStart;
    private Int32Rect _startRect;
    private Int32Rect _currentRect;

    public event Action<ScreenSelectionResult>? SelectionCompleted;

    public ScreenSelectionOverlayWindow(Int32Rect initialRect)
    {
        _virtualScreen = NativeMethods.GetVirtualScreenRect();
        _currentRect = NativeMethods.IsRectInsideAnyMonitor(initialRect)
            ? NativeMethods.ClampRectToNearestMonitor(initialRect)
            : NativeMethods.CenterOnPrimaryMonitor(640, 480);

        Title = "StreamDoumi 영역 지정";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(150, 14, 16, 22));
        Topmost = true;
        ShowInTaskbar = false;
        Left = _virtualScreen.Left;
        Top = _virtualScreen.Top;
        Width = _virtualScreen.Width;
        Height = _virtualScreen.Height;
        Content = _canvas;

        BuildToolbar();
        BuildSelectionBox();

        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            Focus();
            RenderSelection();
        };
    }

    private void BuildToolbar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromArgb(230, 15, 17, 23))
        };
        Canvas.SetLeft(panel, 16);
        Canvas.SetTop(panel, 16);
        Panel.SetZIndex(panel, 10);

        panel.Children.Add(CreateToolbarButton("위치 초기화", (_, _) =>
        {
            _currentRect = NativeMethods.CenterOnPrimaryMonitor(640, 480);
            RenderSelection();
        }));
        panel.Children.Add(CreateToolbarButton("취소", (_, _) => Close()));

        _canvas.Children.Add(panel);
    }

    private static Button CreateToolbarButton(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(37, 42, 53)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 59, 73)),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        button.Click += click;
        return button;
    }

    private void BuildSelectionBox()
    {
        _selection.Background = new SolidColorBrush(Color.FromArgb(48, 66, 211, 146));
        _selection.BorderBrush = new SolidColorBrush(Color.FromRgb(66, 211, 146));
        _selection.BorderThickness = new Thickness(3);
        _selection.CornerRadius = new CornerRadius(6);
        _selection.MouseLeftButtonDown += Selection_MouseLeftButtonDown;
        _selection.MouseMove += Selection_MouseMove;

        var grid = new Grid();
        grid.Children.Add(_label);

        _applyButton.Content = "이 위치와 크기로 지정";
        _applyButton.Padding = new Thickness(18, 12, 18, 12);
        _applyButton.Background = new SolidColorBrush(Color.FromRgb(66, 211, 146));
        _applyButton.BorderBrush = new SolidColorBrush(Color.FromRgb(66, 211, 146));
        _applyButton.Foreground = new SolidColorBrush(Color.FromRgb(7, 17, 12));
        _applyButton.FontSize = 17;
        _applyButton.FontWeight = FontWeights.Bold;
        _applyButton.HorizontalAlignment = HorizontalAlignment.Center;
        _applyButton.VerticalAlignment = VerticalAlignment.Center;
        _applyButton.Cursor = Cursors.Hand;
        _applyButton.Click += (_, _) => CompleteSelection();
        grid.Children.Add(_applyButton);

        _label.Foreground = Brushes.White;
        _label.Background = new SolidColorBrush(Color.FromArgb(210, 15, 17, 23));
        _label.Padding = new Thickness(8, 5, 8, 5);
        _label.Margin = new Thickness(10);
        _label.FontSize = 14;
        _label.FontWeight = FontWeights.SemiBold;
        _label.VerticalAlignment = VerticalAlignment.Top;
        _label.HorizontalAlignment = HorizontalAlignment.Left;

        _selection.Child = grid;
        _canvas.Children.Add(_selection);
    }

    private void Selection_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragMode = HitTestDragMode(e.GetPosition(_selection));
        _dragStart = e.GetPosition(this);
        _startRect = _currentRect;
        CaptureMouse();
        e.Handled = true;
    }

    private void Selection_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode != DragMode.None)
        {
            return;
        }

        _selection.Cursor = CursorForMode(HitTestDragMode(e.GetPosition(_selection)));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        var current = e.GetPosition(this);
        var dx = (int)Math.Round(current.X - _dragStart.X);
        var dy = (int)Math.Round(current.Y - _dragStart.Y);
        _currentRect = NativeMethods.ClampRectToNearestMonitor(RectFromDrag(_startRect, dx, dy, _dragMode));
        RenderSelection();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            ReleaseMouseCapture();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            CompleteSelection();
        }
    }

    private void CompleteSelection()
    {
        SelectionCompleted?.Invoke(new ScreenSelectionResult(_currentRect, NativeMethods.GetMonitorNumber(_currentRect)));
        Close();
    }

    private DragMode HitTestDragMode(Point point)
    {
        var left = point.X <= EdgeSize;
        var right = point.X >= _selection.ActualWidth - EdgeSize;
        var top = point.Y <= EdgeSize;
        var bottom = point.Y >= _selection.ActualHeight - EdgeSize;

        if (left && top) return DragMode.TopLeft;
        if (right && top) return DragMode.TopRight;
        if (left && bottom) return DragMode.BottomLeft;
        if (right && bottom) return DragMode.BottomRight;
        if (left) return DragMode.Left;
        if (right) return DragMode.Right;
        if (top) return DragMode.Top;
        if (bottom) return DragMode.Bottom;
        return DragMode.Move;
    }

    private static Cursor CursorForMode(DragMode mode)
    {
        return mode switch
        {
            DragMode.Left or DragMode.Right => Cursors.SizeWE,
            DragMode.Top or DragMode.Bottom => Cursors.SizeNS,
            DragMode.TopLeft or DragMode.BottomRight => Cursors.SizeNWSE,
            DragMode.TopRight or DragMode.BottomLeft => Cursors.SizeNESW,
            DragMode.Move => Cursors.SizeAll,
            _ => Cursors.Arrow
        };
    }

    private static Int32Rect RectFromDrag(Int32Rect rect, int dx, int dy, DragMode mode)
    {
        var left = rect.X;
        var top = rect.Y;
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;

        if (mode == DragMode.Move)
        {
            return new Int32Rect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
        }

        if (mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft) left += dx;
        if (mode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight) right += dx;
        if (mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight) top += dy;
        if (mode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight) bottom += dy;

        if (right - left < MinSelectionSize)
        {
            if (mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft) left = right - MinSelectionSize;
            else right = left + MinSelectionSize;
        }

        if (bottom - top < MinSelectionSize)
        {
            if (mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight) top = bottom - MinSelectionSize;
            else bottom = top + MinSelectionSize;
        }

        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private void RenderSelection()
    {
        var left = _currentRect.X - _virtualScreen.Left;
        var top = _currentRect.Y - _virtualScreen.Top;
        Canvas.SetLeft(_selection, left);
        Canvas.SetTop(_selection, top);
        _selection.Width = _currentRect.Width;
        _selection.Height = _currentRect.Height;

        var monitorNumber = NativeMethods.GetMonitorNumber(_currentRect);
        _label.Text = $"모니터 {monitorNumber}  위치 {_currentRect.X}, {_currentRect.Y}  크기 {_currentRect.Width}, {_currentRect.Height}";
    }

    private enum DragMode
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}

public sealed record ScreenSelectionResult(Int32Rect Rect, int MonitorNumber);

public sealed record MonitorDisplay(int Number, Rect Bounds, bool IsPrimary);

internal static class NativeMethods
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;

    public static Rect GetVirtualScreenRect()
    {
        return new Rect(
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    public static int GetMonitorNumber(Int32Rect rect)
    {
        var nativeRect = new NativeRect
        {
            Left = rect.X,
            Top = rect.Y,
            Right = rect.X + rect.Width,
            Bottom = rect.Y + rect.Height
        };
        var monitor = MonitorFromRect(ref nativeRect, MonitorDefaultToNearest);
        var targetCenterX = nativeRect.Left + (nativeRect.Right - nativeRect.Left) / 2;
        var targetCenterY = nativeRect.Top + (nativeRect.Bottom - nativeRect.Top) / 2;
        var index = 1;
        var found = 1;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        return found;

        bool Callback(IntPtr handle, IntPtr hdc, ref NativeRect monitorRect, IntPtr data)
        {
            if (handle == monitor ||
                (targetCenterX >= monitorRect.Left && targetCenterX < monitorRect.Right &&
                 targetCenterY >= monitorRect.Top && targetCenterY < monitorRect.Bottom))
            {
                found = index;
                return false;
            }

            index++;
            return true;
        }
    }

    public static IReadOnlyList<MonitorDisplay> GetMonitors()
    {
        var monitors = new List<MonitorDisplay>();
        var index = 1;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return monitors;

        bool Callback(IntPtr handle, IntPtr hdc, ref NativeRect monitorRect, IntPtr data)
        {
            var info = new MonitorInfo();
            info.Size = Marshal.SizeOf<MonitorInfo>();

            var bounds = new Rect(
                monitorRect.Left,
                monitorRect.Top,
                monitorRect.Right - monitorRect.Left,
                monitorRect.Bottom - monitorRect.Top);
            var isPrimary = false;

            if (GetMonitorInfo(handle, ref info))
            {
                isPrimary = (info.Flags & MonitorInfoPrimary) == MonitorInfoPrimary;
            }

            monitors.Add(new MonitorDisplay(index, bounds, isPrimary));
            index++;
            return true;
        }
    }

    public static bool IsRectInsideAnyMonitor(Int32Rect rect)
    {
        return GetMonitors().Any(m => RectIntersects(m.Bounds, rect));
    }

    public static Int32Rect CenterOnPrimaryMonitor(int width, int height)
    {
        var monitor = GetMonitors().FirstOrDefault(m => m.IsPrimary)
            ?? GetMonitors().FirstOrDefault();

        if (monitor is null)
        {
            return new Int32Rect(0, 0, width, height);
        }

        var clampedWidth = Math.Min(width, (int)monitor.Bounds.Width);
        var clampedHeight = Math.Min(height, (int)monitor.Bounds.Height);
        var x = (int)Math.Round(monitor.Bounds.Left + (monitor.Bounds.Width - clampedWidth) / 2.0);
        var y = (int)Math.Round(monitor.Bounds.Top + (monitor.Bounds.Height - clampedHeight) / 2.0);
        return new Int32Rect(x, y, clampedWidth, clampedHeight);
    }

    public static Int32Rect ClampRectToNearestMonitor(Int32Rect rect)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return rect;
        }

        var centerX = rect.X + rect.Width / 2.0;
        var centerY = rect.Y + rect.Height / 2.0;
        var monitor = monitors
            .OrderBy(m =>
            {
                var monitorCenterX = m.Bounds.Left + m.Bounds.Width / 2.0;
                var monitorCenterY = m.Bounds.Top + m.Bounds.Height / 2.0;
                return Math.Pow(centerX - monitorCenterX, 2) + Math.Pow(centerY - monitorCenterY, 2);
            })
            .First();

        var width = Math.Min(rect.Width, Math.Max(1, (int)monitor.Bounds.Width));
        var height = Math.Min(rect.Height, Math.Max(1, (int)monitor.Bounds.Height));
        var x = Math.Clamp(rect.X, (int)monitor.Bounds.Left, (int)monitor.Bounds.Right - width);
        var y = Math.Clamp(rect.Y, (int)monitor.Bounds.Top, (int)monitor.Bounds.Bottom - height);
        return new Int32Rect(x, y, width, height);
    }

    private static bool RectIntersects(Rect monitor, Int32Rect rect)
    {
        return rect.X < monitor.Right &&
               rect.X + rect.Width > monitor.Left &&
               rect.Y < monitor.Bottom &&
               rect.Y + rect.Height > monitor.Top;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
