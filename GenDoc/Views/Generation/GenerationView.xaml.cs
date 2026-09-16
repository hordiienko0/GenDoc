using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GenDoc.Views.Generation;

public partial class GenerationView : UserControl
{
    public GenerationView()
    {
        InitializeComponent();
    }

    /// <summary>Поле перейменування пакета одразу бере фокус і виділяє назву - можна просто друкувати нову.</summary>
    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } box) return;

        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            box.SelectAll();
        });
    }
}
