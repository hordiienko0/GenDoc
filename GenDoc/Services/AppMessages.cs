namespace GenDoc.Services
{
    // Лічильники людей у дереві застаріли (імпорт, переміщення, збереження, кошик).
    public sealed record CountsChangedMessage;

    // Створено/закрито набір — статус-рядок і дерево мають оновитись.
    public sealed record ActiveIntakeChangedMessage;

    // Матриця комплектності застаріла (генерація/перегенерація/зміна вимог пакета/правки людини).
    public sealed record MatrixChangedMessage;
}
