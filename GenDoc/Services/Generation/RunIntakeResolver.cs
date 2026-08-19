namespace GenDoc.Services.Generation
{
    // Набір, до якого належить прогін: виводиться з людей (або документів), яким
    // реально генерували, - найчастіший не-null IntakeId. Прогін «лише постійний
    // склад» дає null. Те саме правило використовує бекфіл старих запусків.
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
