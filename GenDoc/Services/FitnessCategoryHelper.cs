using GenDoc.Models.Enums;

namespace GenDoc.Services
{
    public static class FitnessCategoryHelper
    {
        public const string Regular = "придатний";
        public const string Limited = "обмежено придатний";
        public const string Unfit = "непридатний";

        public static bool IsRegular(string? fitnessCategory)
            => string.IsNullOrWhiteSpace(fitnessCategory)
                || string.Equals(fitnessCategory, Regular, StringComparison.OrdinalIgnoreCase);

        public static bool Matches(FitnessFilter filter, string? fitnessCategory) => filter switch
        {
            FitnessFilter.All => true,
            FitnessFilter.RegularOnly => IsRegular(fitnessCategory),
            FitnessFilter.LimitedOnly => !IsRegular(fitnessCategory),
            _ => true
        };
    }
}
