using System.IO;
using System.Text;

namespace GenDoc.Services
{
    public static class ErrorLog
    {
        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenDoc", "logs");

        public static string FilePathFor(string directory, DateTime at)
            => Path.Combine(directory, $"error-{at:yyyy-MM-dd}.log");

        public static string Format(DateTime at, string? user, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== " + at.ToString("yyyy-MM-dd HH:mm:ss") + " · користувач: " + (string.IsNullOrWhiteSpace(user) ? "-" : user));
            var current = ex;
            var depth = 0;
            while (current is not null)
            {
                var indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{current.GetType().FullName}: {current.Message}");
                if (!string.IsNullOrWhiteSpace(current.StackTrace))
                    sb.AppendLine(indent + current.StackTrace.Replace("\n", "\n" + indent));
                current = current.InnerException;
                depth++;
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static string Write(Exception ex, string? user, string? directory = null)
        {
            var dir = directory ?? DefaultDirectory;
            var now = DateTime.Now;
            var path = FilePathFor(dir, now);
            try
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(path, Format(now, user, ex), Encoding.UTF8);
            }
            catch { }
            return path;
        }
    }
}
