using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        vm.MappingPanelWidth = Math.Clamp(
            vm.MappingPanelWidth - e.HorizontalChange,
            TemplatesViewModel.MappingPanelMinWidth,
            TemplatesViewModel.MappingPanelMaxWidth);
    }
}
