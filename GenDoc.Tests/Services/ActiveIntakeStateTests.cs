using GenDoc.Models;
using GenDoc.Services;

namespace GenDoc.Tests.Services;

// Мій набір: обраний профілем, якщо він ще існує; інакше глобальний активний.
public class ActiveIntakeStateTests
{
    private static Intake Intake(int id) => new() { Id = id, Number = id, DisplayNumber = $"Набір №{id}" };

    [Fact]
    public void Pick_PrefersMine() =>
        Assert.Equal(5, ActiveIntakeState.Pick(Intake(5), Intake(4))!.Id);

    [Fact]
    public void Pick_FallsBackToGlobal_WhenMineMissing() =>
        Assert.Equal(4, ActiveIntakeState.Pick(null, Intake(4))!.Id);

    [Fact]
    public void Pick_NullWhenNothing() =>
        Assert.Null(ActiveIntakeState.Pick(null, null));
}
