using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests.Completeness;

public class MatrixCellMenuTests
{
    private sealed class NoopCoordinator : ICellActionCoordinator
    {
        public Task OpenAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task GenerateAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task RegenerateAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task HistoryAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task SaveAsAsync(MatrixCellViewModel cell) => Task.CompletedTask;
    }

    private static MatrixCellViewModel Cell(
        TemplateRequirement requirement, MatrixDocDto? doc, bool isGroup)
    {
        var cell = new MatrixCellViewModel(new NoopCoordinator(), 1, 2, "придатний", requirement, isGroup);
        cell.Initialize(doc);
        return cell;
    }

    private static MatrixDocDto Doc(bool rosterUnknown = false) => new(
        Id: 10, RecipientId: 1, TemplateId: 2, Version: 3, HasContent: true, IsStale: false,
        SourceType: DocumentSourceType.Generated, IsGroup: true, RosterUnknown: rosterUnknown);

    [Fact]
    public void GroupCell_WithoutDocument_HasNoMenu()
    {
        var cell = Cell(TemplateRequirement.Required, doc: null, isGroup: true);
        Assert.False(cell.HasMenu);
    }

    [Fact]
    public void GroupCell_WithDocument_HasMenu()
    {
        var cell = Cell(TemplateRequirement.Required, Doc(), isGroup: true);
        Assert.True(cell.HasMenu);
    }

    [Fact]
    public void GroupCell_RosterUnknown_HasMenu()
    {
        var cell = Cell(TemplateRequirement.Required, Doc(rosterUnknown: true), isGroup: true);
        Assert.True(cell.HasMenu);
    }

    [Fact]
    public void PersonalCell_WithoutDocument_KeepsGenerateMenu()
    {
        var cell = Cell(TemplateRequirement.Required, doc: null, isGroup: false);
        Assert.True(cell.HasMenu);
    }

    [Fact]
    public void NotApplicableCell_HasNoMenu()
    {
        var cell = Cell(TemplateRequirement.NotApplicable, doc: null, isGroup: false);
        Assert.False(cell.HasMenu);
    }
}
