namespace GenDoc.Services;

public static class PluralHelper
{
    public static string Pluralize(int count, string one, string few, string many)
    {
        var n = Math.Abs(count) % 100;
        var n1 = n % 10;

        if (n is >= 11 and <= 14) return many;
        if (n1 == 1) return one;
        if (n1 is >= 2 and <= 4) return few;
        return many;
    }
}
