using GenDoc.Models;

namespace GenDoc.Services
{
    // Канонічне впорядкування складу для звітів "на людину": старшинство звання,
    // потім прізвище/ім'я/по батькові за українською абеткою. Один метод - щоб
    // жодне місце виклику не винаходило власне сортування.
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
