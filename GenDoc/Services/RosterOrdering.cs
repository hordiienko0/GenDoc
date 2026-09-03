using GenDoc.Models;

namespace GenDoc.Services
{
    public static class RosterOrdering
    {
        public static IOrderedEnumerable<Recipient> Apply(IEnumerable<Recipient> recipients)
            => recipients
                .OrderBy(r => RankOrder.Seniority(r.Rank))
                .ThenBy(r => r.LastName, UkrainianCollation.Surname)
                .ThenBy(r => r.FirstName, UkrainianCollation.Surname)
                .ThenBy(r => r.MiddleName, UkrainianCollation.Surname);
    }
}
