using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class PersonEditWindow : Window
{
    private const double MinZoom = 0.8;
    private const double MaxZoom = 1.8;
    private const double ZoomStep = 0.1;

    private const double FieldGap = 16;
    private const double MinFieldWidth = 300;
    private const int MaxColumns = 3;
    // Смуга прокрутки (~17) + запас: поля мають вміщатись і тоді, коли смуга з'являється.
    private const double ScrollBarReserve = 22;

    // Розмір, положення-максимізація й масштаб пам'ятаємо до закриття програми - наступне вікно відкривається таким самим.
    private static double _zoom = 1.0;
    private static Size? _lastSize;
    private static WindowState _lastState = WindowState.Normal;

    public static readonly DependencyProperty FieldWidthProperty = DependencyProperty.Register(
        nameof(FieldWidth), typeof(double), typeof(PersonEditWindow), new PropertyMetadata(MinFieldWidth));

    public double FieldWidth
    {
        get => (double)GetValue(FieldWidthProperty);
        set => SetValue(FieldWidthProperty, value);
    }

    private readonly PersonEditDialogViewModel _viewModel;
    private bool _closeConfirmed;

    public PersonEditWindow(PersonEditDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        RestoreLayout();
        ApplyZoom(_zoom);

        viewModel.RequestClose += (_, _) => Close();
        viewModel.CardReplaced += () => FocusFirstField(scrollToTop: true);
        viewModel.SaveFailed += BringFirstErrorIntoView;

        Loaded += (_, _) => FocusFirstField(scrollToTop: false);
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void RestoreLayout()
    {
        var work = SystemParameters.WorkArea;
        var size = _lastSize ?? new Size(Width, Height);
        Width = Math.Min(size.Width, work.Width - 20);
        Height = Math.Min(size.Height, work.Height - 20);
        if (_lastState == WindowState.Maximized) WindowState = WindowState.Maximized;
    }

    private void RememberLayout()
    {
        _lastState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0) _lastSize = bounds.Size;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed) { RememberLayout(); return; }
        if (!_viewModel.NeedsCloseConfirmation) { RememberLayout(); return; }

        e.Cancel = true;
        if (!await _viewModel.ConfirmCloseAsync(this)) return;

        _closeConfirmed = true;
        _ = Dispatcher.BeginInvoke(Close);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!ctrl) return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                var command = shift ? _viewModel.SaveAndNextCommand : _viewModel.SaveCommand;
                if (command.CanExecute(null)) command.Execute(null);
                break;
            case Key.OemPlus:
            case Key.Add:
                e.Handled = true;
                ApplyZoom(_zoom + ZoomStep);
                break;
            case Key.OemMinus:
            case Key.Subtract:
                e.Handled = true;
                ApplyZoom(_zoom - ZoomStep);
                break;
            case Key.D0:
            case Key.NumPad0:
                e.Handled = true;
                ApplyZoom(1.0);
                break;
        }
    }

    // Esc ловимо вже після елементів: розкритий список «Придатність» сам забирає Esc, щоб лише згорнутись.
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        Close();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        ApplyZoom(_zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom + ZoomStep);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom - ZoomStep);
    private void ZoomReset_Click(object sender, MouseButtonEventArgs e) => ApplyZoom(1.0);

    private void ApplyZoom(double zoom)
    {
        _zoom = Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom), 1);
        ZoomRoot.LayoutTransform = Math.Abs(_zoom - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    // Ширина ScrollViewer не залежить від того, чи видно смугу прокрутки, - тож колонки не «стрибають» туди-сюди.
    private void BodyScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFieldWidth();

    private void UpdateFieldWidth()
    {
        var available = BodyScroll.ActualWidth - Body.Margin.Left - Body.Margin.Right - ScrollBarReserve;
        if (available <= 0) return;

        var columns = Math.Clamp((int)((available + FieldGap) / (MinFieldWidth + FieldGap)), 1, MaxColumns);
        FieldWidth = Math.Max(160, Math.Floor((available + FieldGap) / columns) - FieldGap);
    }

    private void FocusFirstField(bool scrollToTop)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (scrollToTop) BodyScroll.ScrollToTop();
            LastNameBox.Focus();
            Keyboard.Focus(LastNameBox);
        });
    }

    // Помилка може бути далеко внизу анкети (наприклад, дата) - прокручуємо до першої, щоб було видно, чому не зберіглось.
    private void BringFirstErrorIntoView()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var error = FindVisual<TextBlock>(Body, t =>
                t.Style == (Style)FindResource("ErrorTextStyle") && t.IsVisible && !string.IsNullOrEmpty(t.Text));
            if (error?.Parent is FrameworkElement field)
            {
                field.BringIntoView();
                if (FindVisual<TextBox>(field, _ => true) is { } box) box.Focus();
            }
        });
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && match(typed)) return typed;
            if (FindVisual(child, match) is { } found) return found;
        }
        return null;
    }

}
