using GenDoc.Models.Enums;

namespace GenDoc.Services.Intakes
{
    public record IntakeOverview(
        int Id, int Number, string DisplayNumber, IntakeStatus Status,
        DateOnly DateStart, DateOnly DateEnd, DateOnly? DateClosed,
        int RootOrgNodeId, int PeopleCount, int CompletenessPercent, int IncompletePeopleCount, int? PackageId)
    {
        public bool HasPackage => PackageId is not null;
    }

    public record IntakeCloseInfo(
        int IntakeId, string DisplayNumber, int PeopleCount, int IncompletePeopleCount,
        int OccupiedRoomCount, IReadOnlyList<(int NodeId, string Name)> GraduateTargets, int? PackageId = null);

    public record IntakeCloseRequest(int IntakeId, bool ReleaseRooms, bool MovePersonnel, int? TargetNodeId);

    public record IntakeYearOption(int? Year, string Label);
}
