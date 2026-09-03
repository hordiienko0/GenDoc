using System.Windows;
using System.Windows.Controls;
using GenDoc.ViewModels.Templates.Builder;

namespace GenDoc.Views.Templates
{
    public partial class TemplateBuilderView : UserControl
    {
        private TextBox? _caretTarget;

        public TemplateBuilderView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => PushSheetViewport();
        }

        private void PushSheetViewport()
        {
            if (DataContext is TemplateBuilderViewModel viewModel && SheetScrollViewer.ActualWidth > 0)
                viewModel.SetSheetViewport(SheetScrollViewer.ActualWidth, SheetScrollViewer.ActualHeight);
        }

        private void PreviewResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (DataContext is not TemplateBuilderViewModel viewModel) return;

            viewModel.PreviewWidth = Math.Clamp(
                viewModel.PreviewWidth - e.HorizontalChange,
                TemplateBuilderViewModel.PreviewMinWidth,
                TemplateBuilderViewModel.PreviewMaxWidth);
        }

        private void SheetScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is TemplateBuilderViewModel viewModel)
                viewModel.SetSheetViewport(e.NewSize.Width, e.NewSize.Height);
        }

        private void BlockText_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            _caretTarget = textBox;

            if (DataContext is TemplateBuilderViewModel viewModel
                && FindBlock(textBox) is { } block)
            {
                viewModel.SelectBlockCommand.Execute(block);
            }
        }

        private void PaletteField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string tag }) return;

            if (_caretTarget is null)
            {
                if (DataContext is TemplateBuilderViewModel viewModel)
                    viewModel.StatusMessage = "Спершу поставте курсор у текст блока - мітка вставиться в цю позицію.";
                return;
            }

            var position = _caretTarget.SelectionStart;
            _caretTarget.SelectedText = tag;
            _caretTarget.SelectionStart = position + tag.Length;
            _caretTarget.SelectionLength = 0;
            _caretTarget.Focus();
        }

        private static BuilderBlockViewModel? FindBlock(FrameworkElement element)
        {
            DependencyObject? current = element;

            while (current is not null)
            {
                if (current is FrameworkElement { DataContext: BuilderBlockViewModel block }) return block;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
