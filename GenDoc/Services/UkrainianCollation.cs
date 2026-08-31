using System.Globalization;

namespace GenDoc.Services
{
    // InvariantGlobalization у GenDoc.csproj не увімкнено - System.Globalization працює
    // на повному ICU, тож StringComparer.Create("uk-UA") дає справжнє українське
    // впорядкування (Ґ/Є/І/Ї на своїх місцях), а не побайтове порівняння за код-пойнтами
    // UTF-16, як StringComparer.Ordinal (де ці літери потрапляють далеко не на свої місця).
    public static class UkrainianCollation
    {
        // Єдина точка порівняння кирилиці без урахування регістру. SQLite для
        // цього не годиться: побайтове порівняння вважає «Корпус А» і «корпус а»
        // різними, а NOCASE знає лише латиницю. Тому такі порівняння роблять У
        // ПАМ'ЯТІ - через цей компаратор.
        public static readonly StringComparer IgnoreCase =
            StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true);

        public static readonly StringComparer Surname = IgnoreCase;
    }
}
