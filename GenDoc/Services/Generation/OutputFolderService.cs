using System.IO;
using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Generation
{
    public interface IOutputFolderService
    {
        Task<string> GetDefaultAsync();
        Task SaveDefaultAsync(string folder);
        Task<string?> ResolveOnDiskAsync(string relativeFileName);
    }

    public class OutputFolderService : IOutputFolderService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        public OutputFolderService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

        public async Task<string> GetDefaultAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var configured = await db.AppSettings.Select(s => s.DefaultOutputFolder).FirstOrDefaultAsync();
            return OutputFolderResolver.Resolve(configured);
        }

        public async Task SaveDefaultAsync(string folder)
        {
            using var db = _dbFactory.CreateDbContext();
            var settings = await db.AppSettings.FirstOrDefaultAsync();
            if (settings is null)
            {
                settings = new Models.AppSettings();
                db.AppSettings.Add(settings);
            }
            settings.DefaultOutputFolder = folder.Trim();
            await db.SaveChangesAsync();
        }

        internal static async Task<string?> ConfiguredRootAsync(AppDbContext db)
        {
            var configured = await db.AppSettings.Select(s => s.DefaultOutputFolder).FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(configured) ? null : OutputFolderResolver.Resolve(configured);
        }

        internal static async Task<string> PlaceRegeneratedAsync(
            AppDbContext db, string root, Models.GeneratedDocument? previous, Models.Recipient recipient, string templateName)
        {
            var intakeName = recipient.IntakeId is int intakeId
                ? await db.Intakes.IgnoreQueryFilters()
                    .Where(i => i.Id == intakeId).Select(i => i.DisplayNumber).FirstOrDefaultAsync()
                : null;

            var sameScope = previous is not null
                            && previous.IntakeId == recipient.IntakeId
                            && previous.OrgNodeIdSnapshot == recipient.OrgNodeId;

            return Documents.DocumentFolderLayout.ForRegeneration(
                previous?.FileName, sameScope, intakeName, TemplateNaming.Clean(templateName),
                GenerationService.ResolveRunStamp(root),
                $"{recipient.LastName} {recipient.FirstName}", recipient.ServiceNumber, ".docx");
        }

        internal static async Task WriteAsync(string root, string relativeFileName, byte[] bytes)
        {
            var path = Path.Combine(root, relativeFileName);
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, bytes);
        }

        public async Task<string?> ResolveOnDiskAsync(string relativeFileName)
        {
            if (string.IsNullOrWhiteSpace(relativeFileName)) return null;
            var path = Path.Combine(await GetDefaultAsync(), relativeFileName);
            return File.Exists(path) ? path : null;
        }
    }
}
