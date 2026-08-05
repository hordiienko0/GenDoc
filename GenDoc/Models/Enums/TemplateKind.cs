namespace GenDoc.Models.Enums
{
    public enum TemplateKind
    {
        // Один документ на кожного одержувача (стара, звична поведінка).
        PerRecipient = 0,

        // Один документ на весь список одержувачів, з повторюваним блоком {{#…}}/{{/…}}.
        Group = 1
    }
}
