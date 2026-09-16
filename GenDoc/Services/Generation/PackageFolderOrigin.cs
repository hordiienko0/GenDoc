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

        public static void Detach(GenerationPackage package)
        {
            if (IsFromFolder(package.Description)) package.Description = null;
        }
    }
}
