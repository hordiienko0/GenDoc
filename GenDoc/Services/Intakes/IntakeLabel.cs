namespace GenDoc.Services.Intakes
{
    public static class IntakeLabel
    {
        public static string Of(int number, string? displayNumber)
            => string.IsNullOrWhiteSpace(displayNumber) ? $"Набір №{number}" : displayNumber.Trim();
    }
}
