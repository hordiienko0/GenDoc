using System.Windows;
using GenDoc.ViewModels.Recipients;
using GenDoc.Views.Recipients;

namespace GenDoc.Services;

public class DialogService : IDialogService
{
    private readonly Dictionary<Type, Type> _viewModelToWindow = new()
    {
        [typeof(RecipientEditViewModel)] = typeof(RecipientEditWindow),
    };

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null)
        where TViewModel : notnull
    {
        if (!_viewModelToWindow.TryGetValue(viewModel.GetType(), out var windowType))
            throw new InvalidOperationException($"Для {viewModel.GetType().Name} не зареєстровано вікно діалогу.");

        var window = (Window)Activator.CreateInstance(windowType, viewModel)!;
        if (owner is not null)
        {
            window.Owner = owner;
        }

        return window.ShowDialog();
    }
}
