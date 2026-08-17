using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Documents;

namespace GenDoc.ViewModels.Archive
{
    /// <summary>
    /// Гілка дерева «Архіву». Дерево — це НАВІГАТОР, а не заміна списку:
    /// вибір гілки лише додає до запиту префікс шляху, а фільтри, колонки,
    /// версії й підвантаження праворуч лишаються недоторканими.
    /// </summary>
    public partial class ArchiveFolderNodeViewModel : ObservableObject
    {
        public ArchiveFolderNodeViewModel(ArchiveFolderNode node, string? selectedPath)
        {
            Name = node.Name;
            Path = node.Path;
            DocumentCount = node.DocumentCount;

            Children = new ObservableCollection<ArchiveFolderNodeViewModel>(
                node.Children.Select(c => new ArchiveFolderNodeViewModel(c, selectedPath)));

            isSelected = selectedPath is not null && selectedPath == Path;

            // Гілка, всередині якої лежить обране, мусить бути розгорнута — інакше
            // після перезавантаження дерева вибір «зникав» би під згорнутим вузлом.
            isExpanded = selectedPath is not null
                && (selectedPath.StartsWith(Path + "\\", StringComparison.Ordinal) || isSelected);
        }

        public string Name { get; }
        public string Path { get; }
        public int DocumentCount { get; }
        public ObservableCollection<ArchiveFolderNodeViewModel> Children { get; }

        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isExpanded;

        /// <summary>Проходить усе піддерево — щоб зняти позначку зі старого
        /// вибору, не перебудовуючи дерево цілком.</summary>
        public IEnumerable<ArchiveFolderNodeViewModel> SelfAndDescendants()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var node in child.SelfAndDescendants())
                    yield return node;
        }
    }
}
