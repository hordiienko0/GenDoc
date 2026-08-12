using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Views.Templates;

public partial class TemplatesView : UserControl
{
    public TemplatesView()
    {
        InitializeComponent();
    }

    // Панель мапінгу праворуч; тягнемо її лівий край. Рух курсора вліво — від'ємний,
    // тому віднімаємо, щоб панель ставала ширшою.
    private void MappingResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not TemplatesViewModel vm) return;

        // Перше перетягування переводить панель з авторозміру у явну ширину, і
        // відлік починається саме з тієї ширини, яку оператор бачить на екрані —
        // інакше панель стрибнула б на давнє збережене число.
        if (vm.IsMappingPanelAutoSized)
        {
            if (sender is Thumb { } thumb && VisualTreeHelper.GetParent(thumb) is FrameworkElement panel)
                vm.MappingPanelWidth = panel.ActualWidth;

            vm.IsMappingPanelAutoSized = false;
        }

        vm.MappingPanelWidth = Math.Clamp(
            vm.MappingPanelWidth - e.HorizontalChange,
            TemplatesViewModel.MappingPanelMinWidth,
            TemplatesViewModel.MappingPanelMaxWidth);
    }
}
