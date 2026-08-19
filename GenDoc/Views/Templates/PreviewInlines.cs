using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GenDoc.Services.Templates;

namespace GenDoc.Views.Templates
{
    /// <summary>
    /// Заливає рядок попереднього перегляду в TextBlock.Inlines. Через ItemsControl
    /// це не зробити: кожен фрагмент рядка має свою заливку, але переносити рядок
    /// вони мусять спільно - тобто це саме Inlines одного TextBlock, а не окремі
    /// елементи в панелі.
    /// </summary>
    public static class PreviewInlines
    {
        public static readonly DependencyProperty RunsProperty = DependencyProperty.RegisterAttached(
            "Runs",
            typeof(IEnumerable<PreviewRun>),
            typeof(PreviewInlines),
            new PropertyMetadata(null, OnRunsChanged));

        public static void SetRuns(DependencyObject element, IEnumerable<PreviewRun>? value)
            => element.SetValue(RunsProperty, value);

        public static IEnumerable<PreviewRun>? GetRuns(DependencyObject element)
            => (IEnumerable<PreviewRun>?)element.GetValue(RunsProperty);

        private static void OnRunsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock textBlock) return;

            textBlock.Inlines.Clear();
            if (e.NewValue is not IEnumerable<PreviewRun> runs) return;

            foreach (var run in runs)
                textBlock.Inlines.Add(Build(run));
        }

        private static Run Build(PreviewRun source)
        {
            var run = new Run(source.Text);

            switch (source.Kind)
            {
                case PreviewRunKind.DbValue:
                    run.Background = Brush("AccentSoftBrush");
                    break;

                case PreviewRunKind.ManualValue:
                    run.Background = Brush("WarningSoftBrush");
                    run.Foreground = Brush("WarnTextBrush");
                    run.FontFamily = new FontFamily("Segoe UI");
                    run.FontSize = 11;
                    break;
            }

            return run;
        }

        // Ресурси читаються напряму: Run - не FrameworkElement, StaticResource
        // всередині нього не резолвиться.
        private static Brush? Brush(string key)
            => Application.Current?.TryFindResource(key) as Brush;
    }
}
