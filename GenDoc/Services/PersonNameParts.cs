namespace GenDoc.Services
{
    /// <summary>Прізвище, ім'я та по батькові, розібрані з одного рядка.</summary>
    public record PersonNameParts(string LastName, string FirstName, string? MiddleName)
    {
        /// <summary>Рядок дав лише одне слово - по ньому не можна сказати, чи це
        /// прізвище, чи ім'я. Імпорт позначає такі рядки як неповні.</summary>
        public bool IsIncomplete => FirstName.Length == 0;
    }

    /// <summary>
    /// Розбір «ПІБ одним рядком» на частини: перше слово - прізвище, друге -
    /// ім'я, решта - по батькові.
    ///
    /// Спільне для імпорту з Excel і для картки постійного складу, яка
    /// заводиться разом із профілем: два розбори того самого рядка неминуче
    /// розійшлися б у дрібницях, а це прізвище в наказі.
    /// </summary>
    public static class FullNameParser
    {
        public static PersonNameParts Split(string? fullName)
        {
            var parts = (fullName ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length switch
            {
                0 => new PersonNameParts(string.Empty, string.Empty, null),
                1 => new PersonNameParts(parts[0], string.Empty, null),
                2 => new PersonNameParts(parts[0], parts[1], null),
                _ => new PersonNameParts(parts[0], parts[1], string.Join(' ', parts[2..]))
            };
        }
    }
}
