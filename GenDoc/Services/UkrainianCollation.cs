using System.Globalization;

namespace GenDoc.Services
{
    // InvariantGlobalization у GenDoc.csproj не увімкнено - System.Globalization працює
    // на повному ICU, тож StringComparer.Create("uk-UA") дає справжнє українське
    // впорядкування (Ґ/Є/І/Ї на своїх місцях), а не побайтове порівняння за код-пойнтами
    // UTF-16, як StringComparer.Ordinal (де ці літери потрапляють далеко не на свої місця).
    public static class UkrainianCollation
    {
        public static readonly StringComparer Surname =
            StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true);
    }
}
