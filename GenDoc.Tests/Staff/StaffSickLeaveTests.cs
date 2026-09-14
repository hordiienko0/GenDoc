using GenDoc.Models.Enums;
using GenDoc.ViewModels.Staff;

namespace GenDoc.Tests.Staff;

/// <summary>
/// Хвороба — третя подія постійного складу поряд із відрядженням і відпусткою:
/// рапорт про відпустку для лікування й про повернення до обов'язків оформлюються так само.
/// </summary>
public class StaffSickLeaveTests
{
    [Fact]
    public void SickEvent_HasItsOwnStateText()
    {
        var row = new StaffRowViewModel(new GenDoc.Services.Staff.StaffRowOverview(
            Id: 1, FullName: "БУГАЙ Сергій Михайлович", Rank: "капітан",
            Position: "курсовий офіцер", UnitName: null,
            CurrentState: StaffEventKind.Sick, DocsCount: 0, IsCourseOfficer: true));

        Assert.Equal("На лікуванні", row.StateText);
    }

    [Fact]
    public void SickEvent_IsOfferedAsAStateFilter()
    {
        var labels = StaffViewModel.StateFilterOptions.Select(o => o.Filter).ToList();

        Assert.Contains(StaffStateFilter.Sick, labels);
    }

    [Fact]
    public void SickDialog_HasItsOwnHeader()
        => Assert.Equal("Оформити хворобу", StaffDocDialogViewModel.HeaderFor(StaffEventKind.Sick));

    [Fact]
    public void ExistingKinds_KeepTheirHeaders()
    {
        Assert.Equal("Оформити відрядження", StaffDocDialogViewModel.HeaderFor(StaffEventKind.BusinessTrip));
        Assert.Equal("Оформити відпустку", StaffDocDialogViewModel.HeaderFor(StaffEventKind.Leave));
        Assert.Equal("Згенерувати документи", StaffDocDialogViewModel.HeaderFor(null));
    }

    [Fact]
    public void AuditLabel_NamesTheSickEvent()
        => Assert.Equal("хворобу", GenDoc.Services.Staff.StaffService.KindLabel(StaffEventKind.Sick));
}
