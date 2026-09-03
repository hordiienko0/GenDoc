using GenDoc.Models;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests;

public class TestDbSmokeTests
{
    [Fact]
    public void TestDb_WritesAndReadsAcrossSeparateContexts()
    {
        using var db = new TestDb();

        using (var write = db.Factory.CreateDbContext())
        {
            write.Recipients.Add(new Recipient
            {
                LastName = "ШЕВЧЕНКО", FirstName = "Тарас",
                Rank = "майор", Position = "слухач", ServiceNumber = "12345"
            });
            write.SaveChanges();
        }

        using var read = db.Factory.CreateDbContext();
        var person = Assert.Single(read.Recipients.ToList());
        Assert.Equal("ШЕВЧЕНКО", person.LastName);
    }

    [Fact]
    public void TestDb_AppliesSoftDeleteQueryFilter()
    {
        using var db = new TestDb();

        using (var write = db.Factory.CreateDbContext())
        {
            write.Recipients.Add(new Recipient
            {
                LastName = "ВИДАЛЕНИЙ", FirstName = "Іван",
                Rank = "капітан", Position = "слухач", ServiceNumber = "1",
                DeletedAt = DateTime.Now
            });
            write.SaveChanges();
        }

        using var read = db.Factory.CreateDbContext();
        Assert.Empty(read.Recipients.ToList());
        Assert.Single(read.Recipients.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public void TestServices_BuildArchiveAndGenerationServices()
    {
        using var db = new TestDb();

        Assert.NotNull(TestServices.Archive(db));
        Assert.NotNull(TestServices.Generation(db));
    }

    [Fact]
    public void XlsxTemplateScan_ForGeneration_FindsDopuskTemplateRow()
    {
        var (row, mappings) = XlsxTemplateScan.ForGeneration(TemplateFixtures.DopuskXlsx);

        Assert.Equal(7, row);
        Assert.NotEmpty(mappings);
    }
}
