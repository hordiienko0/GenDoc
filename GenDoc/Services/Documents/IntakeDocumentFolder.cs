using System.IO;
using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Documents
{
    /// <summary>Що сталося з текою документів набору після перейменування; зміни в базі ще не збережено.</summary>
    public sealed class IntakeFolderRename
    {
        private readonly List<Action> _undo = new();
        private readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase);

        internal IntakeFolderRename(string oldDirectory, string newDirectory)
        {
            OldDirectory = oldDirectory;
            NewDirectory = newDirectory;
        }

        public string OldDirectory { get; }
        public string NewDirectory { get; }

        /// <summary>Теку перейменовано цілком (разом із файлами, яких немає в базі).</summary>
        public bool FolderMoved { get; internal set; }

        public int MovedFiles { get; internal set; }
        public int UpdatedRecords { get; internal set; }

        /// <summary>Файли, що лишились у старій теці; база й далі вказує на них там.</summary>
        public IReadOnlyCollection<string> KeptFiles => _kept;

        public bool HasChanges => FolderMoved || MovedFiles > 0 || UpdatedRecords > 0;

        public string? Warning => _kept.Count == 0
            ? null
            : $"Теку з документами перенесено не повністю: {_kept.Count} "
              + $"{PluralHelper.Pluralize(_kept.Count, "файл лишився", "файли лишилися", "файлів лишилося")} "
              + $"у «{OldDirectory}» (відкриті в іншій програмі або в «{NewDirectory}» уже є файл із такою назвою). "
              + "Програма й далі знаходить їх там.";

        internal void Kept(string relativePath) => _kept.Add(relativePath);

        internal void OnUndo(Action action) => _undo.Add(action);

        /// <summary>Повертає файли й теку на місце, якщо зберегти нові шляхи в базі не вдалося.</summary>
        public void Undo()
        {
            for (var i = _undo.Count - 1; i >= 0; i--)
            {
                try { _undo[i](); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Тека набору у вихідній теці документів: «&lt;набір&gt;\&lt;шаблон&gt;\&lt;дата&gt;\…». Шляхи документів у базі
    /// відносні й починаються з неї, тож після перейменування набору переїжджають і файли, і шляхи.
    /// </summary>
    public static class IntakeDocumentFolder
    {
        /// <summary>
        /// Переносить документи набору з теки старої назви в теку нової й переписує їхні шляхи (без SaveChanges).
        /// Тека, якої не ділить інший набір, перейменовується цілком; інакше файли переносяться поодинці,
        /// а той, що не переноситься, лишається на місці зі старим шляхом.
        /// </summary>
        public static async Task<IntakeFolderRename?> RenameAsync(
            AppDbContext db, string root, int intakeId, string oldIntakeName, string newIntakeName)
        {
            var oldFolder = DocumentFolderLayout.IntakeFolder(oldIntakeName);
            var newFolder = DocumentFolderLayout.IntakeFolder(newIntakeName);
            if (string.Equals(oldFolder, newFolder, StringComparison.Ordinal)) return null;

            var docRows = (await db.GeneratedDocuments.IgnoreQueryFilters()
                    .Select(d => new { d.Id, d.FileName, d.IntakeId }).ToListAsync())
                .Where(d => InFolder(d.FileName, oldFolder)).ToList();
            var groupRows = (await db.GeneratedGroupDocuments.IgnoreQueryFilters()
                    .Select(d => new { d.Id, d.FileName, d.IntakeId }).ToListAsync())
                .Where(d => InFolder(d.FileName, oldFolder)).ToList();

            var otherIntakeNames = await db.Intakes.IgnoreQueryFilters()
                .Where(i => i.Id != intakeId).Select(i => i.DisplayNumber).ToListAsync();
            var shared = docRows.Any(d => d.IntakeId != intakeId)
                || groupRows.Any(d => d.IntakeId != intakeId)
                || otherIntakeNames.Any(n => string.Equals(
                    DocumentFolderLayout.IntakeFolder(n), oldFolder, StringComparison.OrdinalIgnoreCase));

            var docIds = docRows.Where(d => d.IntakeId == intakeId).Select(d => d.Id).ToList();
            var groupIds = groupRows.Where(d => d.IntakeId == intakeId).Select(d => d.Id).ToList();
            var docs = await db.GeneratedDocuments.IgnoreQueryFilters().Where(d => docIds.Contains(d.Id)).ToListAsync();
            var groups = await db.GeneratedGroupDocuments.IgnoreQueryFilters().Where(d => groupIds.Contains(d.Id)).ToListAsync();

            var oldDir = Path.Combine(root, oldFolder);
            var newDir = Path.Combine(root, newFolder);
            var result = new IntakeFolderRename(oldDir, newDir);

            // Лише регістр літер: на диску це та сама тека, файли знаходяться за будь-яким шляхом.
            var caseOnly = string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase);

            var wholeFolder = !shared && Directory.Exists(oldDir) && (caseOnly || !Directory.Exists(newDir))
                && TryMoveDirectory(oldDir, newDir, caseOnly);
            if (wholeFolder)
            {
                result.FolderMoved = true;
                result.OnUndo(() => TryMoveDirectory(newDir, oldDir, caseOnly));
            }

            string? Relocate(string fileName)
            {
                var rebased = newFolder + fileName[oldFolder.Length..];
                if (wholeFolder || caseOnly) return rebased;

                var from = Path.Combine(root, fileName);
                var to = Path.Combine(root, rebased);
                if (!File.Exists(from)) return rebased; // уже перенесено з іншою версією або на диску файлу немає
                if (File.Exists(to))
                {
                    result.Kept(fileName);
                    return null;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(from, to);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Kept(fileName);
                    return null;
                }

                result.MovedFiles++;
                result.OnUndo(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(from)!);
                    File.Move(to, from);
                });
                return rebased;
            }

            foreach (var doc in docs)
            {
                if (Relocate(doc.FileName) is not { } moved) continue;
                doc.FileName = moved;
                result.UpdatedRecords++;
            }
            foreach (var group in groups)
            {
                if (Relocate(group.FileName) is not { } moved) continue;
                group.FileName = moved;
                result.UpdatedRecords++;
            }

            if (!wholeFolder && !caseOnly && result.MovedFiles > 0)
                RemoveEmptyDirectories(oldDir);

            return result;
        }

        private static bool InFolder(string? fileName, string folder)
            => fileName is not null
               && fileName.Length > folder.Length + 1
               && (fileName[folder.Length] == '\\' || fileName[folder.Length] == '/')
               && fileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase);

        private static bool TryMoveDirectory(string from, string to, bool caseOnly)
        {
            try
            {
                if (!caseOnly)
                {
                    Directory.Move(from, to);
                    return true;
                }

                var temp = $"{from}.rename-{Guid.NewGuid():N}";
                Directory.Move(from, temp);
                try
                {
                    Directory.Move(temp, to);
                }
                catch
                {
                    Directory.Move(temp, from);
                    throw;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Прибирає порожні підтеки старої теки (і її саму, якщо вона спорожніла).</summary>
        private static void RemoveEmptyDirectories(string directory)
        {
            if (!Directory.Exists(directory)) return;
            try
            {
                foreach (var child in Directory.GetDirectories(directory))
                    RemoveEmptyDirectories(child);
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
