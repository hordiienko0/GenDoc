using GenDoc.Models;
using GenDoc.Services.Rooms;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Rooms;

namespace GenDoc.Tests.Rooms;

public class RoomDraftSurvivesFilterTests
{
    private static RoomsViewModel Create(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.Rooms.AddRange(
                new Room { Building = "Корпус А", Number = "101", Capacity = 2 },
                new Room { Building = "Корпус Б", Number = "202", Capacity = 2 });
            ctx.SaveChanges();
        }

        return new RoomsViewModel(new RoomService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()));
    }

    [Fact]
    public void NewUnsavedRoom_StaysOnTop_WhileSearching()
    {
        using var db = new TestDb();
        var vm = Create(db);

        vm.AddRoomCommand.Execute(null);
        var draft = vm.Rooms[0];
        Assert.True(draft.IsNew);

        vm.SearchText = "202";

        Assert.Equal(2, vm.Rooms.Count);
        Assert.Same(draft, vm.Rooms[0]);
        Assert.Equal("202", vm.Rooms[1].Number);

        vm.SearchText = null;

        Assert.Equal(3, vm.Rooms.Count);
        Assert.Same(draft, vm.Rooms[0]);

        vm.CancelEditCommand.Execute(draft);

        Assert.Equal(2, vm.Rooms.Count);
        Assert.DoesNotContain(vm.Rooms, r => r.IsNew);

        vm.SearchText = "101";
        Assert.Single(vm.Rooms);
    }

    [Fact]
    public void SavingTheDraft_DropsItFromTheTop_AndKeepsTheFilter()
    {
        using var db = new TestDb();
        var vm = Create(db);

        vm.AddRoomCommand.Execute(null);
        var draft = vm.Rooms[0];
        vm.SearchText = "303";
        Assert.Single(vm.Rooms);

        draft.BuildingInput = "Корпус В";
        draft.NumberInput = "303";
        draft.CapacityInput = "3";
        vm.SaveRoomCommand.Execute(draft);

        var saved = Assert.Single(vm.Rooms);
        Assert.False(saved.IsNew);
        Assert.Equal("303", saved.Number);
    }
}
