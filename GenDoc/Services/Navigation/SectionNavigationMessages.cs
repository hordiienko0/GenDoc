namespace GenDoc.Services.Navigation
{
    public sealed record NavigateToSectionMessage(string SectionTitle, object? Payload);

    public sealed record IntakeNavigationPayload(int IntakeId, int RootOrgNodeId, int? PackageId);

    public sealed record ArchiveRunNavigationPayload(int RunId);

    public interface INavigationTarget
    {
        Task ApplyNavigationPayloadAsync(object payload);
    }
}
