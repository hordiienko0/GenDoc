using System.Globalization;

namespace GenDoc.Services
{
    public static class UkrainianCollation
    {
        public static readonly StringComparer IgnoreCase =
            StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true);

        public static readonly StringComparer Surname = IgnoreCase;
    }
}
