using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class UserProfile : ISoftDeletable
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
