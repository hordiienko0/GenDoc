using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Templates;

/// <summary>
/// Один підпис призначення шаблона на весь застосунок: список «Шаблони» і панель
/// «Швидке оформлення» в постійному складі мають називати те саме однаково.
/// </summary>
public static class TemplateAudienceCaption
{
    public static string For(TemplateAudience audience) => audience switch
    {
        TemplateAudience.PermanentStaff => "Постійний склад",
        TemplateAudience.Everyone => "для всіх",
        _ => "Набори"
    };
}
