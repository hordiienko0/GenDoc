using System.Windows;
using System.Windows.Controls;
using GenDoc.ViewModels.Templates.Builder;

namespace GenDoc.Views.Templates
{
    public partial class TemplateBuilderView : UserControl
    {
        // Куди вставляти мітку з палітри. Позиція курсора — стан самого TextBox,
        // тому це залишається у в'юсі, а не в'ю-моделі.
        private TextBox? _caretTarget;

        public TemplateBuilderView()
        {
            InitializeComponent();
        }

        /// <summary>Тягнемо вліво — панель перегляду ширшає. Знак від'ємний, бо
        /// роздільник стоїть на її лівому краю.</summary>
        private void PreviewResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (DataContext is not TemplateBuilderViewModel viewModel) return;

            // Ширина панелі завжди явна (PreviewPanelWidth), тому підхоплювати
            // «ту, що на екрані», більше не потрібно — просто зсув.
            viewModel.PreviewWidth = Math.Clamp(
                viewModel.PreviewWidth - e.HorizontalChange,
                TemplateBuilderViewModel.PreviewMinWidth,
                TemplateBuilderViewModel.PreviewMaxWidth);
        }

        private void BlockText_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            _caretTarget = textBox;

            // Клік у поле робить блок «редагованим» — акцентна рамка з макета.
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
                    viewModel.StatusMessage = "Спершу поставте курсор у текст блока — мітка вставиться в цю позицію.";
                return;
            }

            var position = _caretTarget.SelectionStart;
            _caretTarget.SelectedText = tag;
            _caretTarget.SelectionStart = position + tag.Length;
            _caretTarget.SelectionLength = 0;
            _caretTarget.Focus();
        }

        // Рядок підпису теж лежить у блоці, тому підіймаємось по DataContext-ах,
        // а не по типу елемента.
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
