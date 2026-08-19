using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class RoomDisplayTests
{
    [Theory]
    [InlineData(null, "307", "307")]
    [InlineData("", "307", "307")]
    [InlineData("Б", "307", "Б / 307")]
    [InlineData("Б", null, "Б")]
    [InlineData(null, null, "-")]
    public void RoomDisplay_OmitsEmptyBuilding(string? building, string? number, string expected)
        => Assert.Equal(expected, PersonCardViewModel.FormatRoom(building, number));
}
