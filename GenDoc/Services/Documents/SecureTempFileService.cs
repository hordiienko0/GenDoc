using System.Diagnostics;
using System.IO;

namespace GenDoc.Services.Documents
{
    public interface ISecureTempFileService
    {
        Task OpenAsync(string fileName, byte[] content);
        Task PrintAsync(string fileName, byte[] content);
        Task CleanupAsync();
    }

    public class SecureTempFileService : ISecureTempFileService
    {
        internal static string TempRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GenDoc", "_temp");

        internal static string LegacyTempRoot => Path.Combine(AppContext.BaseDirectory, "_temp");

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
            CleanupFolder(TempRoot);
            CleanupFolder(LegacyTempRoot);
        });

        private static void CleanupFolder(string root)
        {
            if (!Directory.Exists(root)) return;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            try { Directory.Delete(root, recursive: true); }
            catch
            {
            }
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return cleaned.Length == 0 ? "документ" : cleaned;
        }
    }
}
