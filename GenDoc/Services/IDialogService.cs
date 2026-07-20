using System.Windows;

namespace GenDoc.Services;

public interface IDialogService
{
    bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null)
        where TViewModel : notnull;
}
