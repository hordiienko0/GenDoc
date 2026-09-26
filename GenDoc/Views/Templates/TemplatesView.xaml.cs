using System;
using System.Windows;
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

    private void MappingResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not TemplatesViewModel vm) return;

        // Списку лишаємо щонайменше ~400 px, інакше в ньому не видно навіть назви вибраного шаблона.
        var max = Math.Max(TemplatesViewModel.MappingPanelMinWidth,
            Math.Min(TemplatesViewModel.MappingPanelMaxWidth, ActualWidth - 400));

        vm.MappingPanelWidth = Math.Clamp(
            vm.MappingPanelWidth - e.HorizontalChange,
            TemplatesViewModel.MappingPanelMinWidth,
            max);
    }

    /// <summary>
    /// Межа між «Назвою» і «Файлом»: зсув у пікселях переводиться в частки-зірки за поточною сумарною
    /// шириною двох колонок у заголовку, де стоїть ручка.
    /// </summary>
    private void NameColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not TemplatesViewModel vm || sender is not FrameworkElement { Parent: Grid header }) return;
        if (header.ColumnDefinitions.Count < 2) return;

        var pixels = header.ColumnDefinitions[0].ActualWidth + header.ColumnDefinitions[1].ActualWidth;
        var stars = vm.NameColumnLength.Value + vm.FileColumnLength.Value;
        if (pixels <= 0) return;

        vm.ResizeNameColumn(e.HorizontalChange * stars / pixels);
    }
}
