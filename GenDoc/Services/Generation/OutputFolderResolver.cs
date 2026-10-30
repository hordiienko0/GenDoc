using System.IO;

namespace GenDoc.Services.Generation
{
    public static class OutputFolderResolver
    {
        public const string DefaultSubfolder = "Документи";

        private const string LocalFallbackFolder = "ДокОблік";

        public static string Resolve(string? configured)
            => Resolve(configured, LocalDefaultRoot(), CloudFolders.OneDriveRoots());

        /// <summary>
        /// Порожня тека або тека в OneDrive - пишемо в «Документи» поруч із базою. Раніше за замовчуванням
        /// брались «Мої документи», а їх Windows тихо переносить в OneDrive.
        /// </summary>
        public static string Resolve(string? configured, string defaultRoot, IReadOnlyCollection<string> oneDriveRoots)
        {
            var folder = configured?.Trim();
            return string.IsNullOrEmpty(folder) || CloudFolders.IsInOneDrive(folder, oneDriveRoots)
                ? Path.Combine(defaultRoot, DefaultSubfolder)
                : folder;
        }

        /// <summary>Тека програми й бази; якщо й вона в OneDrive - тека в профілі, куди OneDrive не дістає.</summary>
        private static string LocalDefaultRoot()
        {
            var appFolder = AppContext.BaseDirectory;
            return CloudFolders.IsInOneDrive(appFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), LocalFallbackFolder)
                : appFolder;
        }
    }
}
