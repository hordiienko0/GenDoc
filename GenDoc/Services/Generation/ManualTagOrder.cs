namespace GenDoc.Services.Generation
{
    /// <summary>
    /// Порядок полів «Значення для цієї генерації»: за текстом тега, але числа - як числа
    /// («набої_вправа_2» перед «набої_вправа_10»). Сортування за сирим тегом «{{…}}» ставило
    /// «_10}}»…«_16}}» перед «_1}}»…«_9}}», бо «0» менше за «}».
    /// </summary>
    public sealed class ManualTagOrder : IComparer<string>
    {
        public static readonly ManualTagOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = ManualTagClassifier.Normalize(x ?? string.Empty);
            var b = ManualTagClassifier.Normalize(y ?? string.Empty);

            var i = 0;
            var j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    var startA = i;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    var startB = j;
                    while (j < b.Length && char.IsDigit(b[j])) j++;

                    var numberA = a[startA..i].TrimStart('0');
                    var numberB = b[startB..j].TrimStart('0');
                    var byNumber = numberA.Length != numberB.Length
                        ? numberA.Length.CompareTo(numberB.Length)
                        : string.CompareOrdinal(numberA, numberB);
                    if (byNumber != 0) return byNumber;
                    continue;
                }

                var byChar = a[i].CompareTo(b[j]);
                if (byChar != 0) return byChar;
                i++;
                j++;
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
