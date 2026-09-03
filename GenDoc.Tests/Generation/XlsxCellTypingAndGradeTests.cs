using ClosedXML.Excel;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class XlsxCellTypingAndGradeTests
{
    private static IXLCell Cell()
    {
        var wb = new XLWorkbook();
        return wb.AddWorksheet("a").Cell(1, 1);
    }

    [Theory]
    [InlineData("0501234567")]
    [InlineData("021000")]
    [InlineData("007")]
    public void LeadingZeroValues_StayText(string raw)
    {
        var cell = Cell();

        XlsxGenerationService.AssignTypedOrString(cell, raw);

        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(raw, cell.GetString());
    }

    [Theory]
    [InlineData("42")]
    [InlineData("0")]
    [InlineData("1250")]
    public void PlainNumbers_StayNumbers(string raw)
    {
        var cell = Cell();

        XlsxGenerationService.AssignTypedOrString(cell, raw);

        Assert.Equal(XLDataType.Number, cell.DataType);
    }

    [Fact]
    public void Dates_StayDates()
    {
        var cell = Cell();

        XlsxGenerationService.AssignTypedOrString(cell, "06.08.2026");

        Assert.Equal(XLDataType.DateTime, cell.DataType);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(37, 4)]
    public void GradeRandom34_IsStableAcrossCalls(int recipientId, int columnIndex)
    {
        var first = XlsxGenerationService.ComputeGradeRandom34(recipientId, columnIndex);
        var second = XlsxGenerationService.ComputeGradeRandom34(recipientId, columnIndex);

        Assert.Equal(first, second);
        Assert.InRange(first, 3, 4);
    }

    [Fact]
    public void GradeRandom34_VariesAcrossPeopleAndColumns()
    {
        var values = new List<int>();
        for (var person = 1; person <= 20; person++)
            for (var column = 0; column < 3; column++)
                values.Add(XlsxGenerationService.ComputeGradeRandom34(person, column));

        Assert.Contains(3, values);
        Assert.Contains(4, values);
    }

    [Fact]
    public void GradeRandom34_MatchesRecordedValues()
    {
        var actual = new[]
        {
            XlsxGenerationService.ComputeGradeRandom34(1, 0),
            XlsxGenerationService.ComputeGradeRandom34(1, 1),
            XlsxGenerationService.ComputeGradeRandom34(2, 0),
            XlsxGenerationService.ComputeGradeRandom34(37, 4),
        };

        Assert.Equal(new[] { 3, 4, 4, 3 }, actual);
    }
}
