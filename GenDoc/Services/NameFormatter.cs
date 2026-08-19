namespace GenDoc.Services;

// Єдина точка форматування ПІБ - щоб не дублювати логіку "ініціал лише якщо
// поле непорожнє і починається літерою" у кожній ViewModel/сервісі.
public static class NameFormatter
{
    public static string FullName(string lastName, string? firstName, string? middleName)
        => string.Join(' ', new[] { lastName, firstName, middleName }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string ShortName(string lastName, string? firstName, string? middleName)
    {
        var parts = new List<string> { lastName };

        if (!string.IsNullOrEmpty(firstName) && char.IsLetter(firstName[0]))
            parts.Add(firstName[0] + ".");

        if (!string.IsNullOrEmpty(middleName) && char.IsLetter(middleName[0]))
            parts.Add(middleName[0] + ".");

        return string.Join(' ', parts);
    }

    // «Ім'я ПРІЗВИЩЕ» - формат підпису (не FullNameFormatted: без по батькові,
    // прізвище другим, а не першим).
    public static string SignatureName(string lastName, string firstName)
        => $"{firstName} {lastName.ToUpper(new System.Globalization.CultureInfo("uk-UA"))}";
}
