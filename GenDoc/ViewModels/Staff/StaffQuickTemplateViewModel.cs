using GenDoc.Models.Enums;
using GenDoc.ViewModels.Templates;

namespace GenDoc.ViewModels.Staff;

/// <summary>Шаблон у панелі «Швидке оформлення»: клік по ньому оформлює документ обраним людям.</summary>
public sealed class StaffQuickTemplateViewModel
{
    public StaffQuickTemplateViewModel(int id, string name, TemplateAudience audience)
    {
        Id = id;
        Name = name;
        Audience = audience;
    }

    public int Id { get; }
    public string Name { get; }
    public TemplateAudience Audience { get; }
    public string AudienceCaption => TemplateAudienceCaption.For(Audience);
}
