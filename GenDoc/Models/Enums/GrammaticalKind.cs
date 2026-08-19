namespace GenDoc.Models.Enums
{
    // Тип слова, що відмінюється - правила закінчень різняться для прізвища,
    // імені, по батькові та звання.
    public enum GrammaticalKind
    {
        Surname,
        GivenName,
        Patronymic,
        Rank
    }
}
