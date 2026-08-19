using System.Diagnostics;
using System.IO;

namespace GenDoc.Services.Documents
{
    public interface ISecureTempFileService
    {
        // Пише байти в ./_temp/{guid}/{fileName} з ReadOnly і відкриває асоційованою програмою.
        // Win32Exception (нема асоціації) прокидається до викликача.
        Task OpenAsync(string fileName, byte[] content);
        // Те саме, але shell-verb "print": друкує Word/Excel, як вони ж і відкривають (2.5).
        Task PrintAsync(string fileName, byte[] content);
        Task CleanupAsync();
    }

    public class SecureTempFileService : ISecureTempFileService
    {
        private static string TempRoot => Path.Combine(AppContext.BaseDirectory, "_temp");

        public async Task OpenAsync(string fileName, byte[] content)
        {
            var path = await WriteTempAsync(fileName, content);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        public async Task PrintAsync(string fileName, byte[] content)
        {
            var path = await WriteTempAsync(fileName, content);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "print" });
        }

        private static async Task<string> WriteTempAsync(string fileName, byte[] content)
        {
            var dir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, SanitizeFileName(fileName));

            await File.WriteAllBytesAsync(path, content);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            return path;
        }

        public Task CleanupAsync() => Task.Run(() =>
        {
            if (!Directory.Exists(TempRoot)) return;

            foreach (var file in Directory.EnumerateFiles(TempRoot, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            try { Directory.Delete(TempRoot, recursive: true); }
            catch
            {
                // Відкритий файл лишається до наступного очищення.
            }
        });

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return cleaned.Length == 0 ? "документ" : cleaned;
        }
    }
}
