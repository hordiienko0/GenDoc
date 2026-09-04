using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class RosterScopeTests
{
    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(7, null, false, true)]
    [InlineData(5, 5, false, true)]
    [InlineData(7, 5, false, false)]
    [InlineData(null, 5, false, false)]
    [InlineData(null, 5, true, true)]
    [InlineData(7, 5, true, false)]
    public void InScope_ActiveIntakeLimitsRoster_PermanentStaffOnlyOnExplicitOptIn(
        int? recipientIntakeId, int? intakeId, bool includePermanentStaff, bool expected)
        => Assert.Equal(expected, RosterSelection.InScope(recipientIntakeId, intakeId, includePermanentStaff));

    [Fact]
    public void Everyone_HasNoIntakeScope()
    {
        Assert.Null(RosterSelection.Everyone.IntakeId);
        Assert.False(RosterSelection.Everyone.IncludePermanentStaff);
    }
}
