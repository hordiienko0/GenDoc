using System.IO;
using GenDoc.Data;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Templates
{
    /// <summary>
    /// Що зібрано з однієї теки: один комплект — один пакет генерації.
    /// <paramref name="TemplateCount"/> — групові шаблони, що потрапили в пакет;
    /// <paramref name="PerRecipientCount"/> — персональні: завантажені, але в пакет не йдуть.
    /// </summary>
    public record FolderImportPackage(
        string Name, int TemplateCount, int ExportTemplateCount, int ReusedCount, int PerRecipientCount = 0);

    public record FolderImportResult(
        bool Success,
        string? ErrorMessage,
        IReadOnlyList<FolderImportPackage> Packages,
        IReadOnlyList<string> Failures);

    public interface ITemplateFolderImportService
    {
        FolderImportResult ImportFolder(string folderPath);
    }

    /// <summary>
    /// Завантажує шаблони з теки й одразу збирає з них пакет генерації.
    /// Підтека = окремий пакет; файли, що лежать просто в обраній теці, — пакет з її назвою.
    /// Шаблон, який уже завантажений (за назвою файлу), не дублюється — у пакет іде наявний.
    /// </summary>
    public class TemplateFolderImportService : ITemplateFolderImportService
    {
        private static readonly string[] DocxExtensions = { ".docx" };
        private static readonly string[] XlsxExtensions = { ".xlsx" };

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ITemplateService _templateService;
        private readonly IExportTemplateService _exportTemplateService;
        private readonly IGenerationService _generationService;

        public TemplateFolderImportService(
            IDbContextFactory<AppDbContext> dbFactory,
            ITemplateService templateService,
            IExportTemplateService exportTemplateService,
            IGenerationService generationService)
        {
            _dbFactory = dbFactory;
            _templateService = templateService;
            _exportTemplateService = exportTemplateService;
            _generationService = generationService;
        }

        public FolderImportResult ImportFolder(string folderPath)
        {
            var empty = Array.Empty<FolderImportPackage>();
            var noFailures = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return new FolderImportResult(false, $"Теку не знайдено: {folderPath}", empty, noFailures);

            var groups = CollectGroups(folderPath);
            if (groups.Count == 0)
            {
                return new FolderImportResult(false,
                    "У теці немає шаблонів .docx або .xlsx - ні в ній самій, ні в її підтеках.",
                    empty, noFailures);
            }

            var packages = new List<FolderImportPackage>();
            var failures = new List<string>();

            foreach (var group in groups)
            {
                var docxIds = new List<int>();
                var exportTemplateIds = new List<int>();
                var reused = 0;

                foreach (var file in group.Files)
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (DocxExtensions.Contains(extension))
                        Take(file, FindExistingTemplateId, _templateService.Upload, docxIds, ref reused, failures);
                    else if (XlsxExtensions.Contains(extension))
                        Take(file, FindExistingExportTemplateId, _exportTemplateService.UploadTemplate,
                             exportTemplateIds, ref reused, failures);
                }

                if (docxIds.Count == 0 && exportTemplateIds.Count == 0) continue;

                // Пакет складають лише групові шаблони; персональні лишаються завантаженими,
                // їх видають вибраній людині за потреби.
                var groupIds = GroupTemplateIds(docxIds);
                var perRecipientCount = docxIds.Count - groupIds.Count;

                if (groupIds.Count == 0 && exportTemplateIds.Count == 0)
                {
                    packages.Add(new FolderImportPackage(group.Name, 0, 0, reused, perRecipientCount));
                    continue;
                }

                var name = UniquePackageName(group.Name);
                _generationService.CreatePackage(
                    name,
                    $"Зібрано з теки «{group.FolderPath}»",
                    groupIds,
                    exportTemplateIds.Select(id => (id, FitnessFilter.All)).ToList());

                packages.Add(new FolderImportPackage(
                    name, groupIds.Count, exportTemplateIds.Count, reused, perRecipientCount));
            }

            if (packages.Count == 0)
            {
                return new FolderImportResult(false,
                    "Жодного шаблона не вдалося завантажити.", empty, failures);
            }

            return new FolderImportResult(true, null, packages, failures);
        }

        private void Take(
            string file,
            Func<string, int?> findExisting,
            Func<string, UploadResult> upload,
            List<int> target,
            ref int reused,
            List<string> failures)
        {
            var fileName = Path.GetFileName(file);

            if (findExisting(fileName) is int existingId)
            {
                if (!target.Contains(existingId)) target.Add(existingId);
                reused++;
                return;
            }

            var result = upload(file);
            if (!result.Success)
            {
                failures.Add($"{fileName}: {result.ErrorMessage}");
                return;
            }

            target.Add(result.TemplateId);
        }

        private List<int> GroupTemplateIds(IReadOnlyList<int> templateIds)
        {
            using var db = _dbFactory.CreateDbContext();
            var groupIds = db.Templates
                .Where(t => templateIds.Contains(t.Id) && t.Kind == TemplateKind.Group)
                .Select(t => t.Id)
                .ToHashSet();

            return templateIds.Where(groupIds.Contains).ToList();
        }

        private int? FindExistingTemplateId(string fileName)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Templates
                .AsEnumerable()
                .FirstOrDefault(t => string.Equals(t.OriginalFileName, fileName, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        private int? FindExistingExportTemplateId(string fileName)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplates
                .AsEnumerable()
                .FirstOrDefault(t => string.Equals(t.OriginalFileName, fileName, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        private string UniquePackageName(string name)
        {
            using var db = _dbFactory.CreateDbContext();
            var taken = db.GenerationPackages.Select(p => p.Name).AsEnumerable().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(name)) return name;

            for (var i = 2; ; i++)
            {
                var candidate = $"{name} ({i})";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        private sealed record Group(string Name, string FolderPath, IReadOnlyList<string> Files);

        /// <summary>Файли просто в теці - один пакет; кожна підтека (з усім, що в ній глибше) - ще один.</summary>
        private static List<Group> CollectGroups(string folderPath)
        {
            var groups = new List<Group>();

            var rootFiles = TemplateFilesIn(folderPath, SearchOption.TopDirectoryOnly);
            if (rootFiles.Count > 0)
                groups.Add(new Group(Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)), folderPath, rootFiles));

            foreach (var sub in Directory.GetDirectories(folderPath).OrderBy(d => d, StringComparer.CurrentCulture))
            {
                var files = TemplateFilesIn(sub, SearchOption.AllDirectories);
                if (files.Count > 0)
                    groups.Add(new Group(Path.GetFileName(sub), sub, files));
            }

            return groups;
        }

        private static List<string> TemplateFilesIn(string folder, SearchOption option) =>
            Directory.GetFiles(folder, "*.*", option)
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .Where(f => DocxExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())
                            || XlsxExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.CurrentCulture)
                .ToList();
    }
}
