using GenDoc.Models;

namespace GenDoc.Services
{
    /// <summary>
    /// Начальник курсів - головний підписант документів. Береться з налаштувань (поля «командира»),
    /// а не з постійного складу, тож у списках підписантів і курсових офіцерів він стоїть окремим
    /// пунктом з <see cref="PickerId"/> замість ідентифікатора особи.
    /// </summary>
    public static class HeadOfCourses
    {
        /// <summary>Ідентифікатор пункту «начальник курсів» у пікерах: справжні Id осіб у SQLite починаються з 1.</summary>
        public const int PickerId = 0;

        private const string DefaultPosition = "Начальник курсів";

        public static bool IsConfigured(OrganizationSettings? org) =>
            !string.IsNullOrWhiteSpace(org?.CommanderFullName);

        /// <summary>Підпис у відомості: «Начальник курсів полковник Іван ПЕТРЕНКО».</summary>
        public static string? Signature(OrganizationSettings? org)
        {
            if (!IsConfigured(org)) return null;

            var position = string.IsNullOrWhiteSpace(org!.CommanderPosition) ? DefaultPosition : org.CommanderPosition.Trim();
            var parts = new[] { position, org.CommanderRank?.Trim(), org.CommanderFullName.Trim() };
            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>Повна посада над підписом, коли в налаштуваннях її не вписано.</summary>
        public const string DefaultFormalPosition =
            "Начальник курсів перепідготовки та підвищення кваліфікації Харківського національного університету Повітряних Сил";

        private const string HeadPrefix = "Начальник курсів";
        private const string OfficerPrefix = "Курсовий офіцер курсів";

        /// <summary>{{посада_підписанта}} для начальника курсів: посада з налаштувань або <see cref="DefaultFormalPosition"/>.</summary>
        public static string FormalPosition(OrganizationSettings? org) =>
            string.IsNullOrWhiteSpace(org?.CommanderPosition) ? DefaultFormalPosition : org!.CommanderPosition.Trim();

        /// <summary>
        /// {{посада_підписанта}} для курсового офіцера - з тієї самої посади начальника: «Начальник курсів
        /// перепідготовки…» → «Курсовий офіцер курсів перепідготовки…». Так посада над підписом завжди
        /// відповідає вибраному підписантові, а не зашита в шаблоні.
        /// </summary>
        public static string CourseOfficerPosition(OrganizationSettings? org)
        {
            var head = FormalPosition(org);
            if (!head.StartsWith(HeadPrefix, StringComparison.OrdinalIgnoreCase))
                head = DefaultFormalPosition;
            return OfficerPrefix + head[HeadPrefix.Length..];
        }

        /// <summary>Підпис «звання» + «Ім'я ПРІЗВИЩЕ» у тому вигляді, як його вписано в налаштуваннях.</summary>
        public static string DisplayLabel(OrganizationSettings org) =>
            $"{JoinRankAndName(org)} - начальник курсів";

        private static string JoinRankAndName(OrganizationSettings org) =>
            string.Join(' ', new[] { org.CommanderRank?.Trim(), org.CommanderFullName.Trim() }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
