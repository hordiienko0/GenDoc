using System.Globalization;
using GenDoc.Models;

namespace GenDoc.Services.Personnel
{
    /// <summary>Особа разом із тим, чого немає в самому записі: де вона в дереві й до якого набору належить.</summary>
    public sealed record PersonExportRow(
        Recipient Person,
        string FolderName,
        string FolderPath,
        int FolderOrder,
        string? IntakeName);

    public enum PersonExportFieldGroup
    {
        Main,
        Questionnaire,
        EarlierForms,
        Other
    }

    /// <summary>
    /// Поле, яке можна вивести колонкою. Value повертає string, DateTime або null:
    /// дата йде в Excel датою, а не текстом. «№ з/п» значення не має - це номер рядка у файлі.
    /// </summary>
    public sealed record PersonExportField(
        string Key,
        string Label,
        PersonExportFieldGroup Group,
        Func<PersonExportRow, object?> Value)
    {
        public bool IsRowNumber => Key == PersonExportFields.RowNumberKey;

        public string Text(PersonExportRow row) => PersonExportFields.AsText(Value(row));
    }

    public static class PersonExportFields
    {
        public const string RowNumberKey = "RowNumber";
        public const string DateFormat = "dd.MM.yyyy";

        public static readonly IReadOnlyList<PersonExportField> All = Build();

        private static readonly Dictionary<string, PersonExportField> ByKey =
            All.ToDictionary(f => f.Key, StringComparer.Ordinal);

        /// <summary>Колонки, з якими діалог відкривається вперше - звичайний список підрозділу.</summary>
        public static readonly IReadOnlyList<string> DefaultKeys = new[]
        {
            RowNumberKey, "Rank", "FullName", "Position", "ServiceNumber", "Room"
        };

        public static PersonExportField? Find(string? key)
            => key is not null && ByKey.TryGetValue(key, out var field) ? field : null;

        public static string GroupTitle(PersonExportFieldGroup group) => group switch
        {
            PersonExportFieldGroup.Main => "ОСНОВНЕ",
            PersonExportFieldGroup.Questionnaire => "АНКЕТНІ ДАНІ",
            PersonExportFieldGroup.EarlierForms => "ПОЛЯ ПОПЕРЕДНІХ АНКЕТ",
            _ => "ІНШЕ"
        };

        public static string AsText(object? value) => value switch
        {
            null => string.Empty,
            DateTime date => date.ToString(DateFormat, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        private static List<PersonExportField> Build()
        {
            var fields = new List<PersonExportField>
            {
                Core(RowNumberKey, "№ з/п", _ => null),
                Core("Rank", "Звання", r => r.Person.Rank),
                Core("FullName", "ПІБ повністю",
                    r => NameFormatter.FullName(r.Person.LastName, r.Person.FirstName, r.Person.MiddleName)),
                Core("ShortName", "ПІБ скорочено (ПРІЗВИЩЕ І. П.)",
                    r => NameFormatter.ShortName(r.Person.LastName, r.Person.FirstName, r.Person.MiddleName)),
                Core("LastName", "Прізвище", r => r.Person.LastName),
                Core("FirstName", "Ім'я", r => r.Person.FirstName),
                Core("MiddleName", "По батькові", r => r.Person.MiddleName),
                Core("Position", "Посада", r => r.Person.Position),
                Core("ServiceNumber", "Особовий номер", r => r.Person.ServiceNumber),
                Core("Fitness", "Придатність", r => r.Person.FitnessCategory),
                Core("Room", "Кімната", r => FormatRoom(r.Person.Room)),
                Core("Folder", "Папка (підрозділ)", r => r.FolderName),
                Core("FolderPath", "Повний шлях папки", r => r.FolderPath),
                Core("Intake", "Набір", r => r.IntakeName)
            };

            // Анкету беремо з того самого переліку, що й картка особи: нове поле анкети одразу з'являється в експорті.
            foreach (var q in PersonQuestionnaire.Fields)
            {
                var group = q.InCurrentForm ? PersonExportFieldGroup.Questionnaire : PersonExportFieldGroup.EarlierForms;
                var get = q.Get;
                fields.Add(q.Kind == QuestionnaireFieldKind.Date
                    ? new PersonExportField(q.Key, q.Label, group, r => ParseDate(get(r.Person)))
                    : new PersonExportField(q.Key, q.Label, group, r => get(r.Person)));
            }

            fields.Add(new PersonExportField("Weapons", "Зброя (найменування, №)", PersonExportFieldGroup.Other,
                r => FormatWeapons(r.Person)));

            return fields;
        }

        private static PersonExportField Core(string key, string label, Func<PersonExportRow, object?> value)
            => new(key, label, PersonExportFieldGroup.Main, value);

        private static object? ParseDate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return PersonQuestionnaire.TryParseDate(text, out var date) && date is DateOnly d
                ? d.ToDateTime(TimeOnly.MinValue)
                : text;
        }

        private static string? FormatRoom(Room? room)
        {
            if (room is null || string.IsNullOrWhiteSpace(room.Number)) return null;
            return string.IsNullOrWhiteSpace(room.Building) ? room.Number : $"{room.Building} {room.Number}";
        }

        private static string? FormatWeapons(Recipient person)
        {
            var weapons = person.Weapons
                .OrderBy(w => w.Id)
                .Select(w => string.IsNullOrWhiteSpace(w.SerialNumber)
                    ? w.Name.Trim()
                    : $"{w.Name.Trim()} № {w.SerialNumber.Trim()}".Trim())
                .Where(w => w.Length > 0)
                .ToList();
            return weapons.Count == 0 ? null : string.Join("; ", weapons);
        }
    }
}
