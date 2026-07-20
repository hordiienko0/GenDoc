using System.Windows;
using GenDoc.Native;
using GenDoc.ViewModels.Recipients;

namespace GenDoc.Views.Recipients;

public partial class RecipientEditWindow : Window
{
    private readonly RecipientEditViewModel _viewModel;

    public RecipientEditWindow(RecipientEditViewModel viewModel)
    {
        InitializeComponent();
        DwmHelper.EnableDarkTitleBar(this);

        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(object? sender, EventArgs e)
    {
        DialogResult = _viewModel.WasSaved;
        Close();
    }
}
