namespace GenDoc.Services.Navigation
{
    // Розділ навігації + payload для переходу з "глибоким посиланням" (напр. з картки набору).
    public sealed record NavigateToSectionMessage(string SectionTitle, object? Payload);

    public sealed record IntakeNavigationPayload(int IntakeId, int RootOrgNodeId, int? PackageId);

    public interface INavigationTarget
    {
        Task ApplyNavigationPayloadAsync(object payload);
    }
}
