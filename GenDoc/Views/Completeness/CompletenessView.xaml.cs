using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Views.Completeness;

public partial class CompletenessView : UserControl
{
    private const string CellTemplateXaml = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Border x:Name="CellRoot" Tag="cell" MinHeight="34" Background="{{Binding Cells[{0}].Background}}"
                    ToolTip="{{Binding Cells[{0}].ToolTipText}}">
                <!-- ContextMenu ЧЕРЕЗ Setter, не локальним значенням: локальне значення
                     старше за тригер стилю, і "вимкнути меню" тригером було б неможливо
                     (пастка 17.08). Тригер прибирає меню там, де жоден пункт не показався б. -->
                <Border.Style>
                    <Style TargetType="Border">
                        <Setter Property="ContextMenu">
                            <Setter.Value>
                                <!-- Стилі задані ЯВНО. Без них меню бере НЕЯВНИЙ
                                     Style TargetType="MenuItem" з теми, писаний для
                                     темної смуги меню (Foreground = MenuTextBrush,
                                     #D0E0F2), і на білому тлі всі пункти виглядають
                                     блідо-сірими - «ніби недоступні», хоча
                                     працюють (побачено живим прогоном 2026-08-31).
                                     DynamicResource, а не StaticResource: шаблон
                                     розбирає XamlReader без контексту ресурсів. -->
                                <ContextMenu Style="{{DynamicResource AppContextMenuStyle}}">
                                    <MenuItem Header="Відкрити" Command="{{Binding Cells[{0}].OpenCommand}}"
                                              Style="{{DynamicResource AppContextMenuItemStyle}}"
                                              Visibility="{{Binding Cells[{0}].OpenMenuVisibility}}"/>
                                    <MenuItem Header="Перегенерувати" Command="{{Binding Cells[{0}].RegenerateCommand}}"
                                              Style="{{DynamicResource AppContextMenuItemStyle}}"
                                              Visibility="{{Binding Cells[{0}].RegenerateMenuVisibility}}"/>
                                    <MenuItem Header="Історія версій" Command="{{Binding Cells[{0}].HistoryCommand}}"
                                              Style="{{DynamicResource AppContextMenuItemStyle}}"
                                              Visibility="{{Binding Cells[{0}].HistoryMenuVisibility}}"/>
                                    <MenuItem Header="Зберегти як…" Command="{{Binding Cells[{0}].SaveAsCommand}}"
                                              Style="{{DynamicResource AppContextMenuItemStyle}}"
                                              Visibility="{{Binding Cells[{0}].SaveAsMenuVisibility}}"/>
                                    <MenuItem Header="Згенерувати" Command="{{Binding Cells[{0}].GenerateCommand}}"
                                              Style="{{DynamicResource AppContextMenuItemStyle}}"
                                              Visibility="{{Binding Cells[{0}].GenerateMenuVisibility}}"/>
                                </ContextMenu>
                            </Setter.Value>
                        </Setter>
                        <Style.Triggers>
                            <DataTrigger Binding="{{Binding Cells[{0}].HasMenu}}" Value="False">
                                <Setter Property="ContextMenu" Value="{{x:Null}}"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
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

    private Action? _columnsChangedHandler;

    public CompletenessView()
    {
        InitializeComponent();
        MatrixGrid.PreviewMouseLeftButtonDown += MatrixGrid_PreviewMouseLeftButtonDown;
        Unloaded += CompletenessView_Unloaded;
    }

    private async void CompletenessView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CompletenessViewModel vm) return;

        DetachColumnsChanged();
        _columnsChangedHandler = () => RebuildColumns(vm);
        vm.ColumnsChanged += _columnsChangedHandler;

        try
        {
            await vm.InitializeAsync();
            RebuildColumns(vm);
        }
        catch (Exception ex)
        {
            GenDoc.Services.ErrorLog.Write(ex, null);
            vm.IsBusy = false;
            vm.FooterText = $"Не вдалося завантажити комплектність: {ex.Message}";
        }
    }

    private void CompletenessView_Unloaded(object sender, RoutedEventArgs e) => DetachColumnsChanged();

    private void DetachColumnsChanged()
    {
        if (_columnsChangedHandler is null) return;
        if (DataContext is CompletenessViewModel vm) vm.ColumnsChanged -= _columnsChangedHandler;
        _columnsChangedHandler = null;
    }

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

        var columnLabels = Services.Completeness.MatrixColumnLabels.Build(
            vm.Columns.Select(BuildColumnHeader).ToList());

        for (var i = 0; i < vm.Columns.Count; i++)
        {
            var template = vm.Columns[i];
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

        MatrixGrid.FrozenColumnCount = 2;

        MatrixGrid.ItemsSource = vm.Rows;
    }

    private static string BuildColumnHeader(Services.Completeness.MatrixTemplateInfo template) =>
        string.IsNullOrWhiteSpace(template.ShortName) ? template.Name : template.ShortName;

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
