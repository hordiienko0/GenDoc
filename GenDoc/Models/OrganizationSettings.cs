namespace GenDoc.Models
{
    public class OrganizationSettings
    {
        public int Id { get; set; }

        public string UnitNumber { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string CommanderRank { get; set; } = string.Empty;
        public string CommanderFullName { get; set; } = string.Empty;
    }
}
