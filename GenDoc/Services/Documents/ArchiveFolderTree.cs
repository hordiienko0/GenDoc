using System.Globalization;

namespace GenDoc.Services.Documents
{
    /// <summary>Вузол дерева «Архіву». Path — повний шлях від кореня, саме за
    /// ним список фільтрується; без нього дві однойменні гілки в різних наборах
    /// показували б документи одна одної.</summary>
    public sealed record ArchiveFolderNode(
        string Name,
        string Path,
        int DocumentCount,
        IReadOnlyList<ArchiveFolderNode> Children);

    /// <summary>
    /// Дерево папок «Архіву», зібране РОЗБОРОМ збережених відносних шляхів.
    ///
    /// Шлях у полі FileName — уже результат <see cref="DocumentFolderLayout"/>:
    /// рівно те, що записали на диск. Читаючи його, екран фізично не може
    /// розійтися з тим, що лежить у папках. Другий, незалежний розрахунок із
    /// полів документа з часом розійшовся б — у цьому проєкті так уже двічі
    /// ставалося (мапи тегів; розкладка аркуша, звідки й узявся
    /// TemplateSheetLayout).
    ///
    /// Функція чиста: жодної БД, лише перелік шляхів.
    /// </summary>
    public static class ArchiveFolderTree
    {
        /// <summary>Куди лягають записи, зроблені ДО переходу на папки: у них
        /// збережене саме лише ім'я файлу. Вигадувати їм гілку заднім числом
        /// означало б показати те, чого на диску немає.</summary>
        public const string UnsortedFolder = "Без розкладки";

        private static readonly char[] Separators = { '\\', '/' };

        // Кирилиця й SQLite NOCASE не дружать, тож упорядковуємо в пам'яті
        // українським порівнянням — інакше «Ї» опинялося б після латиниці.
        private static readonly CompareInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;

        public static IReadOnlyList<ArchiveFolderNode> Build(IEnumerable<string?> relativePaths)
        {
            var root = new Builder(string.Empty, string.Empty);

            foreach (var path in relativePaths)
            {
                var folders = FoldersOf(path);
                root.Add(folders, 0);
            }

            return root.ToNodes();
        }

        /// <summary>Гілки шляху БЕЗ імені файлу. Останній сегмент — це файл, а
        /// не папка; шлях без роздільника означає запис, зроблений до переходу
        /// на папки.</summary>
        private static IReadOnlyList<string> FoldersOf(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return new[] { UnsortedFolder };

            var parts = relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length <= 1
                ? new[] { UnsortedFolder }
                : parts[..^1];
        }

        private sealed class Builder
        {
            private readonly Dictionary<string, Builder> _children = new(StringComparer.Ordinal);

            public Builder(string name, string path)
            {
                Name = name;
                Path = path;
            }

            private string Name { get; }
            private string Path { get; }
            private int Count { get; set; }

            public void Add(IReadOnlyList<string> folders, int depth)
            {
                // Лічильник росте на КОЖНОМУ рівні вздовж шляху: вузол показує,
                // скільки документів лежить під ним, а не лише в ньому самому,
                // інакше згорнута гілка виглядала б порожньою.
                Count++;

                if (depth >= folders.Count) return;

                var name = folders[depth];
                if (!_children.TryGetValue(name, out var child))
                {
                    var childPath = Path.Length == 0 ? name : $"{Path}\\{name}";
                    child = new Builder(name, childPath);
                    _children[name] = child;
                }

                child.Add(folders, depth + 1);
            }

            public IReadOnlyList<ArchiveFolderNode> ToNodes() => _children.Values
                .OrderBy(c => c.Name, Comparer<string>.Create((a, b) => Ukrainian.Compare(a, b, CompareOptions.None)))
                .Select(c => new ArchiveFolderNode(c.Name, c.Path, c.Count, c.ToNodes()))
                .ToList();
        }
    }
}
