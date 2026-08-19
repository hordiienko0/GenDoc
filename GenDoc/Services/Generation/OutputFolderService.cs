using System.IO;
using GenDoc.Data;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Generation
{
    public interface IOutputFolderService
    {
        Task<string> GetDefaultAsync();
        Task SaveDefaultAsync(string folder);
        // Абсолютний шлях документа в типовій теці, якщо файл там є; інакше null.
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

        public async Task<string?> ResolveOnDiskAsync(string relativeFileName)
        {
            if (string.IsNullOrWhiteSpace(relativeFileName)) return null;
            var path = Path.Combine(await GetDefaultAsync(), relativeFileName);
            return File.Exists(path) ? path : null;
        }
    }
}
