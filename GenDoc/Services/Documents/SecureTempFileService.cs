using System.Diagnostics;
using System.IO;

namespace GenDoc.Services.Documents
{
    public interface ISecureTempFileService
    {
        Task OpenAsync(string fileName, byte[] content);
        Task<bool> PrintAsync(string fileName, byte[] content);
        Task CleanupAsync();
    }

    public class SecureTempFileService : ISecureTempFileService
    {
        private static readonly TimeSpan PrintWait = TimeSpan.FromSeconds(20);

        internal static string TempRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GenDoc", "_temp");

        internal static string LegacyTempRoot => Path.Combine(AppContext.BaseDirectory, "_temp");

        public async Task OpenAsync(string fileName, byte[] content)
        {
            var path = await WriteTempAsync(fileName, content);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        public async Task<bool> PrintAsync(string fileName, byte[] content)
        {
            var path = await WriteTempAsync(fileName, content);
            using var process = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "print" });
            if (process is null) return false;

            try
            {
                using var timeout = new CancellationTokenSource(PrintWait);
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return true;
        }

        private static async Task<string> WriteTempAsync(string fileName, byte[] content)
        {
            var dir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, TempFileName(fileName));

            await File.WriteAllBytesAsync(path, content);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            return path;
        }

        public static string TempFileName(string fileName)
        {
            var leaf = fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return SanitizeFileName(Path.GetFileName(leaf));
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
