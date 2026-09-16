using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Staff;

/// <summary>Назви подій постійного складу — однакові в «Шаблонах», у панелі дій і в діалозі.</summary>
public static class StaffEventNaming
{
    public static string For(StaffEventKind kind) => kind switch
    {
        StaffEventKind.BusinessTrip => "Відрядження",
        StaffEventKind.Leave => "Відпустка",
        StaffEventKind.Sick => "Хвороба",
        _ => kind.ToString()
    };

    public static string ActionFor(StaffEventKind kind) => kind switch
    {
        StaffEventKind.BusinessTrip => "Оформити відрядження",
        StaffEventKind.Leave => "Оформити відпустку",
        StaffEventKind.Sick => "Оформити хворобу",
        _ => kind.ToString()
    };
}
