using System.IO;

namespace GenDoc.Services.Generation
{
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
