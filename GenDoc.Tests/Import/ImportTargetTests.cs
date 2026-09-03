using GenDoc.Services.Import;

namespace GenDoc.Tests.Import;

public class ImportTargetTests
{
    private static readonly Dictionary<string, int?> UnitMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Основний склад курсу"] = 14,
        ["Постійний склад"] = null
    };

    [Fact]
    public void Unset_target_keeps_the_old_behaviour_and_reads_the_intake_from_the_file()
    {
        var intake = ImportService.ResolveTargetIntakeId(
            ImportTarget.FromFile, UnitMap, "Основний склад курсу");

        Assert.Equal(14, intake);
    }

    [Fact]
    public void Unknown_unit_in_the_file_gives_no_intake()
    {
        Assert.Null(ImportService.ResolveTargetIntakeId(
            ImportTarget.FromFile, UnitMap, "Невідомий підрозділ"));
    }

    [Fact]
    public void Chosen_intake_overrides_the_one_derived_from_the_file()
    {
        var intake = ImportService.ResolveTargetIntakeId(
            new ImportTarget(ImportTargetKind.Intake, IntakeId: 7),
            UnitMap,
            "Основний склад курсу");

        Assert.Equal(7, intake);
    }

    [Fact]
    public void Permanent_staff_belongs_to_no_intake_even_when_the_file_names_one()
    {
        var intake = ImportService.ResolveTargetIntakeId(
            new ImportTarget(ImportTargetKind.PermanentStaff),
            UnitMap,
            "Основний склад курсу");

        Assert.Null(intake);
    }

    [Fact]
    public void Default_target_is_the_file_one()
    {
        Assert.Equal(ImportTargetKind.FromFile, new ImportTarget().Kind);
        Assert.Equal(ImportTargetKind.FromFile, ImportTarget.FromFile.Kind);
    }
}
