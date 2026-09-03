using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Intakes
{
    public static class IntakeFitnessFolders
    {
        public static string FolderNameFor(string? fitnessCategory)
        {
            if (string.IsNullOrWhiteSpace(fitnessCategory)) return IntakeFolderNames.NoCategory;

            var value = fitnessCategory.Trim();

            if (UkrainianCollation.IgnoreCase.Equals(value, FitnessCategoryHelper.Regular))
                return IntakeFolderNames.Fit;
            if (UkrainianCollation.IgnoreCase.Equals(value, FitnessCategoryHelper.Limited))
                return IntakeFolderNames.LimitedFit;
            if (UkrainianCollation.IgnoreCase.Equals(value, FitnessCategoryHelper.Unfit))
                return IntakeFolderNames.Unfit;

            return IntakeFolderNames.NoCategory;
        }

        public static IReadOnlyList<string> AllFolderNames { get; } = new[]
        {
            IntakeFolderNames.Fit,
            IntakeFolderNames.LimitedFit,
            IntakeFolderNames.Unfit,
            IntakeFolderNames.NoCategory
        };

        public static int? Resolve(AppDbContext db, int intakeId, string? fitnessCategory)
        {
            var targetName = FolderNameFor(fitnessCategory);

            var nodes = db.OrgNodes.Where(n => n.IntakeId == intakeId).ToList();

            var existing = nodes.FirstOrDefault(n =>
                UkrainianCollation.IgnoreCase.Equals(n.Name, targetName));
            if (existing is not null) return existing.Id;

            var all = nodes.FirstOrDefault(n =>
                UkrainianCollation.IgnoreCase.Equals(n.Name, IntakeFolderNames.All));
            if (all is null) return null;

            var created = NewFolder(targetName, all, intakeId);
            db.OrgNodes.Add(created);
            db.SaveChanges();
            created.Path = $"{all.Path}{created.Id}/";
            db.SaveChanges();

            return created.Id;
        }

        public static async Task<int?> ResolveAsync(AppDbContext db, int intakeId, string? fitnessCategory)
        {
            var targetName = FolderNameFor(fitnessCategory);

            var nodes = await db.OrgNodes.Where(n => n.IntakeId == intakeId).ToListAsync();

            var existing = nodes.FirstOrDefault(n =>
                UkrainianCollation.IgnoreCase.Equals(n.Name, targetName));
            if (existing is not null) return existing.Id;

            var all = nodes.FirstOrDefault(n =>
                UkrainianCollation.IgnoreCase.Equals(n.Name, IntakeFolderNames.All));
            if (all is null) return null;

            var created = NewFolder(targetName, all, intakeId);
            db.OrgNodes.Add(created);
            await db.SaveChangesAsync();
            created.Path = $"{all.Path}{created.Id}/";
            await db.SaveChangesAsync();

            return created.Id;
        }

        private static OrgNode NewFolder(string name, OrgNode parent, int intakeId) => new()
        {
            Name = name,
            ParentId = parent.Id,
            Depth = parent.Depth + 1,
            SortOrder = Math.Max(AllFolderNames.ToList().IndexOf(name), 0),
            IntakeId = intakeId
        };
    }
}
