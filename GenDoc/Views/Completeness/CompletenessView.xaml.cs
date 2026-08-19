using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Views.Completeness;

public partial class CompletenessView : UserControl
{
    // {0} - індекс колонки в MatrixRowViewModel.Cells; той самий шаблон-рядок для кожної колонки.
    private const string CellTemplateXaml = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Border x:Name="CellRoot" Tag="cell" MinHeight="34" Background="{{Binding Cells[{0}].Background}}"
                    ToolTip="{{Binding Cells[{0}].ToolTipText}}">
                <Border.Style>
                    <Style TargetType="Border">
                        <Style.Triggers>
                            <DataTrigger Binding="{{Binding Cells[{0}].IsNotApplicable}}" Value="True">
                                <Setter Property="ContextMenu" Value="{{x:Null}}"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <Border.ContextMenu>
                    <ContextMenu>
                        <MenuItem Header="Відкрити" Command="{{Binding Cells[{0}].OpenCommand}}"
                                  Visibility="{{Binding Cells[{0}].PresentMenuVisibility}}"/>
                        <MenuItem Header="Перегенерувати" Command="{{Binding Cells[{0}].RegenerateCommand}}"
                                  Visibility="{{Binding Cells[{0}].PresentMenuVisibility}}"/>
                        <MenuItem Header="Історія версій" Command="{{Binding Cells[{0}].HistoryCommand}}"
                                  Visibility="{{Binding Cells[{0}].PresentMenuVisibility}}"/>
                        <MenuItem Header="Зберегти як…" Command="{{Binding Cells[{0}].SaveAsCommand}}"
                                  Visibility="{{Binding Cells[{0}].PresentMenuVisibility}}"/>
                        <MenuItem Header="Згенерувати" Command="{{Binding Cells[{0}].GenerateCommand}}"
                                  Visibility="{{Binding Cells[{0}].GenerateMenuVisibility}}"/>
                    </ContextMenu>
                </Border.ContextMenu>
                <Grid>
                    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
                        <TextBlock Text="{{Binding Cells[{0}].Glyph}}" FontSize="14" FontWeight="Bold"
                                   HorizontalAlignment="Center" Foreground="{{Binding Cells[{0}].Foreground}}"/>
                        <TextBlock Text="{{Binding Cells[{0}].VersionText}}" FontSize="9" HorizontalAlignment="Center"
                                   Foreground="{{Binding Cells[{0}].VersionBrush}}"
                                   Visibility="{{Binding Cells[{0}].VersionVisibility}}"/>
                    </StackPanel>
                    <Rectangle Stroke="{{Binding Cells[{0}].DashedBorderBrush}}" StrokeThickness="1"
                               StrokeDashArray="2,2" Fill="Transparent" IsHitTestVisible="False"
                               Margin="1"/>
                </Grid>
            </Border>
        </DataTemplate>
        """;

    public CompletenessView()
    {
        InitializeComponent();
        MatrixGrid.PreviewMouseLeftButtonDown += MatrixGrid_PreviewMouseLeftButtonDown;
    }

    private async void CompletenessView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CompletenessViewModel vm)
        {
            vm.ColumnsChanged += () => RebuildColumns(vm);
            await vm.InitializeAsync();
            RebuildColumns(vm);
        }
    }

    // Пастка: спершу ItemsSource=null, потім Columns.Clear(), потім нові колонки - інакше
    // DataGrid кидає binding-помилки на старі Cells[i] під час перебудови.
    private void RebuildColumns(CompletenessViewModel vm)
    {
        MatrixGrid.ItemsSource = null;
        MatrixGrid.Columns.Clear();

        var checkboxColumn = new DataGridTemplateColumn
        {
            Width = 36,
            CanUserResize = false,
            HeaderTemplate = (DataTemplate)Resources["CheckBoxHeaderTemplate"],
            CellTemplate = (DataTemplate)Resources["CheckBoxCellTemplate"],
            CellStyle = (Style)Application.Current.Resources["CheckboxCellStyle"],
            HeaderStyle = (Style)Application.Current.Resources["CheckboxHeaderStyle"]
        };
        MatrixGrid.Columns.Add(checkboxColumn);

        var personColumn = new DataGridTemplateColumn
        {
            Header = "ОСОБА",
            Width = new DataGridLength(180),
            MinWidth = 160,
            CellTemplate = (DataTemplate)Resources["PersonCellTemplate"]
        };
        MatrixGrid.Columns.Add(personColumn);

        // Підписи рахуються ОДРАЗУ ДЛЯ ВСІХ колонок, бо вибір короткої назви
        // залежить від сусідів: два «Рапорт котлове …» мають показати те, чим
        // вони різняться, а не спільний початок.
        var columnLabels = Services.Completeness.MatrixColumnLabels.Build(
            vm.Columns.Select(BuildColumnHeader).ToList());

        for (var i = 0; i < vm.Columns.Count; i++)
        {
            var template = vm.Columns[i];
            // Колонка ховається повністю, якщо шаблон не потрібен ОБОМ категоріям одночасно.
            if (template.RequirementRegular == Models.Enums.TemplateRequirement.NotApplicable
                && template.RequirementLimited == Models.Enums.TemplateRequirement.NotApplicable)
                continue;

            var headerText = new TextBlock
            {
                Text = columnLabels[i],
                ToolTip = template.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };

            var xaml = string.Format(CellTemplateXaml, i);
            var cellTemplate = (DataTemplate)XamlReader.Parse(xaml);

            var column = new DataGridTemplateColumn
            {
                Header = headerText,
                Width = new DataGridLength(96),
                MinWidth = 96,
                CellTemplate = cellTemplate
            };
            MatrixGrid.Columns.Add(column);
        }

        var readinessColumn = new DataGridTemplateColumn
        {
            Header = "ГОТОВНІСТЬ",
            Width = new DataGridLength(130),
            MinWidth = 110,
            CellTemplate = (DataTemplate)Resources["ReadinessCellTemplate"]
        };
        MatrixGrid.Columns.Add(readinessColumn);

        // FrozenColumnCount виставляється ПІСЛЯ додавання колонок.
        MatrixGrid.FrozenColumnCount = 2;

        MatrixGrid.ItemsSource = vm.Rows;
    }

    // Коротка назва шаблону, якщо її задали руками, інакше повна: скорочення
    // й розведення двійників - робота MatrixColumnLabels, яка бачить усі
    // колонки одразу.
    private static string BuildColumnHeader(Services.Completeness.MatrixTemplateInfo template) =>
        string.IsNullOrWhiteSpace(template.ShortName) ? template.Name : template.ShortName;

    // Ліва кнопка теж відкриває контекстне меню клітинки (права - стандартною поведінкою WPF).
    private void MatrixGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var border = FindAncestor<Border>(e.OriginalSource as DependencyObject);
        if (border is null || border.Tag as string != "cell") return;
        if (border.ContextMenu is not ContextMenu menu) return;

        menu.PlacementTarget = border;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
