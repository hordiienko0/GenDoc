# Рапорти на котлове харчування — груповий і індивідуальний (design doc)

Дата: 2026-08-05
Фікстури: `шаблони/Шаблон_Рапорт_котлове_ГРУПОВИЙ (2).docx`,
`шаблони/Шаблон_Рапорт_котлове_ІНДИВІДУАЛЬНИЙ (1).docx` — вміст перевірено
(`word/document.xml`, `header*.xml`, `footer*.xml`), тег-інвентар нижче
підтверджений реальними файлами, а не лише текстом промпту.

## 1. Контекст

Основний рушій уже реалізований у попередніх комітах цієї сесії й тут не
чіпається: offset-mapped заміна плейсхолдерів, repeating blocks +
`IDocumentGenerationService.GenerateGroup`, `Template.Kind`/
`TemplateFieldMapping.IsInsideRepeatingBlock`, `GeneratedGroupDocument` з
nullable `ExportTemplateId`/`TemplateId`, `RosterSelection` + сортування за
рангом (`RankOrder`/`UkrainianCollation`/`RosterOrdering`), ad-hoc генерація
через Staff (`StaffService.GenerateDocumentsAsync`,
`StaffDocDialogViewModel`), `UkrainianGrammar` (Gender/Accusative/ArrivedVerb/
SuchPronoun), схема БД до v14 включно.

Реальний вміст обох `.docx` підтвердив точний тег-інвентар із задачі:

- **Груповий**: `{{дата_прибуття}}`, `{{дата_зарахування}}`, блок
  `{{#список}}…{{звання}}→{{піб}}{{роздільник}}…{{/список}}` (прості абзаци з
  табуляцією — **не таблиця**, тож підтримка table-row blocks не потрібна),
  `{{звання_підписанта}}`, `{{піб_підписанта}}`, `{{дата_рапорту}}`.
- **Індивідуальний**: `{{звання_зв}}`, `{{піб_зв}}`, `{{таким}}`,
  `{{дата_прибуття}}`, `{{прибув}}`, `{{дата_зарахування}}`,
  `{{дата_посвідчення}}`, `{{номер_посвідчення}}`, `{{прод_атестат}}`,
  `{{звання_підписанта}}`, `{{піб_підписанта}}`, `{{дата_рапорту}}`.

Жодних `{{посада_зв}}`, `{{кімната}}` чи адресних/організаційних тегів —
посада й заклад є literal-текстом. Заголовки/футери — «ВІДКРИТА ІНФОРМАЦІЯ»,
без плейсхолдерів.

## 2. Прибрати зайве

Раніше в цій сесії додано `{{посада_зв}}`/`{{кімната}}`/
`Recipient.PositionAccusative`, яких немає в реальних шаблонах. Видалити з:
`PlaceholderTagMaps.RecipientTagMap`, `TemplateFieldCatalog.RecipientFields`,
`GenerationService.GetRecipientFieldValue`, `Recipient`/`RecipientEditModel`/
`PersonCardViewModel`/картки особи (поле «Посада (знахідний відмінок)»).
Колонку `PositionAccusative` в БД **не** дропати — існуючі міграції в проєкті
тільки додають колонки, ніколи не видаляють; лишити її сиротою.

## 3. Модель даних

- `Recipient.TravelCertificateDate` (`DateOnly?`) — **не** ручна мітка, а
  звичайне поле, як `TravelCertificateNumber`/`FoodCertificate`. Тег
  `{{дата_посвідчення}}` мапиться на `MappingSourceType.Recipient`
  (`PlaceholderTagMaps`), форматується `UkrainianDate.Long` у
  `GenerationService.GetRecipientFieldValue`. Показати в картці особи поруч з
  іншими полями посвідчення/атестата.
- `AppSettings.LastSignerByTemplateJson` (`string?`) — JSON-словник
  `contextKey → recipientId` для пам'яті підписанта. `contextKey` — простий
  рядок: `"pkg:{packageId}"` для групового запуску, `"tpl:{templateId}"` для
  ad-hoc (перший обраний шаблон, якщо кілька — навмисне спрощення, як і з
  `LastManualValuesJson` раніше).
- Схема v15: `AddMissingColumns` для обох нових колонок, ідемпотентно, за
  наявним патерном.

## 4. Нові допоміжні сервіси

- **`Services/UkrainianDate.cs`** — `static string Long(DateOnly date)` →
  «15 липня 2026 року» (день + місяць у родовому відмінку + «року»). Таблиця
  12 форм місяця (січня, лютого, … грудня), без залежності від системної
  культури (щоб не зламатись на машині з іншим локале).
- **`NameFormatter.SignatureName(Recipient r)`** — `"{FirstName} {LASTNAME}"`
  (ім'я як є, прізвище — `ToUpper(uk-UA)`, без по батькові). Приклад:
  «Сергій ПОНОМАРЕНКО».

## 5. Спільна форма ручних міток

Один новий компонент замість дублювання логіки в двох місцях, де сьогодні
збираються ручні мітки (`GenerationViewModel.ManualTagInputs` і
`StaffDocDialogViewModel.ManualTags`):

```csharp
public enum ManualTagKind { Text, Date }

public partial class ManualTagRowViewModel : ObservableObject
{
    public string Tag { get; }
    public ManualTagKind Kind { get; }
    [ObservableProperty] private string? value;      // Text: сире значення. Date: відформатований рядок.
    [ObservableProperty] private DateTime? dateValue; // тільки Kind == Date
}

public record StaffPickerOption(int RecipientId, string Rank, string SignatureName, string DisplayLabel);

public partial class SignerPickerViewModel : ObservableObject
{
    public ObservableCollection<StaffPickerOption> Options { get; }
    [ObservableProperty] private StaffPickerOption? selected;
}

public class ManualTagFormViewModel
{
    public ObservableCollection<ManualTagRowViewModel> Rows { get; }
    public SignerPickerViewModel? Signer { get; }     // null, якщо пари тегів підписанта немає
    public bool HasContent => Rows.Count > 0 || Signer is not null;
    public Dictionary<string, string> GetValues();    // Rows + розгорнутий Signer у два теги
}
```

Білдер (`IManualTagFormBuilder.BuildAsync(IReadOnlyList<string> tags, string contextKey)`)
класифікує вхідні теги:

| Тег(и) | Kind | Префіл | Формат |
|---|---|---|---|
| `дата_прибуття` | Date | `ActiveIntakeState.Current?.DateStart`, інакше сьогодні | `UkrainianDate.Long` |
| `дата_зарахування` | Date | те саме +1 день | `UkrainianDate.Long` |
| `дата_рапорту` | Date | сьогодні | `dd.MM.yyyy` |
| `звання_підписанта`+`піб_підписанта` (пара) | Signer | збіг `CurrentUserFullName` з ПІБ постійного складу → інакше останній використаний за `contextKey` (`LastSignerByTemplateJson`) → інакше порожньо | `Rank`, `NameFormatter.SignatureName` |
| усе інше | Text | — (або з `LastManualValuesJson`, як зараз) | — |

Дати беруться з `ActiveIntakeState` (уже наявний синглтон активного набору,
той самий, що використовує `CompletenessService.GetBadgeCountAsync`) — окремого
пікера набору в цій формі немає.

Після успішної генерації — `SaveSignerChoiceAsync(contextKey, recipientId)`
пише в `LastSignerByTemplateJson` (той самий патерн злиття, що вже є для
`LastManualValuesJson` у `StaffService`).

**Де підключається:** `GenerationViewModel` (груповий запуск) і
`StaffDocDialogViewModel` (ad-hoc-діалог). Діалог «Перегенерувати» в Архіві
(`ManualValuesDialogViewModel`) залишається без змін — простий текстовий
список, як зараз.

## 6. Тестування

- `UkrainianDate.Long` — кілька місяців, однозначні/двозначні дні.
- `NameFormatter.SignatureName` — звичайний випадок, відсутнє по батькові не
  впливає (не використовується).
- Класифікація тегів білдера (`Text`/`Date`/`Signer`-пара) і префіл дат —
  чиста логіка без БД, тестується так само, як `UkrainianGrammar`.
- Регресія: `GenerateOne`/`GenerateGroup` і раніше написані тести engine/
  blocks лишаються зеленими без змін.

## 7. Acceptance (з початкової задачі)

1. Завантаження групового шаблону класифікує `Kind = Group`; підказка ручних
   міток показує рівно 3 дати (у формі `ManualTagFormViewModel.Rows`) +
   пікер підписанта — і нічого з `{{звання}}`/`{{піб}}`/`{{роздільник}}`/
   маркерів блоку. Усі три дати й підписант префілені — оператор може
   згенерувати без ручного вводу.
2. 32 одержувачі → один DOCX, вигляд ідентичний джерелу (колонка звань на
   табуляції, `;`/`.`, шапка/футер «Відкрита інформація», поля сторінки).
3. 3 позначені одержувачі → документ рівно з них, у порядку за рангом,
   архівується окремою версією `GeneratedGroupDocument` (не дедуплікується).
4. Індивідуальний шаблон класифікується `PerRecipient`; генерація для
   постійного складу заповнює звання/ПІБ у знахідному відмінку, форми роду,
   дані посвідчення/атестата з картки людини.
5. Генерація того самого шаблону «в догонку» для однієї людини без прогону
   пакета створює документ і запис архіву з `Version = n+1`.
6. Усі наявні фікстури-шаблони й далі генеруються побайтово ідентично
   (регресія вже покрита раніше написаними тестами).
7. `dotnet build` без попереджень; конвенції MVVM дотримано.
8. Аудит-лог пишеться для групової й ad-hoc генерації (уже реалізовано).

## 8. Поза межами цієї роботи

- Table-row repeating blocks (реальний шаблон їх не використовує).
- Зміни в діалозі «Перегенерувати» Архіву.
- Видалення orphan-колонки `PositionAccusative` з БД.
- Параметризація закладу/локації через org-settings — literal-текст
  лишається literal-текстом, поки не з'явиться реальна потреба.
