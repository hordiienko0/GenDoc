using GenDoc.Services.Duty;

namespace GenDoc.Services.Personnel
{
    /// <summary>
    /// Аркуші книги, коли вивантаження розкладають на кілька списків: блоки ті самі, що у вечірній перевірці, -
    /// набір і прикомандировані, а прикомандировані ще й за придатністю.
    /// </summary>
    public enum PersonExportSheetKind
    {
        /// <summary>Усі слухачі: набір разом із прикомандированими.</summary>
        All,

        /// <summary>Набір без тих, хто в папках прикомандированих.</summary>
        Intake,

        /// <summary>Прикомандировані з придатністю «придатний» (або без позначки).</summary>
        SecondedFit,

        /// <summary>Прикомандировані з будь-якою іншою придатністю.</summary>
        SecondedLimited
    }

    /// <summary>Один аркуш книги: назва ярлика й люди на ньому.</summary>
    public sealed record PersonExportSheet(string Name, IReadOnlyList<PersonExportRow> Rows);

    /// <summary>
    /// Усе дерево папок (батьки перед дітьми) і всі люди, що лежать у папках.
    /// <paramref name="DefaultIntakeRootId"/> - коренева папка останнього активного набору, а як активного немає,
    /// то набору з найбільшим номером.
    /// </summary>
    public sealed record PersonExportTree(
        IReadOnlyList<DutyFolderOption> Folders, IReadOnlyList<PersonExportRow> Rows, int? DefaultIntakeRootId = null);

    public static class PersonExportSheets
    {
        /// <summary>
        /// Порядок аркушів у книзі й галочок у вікні: усі слухачі, прикомандировані (придатні, обмежено придатні),
        /// у кінці набір. Числа самого переліку лежать у збережених налаштуваннях, тому порядок задано тут,
        /// а не перестановкою значень PersonExportSheetKind.
        /// </summary>
        public static readonly IReadOnlyList<PersonExportSheetKind> Kinds = new[]
        {
            PersonExportSheetKind.All,
            PersonExportSheetKind.SecondedFit,
            PersonExportSheetKind.SecondedLimited,
            PersonExportSheetKind.Intake
        };

        /// <summary>Підпис галочки у вікні експорту.</summary>
        public static string Title(PersonExportSheetKind kind) => kind switch
        {
            PersonExportSheetKind.All => "Усі слухачі",
            PersonExportSheetKind.SecondedFit => "Придатні з прикомандированих",
            PersonExportSheetKind.SecondedLimited => "Обмежено придатні з прикомандированих",
            _ => "Набір"
        };

        /// <summary>Ярлик аркуша в Excel: не довший за 31 символ і без «: \ / ? * [ ]».</summary>
        public static string SheetName(PersonExportSheetKind kind) => kind switch
        {
            PersonExportSheetKind.All => "Усі слухачі",
            PersonExportSheetKind.SecondedFit => "Придатні (прикоманд.)",
            PersonExportSheetKind.SecondedLimited => "Обмежено придатні (прикоманд.)",
            _ => "Набір"
        };

        public static string Hint(PersonExportSheetKind kind) => kind switch
        {
            PersonExportSheetKind.All => "Набір разом із прикомандированими - усі з відмічених нижче папок",
            PersonExportSheetKind.SecondedFit => "З папок прикомандированих - ті, у кого в картці придатність «придатний» або не вказана",
            PersonExportSheetKind.SecondedLimited => "З папок прикомандированих - ті, у кого в картці придатність інша, ніж «придатний»",
            _ => "З папок набору - без тих, хто в папках прикомандированих"
        };

        /// <summary>
        /// Хто з уже відфільтрованих і впорядкованих людей іде на аркуш; порядок рядків зберігається,
        /// а нумерація на кожному аркуші своя.
        /// </summary>
        public static List<PersonExportRow> Rows(
            PersonExportSheetKind kind, IEnumerable<PersonExportRow> people, IReadOnlySet<int> secondedIds)
        {
            bool IsSeconded(PersonExportRow row) => secondedIds.Contains(row.Person.Id);
            bool IsFit(PersonExportRow row) => FitnessCategoryHelper.IsRegular(row.Person.FitnessCategory);

            return kind switch
            {
                PersonExportSheetKind.SecondedFit => people.Where(r => IsSeconded(r) && IsFit(r)).ToList(),
                PersonExportSheetKind.SecondedLimited => people.Where(r => IsSeconded(r) && !IsFit(r)).ToList(),
                PersonExportSheetKind.Intake => people.Where(r => !IsSeconded(r)).ToList(),
                _ => people.ToList()
            };
        }
    }
}
