using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GenDoc.Services.Duty;
using GenDoc.ViewModels.Duty;

namespace GenDoc.Views.Duty;

public partial class EveningCheckView : UserControl
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private EveningCheckViewModel? _vm;

    public EveningCheckView()
    {
        InitializeComponent();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RebuildPreview();
        };
        DataContextChanged += (_, _) => Attach(DataContext as EveningCheckViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(EveningCheckViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += Vm_PropertyChanged;
    }

    private async void EveningCheckView_Loaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as EveningCheckViewModel);
        if (_vm is not null)
        {
            await _vm.InitializeCommand.ExecuteAsync(null);
            SchedulePreview();
        }
    }

    /// <summary>Перегляд перебудовується з невеликою затримкою: під час набору заголовка - раз, а не на кожну літеру.</summary>
    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EveningCheckViewModel.PreviewVersion) or nameof(EveningCheckViewModel.IsPreviewOpen))
            SchedulePreview();
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    /// <summary>Сторінки листа з <see cref="EveningCheckPrintLayout"/> - ті самі, що підуть на друк і в Excel.</summary>
    internal void RebuildPreview()
    {
        if (_vm is null || !_vm.IsPreviewOpen) return;

        PreviewPages.Children.Clear();
        var data = _vm.BuildData();
        if (data is null)
        {
            PreviewInfo.Text = _vm.PeriodError ?? "Вкажіть період.";
            return;
        }
        if (data.Groups.Count == 0)
        {
            PreviewInfo.Text = "Відмітьте хоча б один блок - набір або прикомандированих.";
            return;
        }

        var sheets = EveningCheckPrintLayout.Sheets(data);
        for (var i = 0; i < sheets.Count; i++)
        {
            var page = new Border
            {
                BorderBrush = (Brush)FindResource("BorderLineBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14),
                Child = EveningCheckPrintLayout.Page(sheets[i]),
                ToolTip = $"Сторінка {i + 1} з {sheets.Count}"
            };
            PreviewPages.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = page });
        }
        PreviewInfo.Text = $"{sheets.Count} {Services.PluralHelper.Pluralize(sheets.Count, "сторінка", "сторінки", "сторінок")} A4 альбомних - як на друці та в Excel";
    }
}
