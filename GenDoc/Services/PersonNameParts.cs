namespace GenDoc.Services
{
    public record PersonNameParts(string LastName, string FirstName, string? MiddleName)
    {
        public bool IsIncomplete => FirstName.Length == 0;
    }

    public static class FullNameParser
    {
        public static PersonNameParts Split(string? fullName)
        {
            var parts = (fullName ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length switch
            {
                0 => new PersonNameParts(string.Empty, string.Empty, null),
                1 => new PersonNameParts(parts[0], string.Empty, null),
                2 => new PersonNameParts(parts[0], parts[1], null),
                _ => new PersonNameParts(parts[0], parts[1], string.Join(' ', parts[2..]))
            };
        }
    }
}
