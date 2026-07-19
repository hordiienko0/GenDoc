using System.IO;

namespace GenDoc.Data
{
    public static class DbPaths
    {
        public const string DatabaseFileName = "gendoc.db";

        public static string DatabasePath => Path.Combine(AppContext.BaseDirectory, DatabaseFileName);
    }
}
