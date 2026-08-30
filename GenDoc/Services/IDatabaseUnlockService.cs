namespace GenDoc.Services
{
    public interface IDatabaseUnlockService
    {
        // Чи існує файл бази. Потрібне окремо від TryUnlock, бо режим з'єднання
        // за замовчуванням - ReadWriteCreate: відсутню базу Open() мовчки
        // СТВОРИТЬ на введеному паролі, і вхід виглядатиме вдалим (аудит
        // 2026-08-28). Перший запуск лишається можливим, але з підтвердженням.
        bool DatabaseExists { get; }

        // Повний шлях, за яким застосунок шукає базу - показується в тому
        // підтвердженні: без нього оператор не зрозуміє, що дивляться не туди.
        string DatabasePath { get; }

        bool TryUnlock(string password, out string? errorMessage);
    }
}
