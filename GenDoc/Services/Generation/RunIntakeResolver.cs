namespace GenDoc.Services.Generation
{
    public static class RunIntakeResolver
    {
        public static int? Resolve(IEnumerable<int?> intakeIds)
        {
            var counts = new Dictionary<int, int>();
            foreach (var id in intakeIds)
            {
                if (id is int i) counts[i] = counts.GetValueOrDefault(i) + 1;
            }

            if (counts.Count == 0) return null;

            return counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .First().Key;
        }
    }
}
