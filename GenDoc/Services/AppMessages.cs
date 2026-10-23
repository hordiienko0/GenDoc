namespace GenDoc.Services
{
    public sealed record CountsChangedMessage;

    public sealed record ActiveIntakeChangedMessage;

    public sealed record MatrixChangedMessage;

    /// <summary>Дані частини в «Налаштуваннях» збережено - шапка вікна й меню мають їх підхопити.</summary>
    public sealed record OrganizationSettingsChangedMessage;
}
