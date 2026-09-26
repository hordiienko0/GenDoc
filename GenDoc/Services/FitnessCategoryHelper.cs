using GenDoc.Models.Enums;

namespace GenDoc.Services
{
    public static class FitnessCategoryHelper
    {
        public const string Regular = "придатний";
        public const string Limited = "обмежено придатний";

        public static bool IsRegular(string? fitnessCategory)
            => string.IsNullOrWhiteSpace(fitnessCategory)
                || string.Equals(fitnessCategory, Regular, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Варіант шаблона за назвою: «… (придатні)» - лише придатним, «… (обмежено придатні)» - лише
        /// обмежено придатним, решта - усім.
        /// </summary>
        public static FitnessFilter FilterFromTemplateName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return FitnessFilter.All;
            if (name.Contains("(обмежено придатні)", StringComparison.OrdinalIgnoreCase)) return FitnessFilter.LimitedOnly;
            if (name.Contains("(придатні)", StringComparison.OrdinalIgnoreCase)) return FitnessFilter.RegularOnly;
            return FitnessFilter.All;
        }

        public static bool Matches(FitnessFilter filter, string? fitnessCategory) => filter switch
        {
            FitnessFilter.All => true,
            FitnessFilter.RegularOnly => IsRegular(fitnessCategory),
            FitnessFilter.LimitedOnly => !IsRegular(fitnessCategory),
            _ => true
        };
    }
}
