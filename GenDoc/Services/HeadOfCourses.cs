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

        /// <summary>Підпис «звання» + «Ім'я ПРІЗВИЩЕ» у тому вигляді, як його вписано в налаштуваннях.</summary>
        public static string DisplayLabel(OrganizationSettings org) =>
            $"{JoinRankAndName(org)} - начальник курсів";

        private static string JoinRankAndName(OrganizationSettings org) =>
            string.Join(' ', new[] { org.CommanderRank?.Trim(), org.CommanderFullName.Trim() }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
