using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenDoc.Models
{
    public class AuditLogEntry
    {
        public int Id { get; set; }
        public DateTime OccurredAt { get; set; }

        public int? UserProfileId { get; set; }
        public UserProfile? UserProfile { get; set; }
        public string UserProfileName { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public int? EntityId { get; set; }

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Details { get; set; }
    }
}
