namespace GenDoc.Models.Common
{
    public interface ISoftDeletable
    {
        DateTime? DeletedAt { get; set; }
        string? DeletedBy { get; set; }
    }
}
