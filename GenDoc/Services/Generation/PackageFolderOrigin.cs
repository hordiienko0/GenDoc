using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    /// <summary>
    /// Пакет, зібраний завантаженням теки, пам'ятає теку лише описом «Зібрано з теки «…»». Щойно пакет
    /// перейменовують або змінюють його склад, він уже не віддзеркалює теку - опис-прив'язку знімаємо,
    /// а власний опис користувача лишаємо.
    /// </summary>
    public static class PackageFolderOrigin
    {
        private const string Prefix = "Зібрано з теки «";

        public static string Describe(string folderPath) => $"{Prefix}{folderPath}»";

        public static bool IsFromFolder(string? description) =>
            description is not null && description.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>«Зібрано з теки «D:\…\комплекти\Відрядження»» → «Зібрано з теки «Відрядження»»; інший опис - як є.</summary>
        public static string? ShortDisplay(string? description)
        {
            if (!IsFromFolder(description) || !description!.EndsWith('»')) return description;

            var path = description[Prefix.Length..^1].TrimEnd('\\', '/');
            var cut = path.LastIndexOfAny(new[] { '\\', '/' });
            return cut < 0 ? description : Describe(path[(cut + 1)..]);
        }

        public static void Detach(GenerationPackage package)
        {
            if (IsFromFolder(package.Description)) package.Description = null;
        }
    }
}
