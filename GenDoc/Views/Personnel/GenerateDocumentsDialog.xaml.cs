using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Views.Personnel;

public partial class GenerateDocumentsDialog : Window
{
    public GenerateDocumentsDialog(GenerateDocumentsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        var view = CollectionViewSource.GetDefaultView(viewModel.Templates);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TemplateChoiceItem.Group)));
        viewModel.RequestClose += (_, _) =>
        {
            DialogResult = viewModel.DialogResultValue;
            Close();
        };
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
