using System.IO;

namespace GenDoc.Services.Generation
{
    // Тека, куди лягають документи, якщо оператор не обрав іншої: налаштування або
    // «Документи\GenDoc». Чиста функція - перевантаження з явним коренем для тестів.
    public static class OutputFolderResolver
    {
        public const string DefaultSubfolder = "GenDoc";

        public static string Resolve(string? configured)
            => Resolve(configured, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        public static string Resolve(string? configured, string documentsRoot)
            => string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(documentsRoot, DefaultSubfolder)
                : configured.Trim();
    }
}
