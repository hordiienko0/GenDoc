using System.Globalization;
using GenDoc.Models;

namespace GenDoc.Services.Personnel
{
    public enum QuestionnaireFieldKind
    {
        Text,
        MultilineText,
        Date
    }

    public sealed record QuestionnaireField(
        string Key,
        string Label,
        QuestionnaireFieldKind Kind,
        bool InCurrentForm,
        Func<Recipient, string?> Get,
        Action<Recipient, string?> Set);

    public static class PersonQuestionnaire
    {
        public const string DateFormat = "dd.MM.yyyy";
        public const string DateError = "Дата у форматі дд.мм.рррр";

        private static readonly string[] DateInputFormats = { "dd.MM.yyyy", "d.M.yyyy" };

        // Колонки анкети 31 набору (ЗРВ 15.10-31.10.2026) у тому ж порядку, що в документі; звання і ПІБ - угорі картки.
        // Поля з InCurrentForm = false прийшли з попередніх анкет: у картці їх видно, лише коли вони заповнені.
        public static readonly IReadOnlyList<QuestionnaireField> Fields = new[]
        {
            Date("DateOfBirth", "Дата народження", r => r.DateOfBirth, (r, v) => r.DateOfBirth = v),
            Text("Nationality", "Національність", r => r.Nationality, (r, v) => r.Nationality = v),
            Text("CivilEducation", "Освіта цивільна", r => r.CivilEducation, (r, v) => r.CivilEducation = v),
            Text("MilitaryEducation", "Освіта військова (ВОС)", r => r.MilitaryEducation, (r, v) => r.MilitaryEducation = v),
            Text("Vos", "ВОС", r => r.Vos, (r, v) => r.Vos = v),
            Date("ArmedForcesSince", "З якого часу в ЗСУ", r => r.ArmedForcesSince, (r, v) => r.ArmedForcesSince = v),
            Text("ConscriptionOffice", "Яким РТЦК та СП призваний", r => r.ConscriptionOffice, (r, v) => r.ConscriptionOffice = v),
            Multiline("MaritalStatus", "Сімейний стан", r => r.MaritalStatus, (r, v) => r.MaritalStatus = v),
            Text("RegistrationAddress", "Адреса реєстрації", r => r.RegistrationAddress, (r, v) => r.RegistrationAddress = v),
            Text("ResidenceAddress", "Адреса фактичного проживання", r => r.ResidenceAddress, (r, v) => r.ResidenceAddress = v),
            Text("Phone", "Телефон", r => r.Phone, (r, v) => r.Phone = v),
            Multiline("Note", "Примітка", r => r.Note, (r, v) => r.Note = v),
            Text("TaxNumber", "ІПН", r => r.TaxNumber, (r, v) => r.TaxNumber = v),
            Text("Passport", "Паспорт", r => r.Passport, (r, v) => r.Passport = v),
            Text("PlaceOfBirth", "Місце народження", r => r.PlaceOfBirth, (r, v) => r.PlaceOfBirth = v),
            Text("MedicalBoard", "Довідка ВЛК (№, дата)", r => r.MedicalBoard, (r, v) => r.MedicalBoard = v),
            Text("RankOrder", "Наказ про присвоєння в/звання", r => r.RankOrder, (r, v) => r.RankOrder = v),
            Text("NameTransliterated", "ПІБ на іноземній мові", r => r.NameTransliterated, (r, v) => r.NameTransliterated = v),
            Text("ServedBefore", "Служив/не служив", r => r.ServedBefore, (r, v) => r.ServedBefore = v),
            Multiline("ExtraNote", "Примітка (2)", r => r.ExtraNote, (r, v) => r.ExtraNote = v),

            Date("CourseArrivalDate", "З якого часу прибув на курси П та ПК", r => r.CourseArrivalDate, (r, v) => r.CourseArrivalDate = v, current: false),
            Text("GroupName", "Група", r => r.GroupName, (r, v) => r.GroupName = v, current: false),
            Text("MedicalBoardConclusion", "Висновок ВЛК", r => r.MedicalBoardConclusion, (r, v) => r.MedicalBoardConclusion = v, current: false),
            Text("OriginUnit", "З якої частини прибув", r => r.OriginUnit, (r, v) => r.OriginUnit = v, current: false),
            Text("CommanderContact", "Командир (ПІП та телефон)", r => r.CommanderContact, (r, v) => r.CommanderContact = v, current: false),
            Text("IdDocumentNumber", "Номер посвідчення офіцера/військового квитка", r => r.IdDocumentNumber, (r, v) => r.IdDocumentNumber = v, current: false),
            Text("TravelCertificateNumber", "№ посвідчення про відрядження", r => r.TravelCertificateNumber, (r, v) => r.TravelCertificateNumber = v, current: false),
            Date("TravelCertificateDate", "Дата посвідчення про відрядження", r => r.TravelCertificateDate, (r, v) => r.TravelCertificateDate = v, current: false),
            Text("FoodCertificate", "Прод атестат", r => r.FoodCertificate, (r, v) => r.FoodCertificate = v, current: false),
            Text("Vehicle", "Автомобіль, номер авто", r => r.Vehicle, (r, v) => r.Vehicle = v, current: false)
        };

        public static Dictionary<string, string?> Read(Recipient r)
            => Fields.ToDictionary(f => f.Key, f => f.Get(r));

        // Ключа немає у словнику - поле не чіпаємо; дати мають бути перевірені Validate.
        public static List<string> Write(Recipient r, IReadOnlyDictionary<string, string?> values)
        {
            var changed = new List<string>();
            foreach (var field in Fields)
            {
                if (!values.TryGetValue(field.Key, out var raw)) continue;
                var before = field.Get(r);
                field.Set(r, string.IsNullOrWhiteSpace(raw) ? null : raw.Trim());
                if (!string.Equals(before ?? string.Empty, field.Get(r) ?? string.Empty, StringComparison.Ordinal))
                    changed.Add(field.Label);
            }
            return changed;
        }

        public static Dictionary<string, string> Validate(IReadOnlyDictionary<string, string?> values)
        {
            var errors = new Dictionary<string, string>();
            foreach (var field in Fields.Where(f => f.Kind == QuestionnaireFieldKind.Date))
            {
                if (values.TryGetValue(field.Key, out var raw) && !TryParseDate(raw, out _))
                    errors[field.Key] = DateError;
            }
            return errors;
        }

        public static bool TryParseDate(string? text, out DateOnly? date)
        {
            date = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!DateOnly.TryParseExact(text.Trim(), DateInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return false;
            date = parsed;
            return true;
        }

        private static QuestionnaireField Text(
            string key, string label, Func<Recipient, string?> get, Action<Recipient, string?> set, bool current = true)
            => new(key, label, QuestionnaireFieldKind.Text, current, get, set);

        private static QuestionnaireField Multiline(
            string key, string label, Func<Recipient, string?> get, Action<Recipient, string?> set)
            => new(key, label, QuestionnaireFieldKind.MultilineText, true, get, set);

        private static QuestionnaireField Date(
            string key, string label, Func<Recipient, DateOnly?> get, Action<Recipient, DateOnly?> set, bool current = true)
            => new(key, label, QuestionnaireFieldKind.Date, current,
                r => get(r)?.ToString(DateFormat, CultureInfo.InvariantCulture),
                (r, v) => set(r, TryParseDate(v, out var d) ? d : null));
    }
}
