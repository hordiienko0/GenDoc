using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Intakes
{
    /// <summary>
    /// Куди в дереві набору лягає людина за своєю категорією придатності.
    ///
    /// Єдина точка правила: за ним створює папки майстер набору, за ним
    /// розкладає людей імпорт і за ним же людина ПЕРЕЇЖДЖАЄ, коли курсовий
    /// змінив їй категорію в картці. Три копії цього правила розійшлися б так
    /// само, як свого часу розійшлися два мапери заголовків.
    /// </summary>
    public static class IntakeFitnessFolders
    {
        /// <summary>Назва підпапки для категорії. Незаповнена й нерозпізнана
        /// категорія дають «Без категорії» - людину видно окремо, і зрозуміло,
        /// що їй бракує саме висновку, а не що вона обмежено придатна.</summary>
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

        /// <summary>Усі підпапки придатності - у порядку, в якому вони стоять
        /// під «Всі».</summary>
        public static IReadOnlyList<string> AllFolderNames { get; } = new[]
        {
            IntakeFolderNames.Fit,
            IntakeFolderNames.LimitedFit,
            IntakeFolderNames.Unfit,
            IntakeFolderNames.NoCategory
        };

        /// <summary>Id папки для категорії; за відсутності папка ДОПИСУЄТЬСЯ.
        ///
        /// Дописування, а не разова міграція: набори, створені до появи цих
        /// папок, інакше лишилися б без них назавжди, і розкладання за статусом
        /// у них не працювало б. null - у наборі немає навіть папки «Всі»,
        /// тобто дерево пошкоджене; тоді краще нічого не чіпати, ніж розкидати
        /// людей по корені.</summary>
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

        /// <summary>Те саме асинхронно - для сервісів, що працюють на await.</summary>
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
            // Порядок сталий - за переліком категорій, а не за часом дописування:
            // інакше папка, дописана пізніше, стрибала б у кінець списку.
            SortOrder = Math.Max(AllFolderNames.ToList().IndexOf(name), 0),
            IntakeId = intakeId
        };
    }
}
