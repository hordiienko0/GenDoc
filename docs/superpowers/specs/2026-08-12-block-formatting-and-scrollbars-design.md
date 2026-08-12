# Форматування блоків конструктора + горизонтальні прокрутники

Дата: 2026-08-12. Гілка: `ui-design-alignment`.

Два незалежні шматки, які приїхали одним запитом: полагодити горизонтальні
прокрутники (баг оболонки) і дати оператору вибір шрифту, розміру, кольору,
Ж/К та вирівнювання для блока конструктора (нова можливість, вона ж пункт
«далі за планом» із попередньої сесії).

## A. Горизонтальні прокрутники

### Проблема

`Themes/Theme.xaml` містить один неявний `Style TargetType="ScrollBar"`,описаний
виключно під вертикальний випадок:

- `Width="10"` — застосовується й до горизонтального прокрутника, тож той стає
  смужкою 10 px завширшки; явна `Width` перебиває `Stretch`, і смужка ще й
  опиняється по центру. Це рівно те, що видно на вкладках аркушів;
- `Track` без `Orientation` — розкладається вертикально;
- `IsDirectionReversed="True"` — правильно для вертикального, хибно для
  горизонтального;
- команди `PageUpCommand`/`PageDownCommand` замість `PageLeft`/`PageRight`;
- `SimpleScrollBarThumbStyle` із жорстким `Width="10"` замість `Height`.

Баг загальний для застосунку, не лише для «Шаблонів»: під нього потрапляє
кожен горизонтальний прокрутник.

### Рішення

Зробити неявний стиль чутливим до орієнтації:

- базові сетери й наявний шаблон лишаються **вертикальним** випадком;
- додається `Trigger` на `Orientation="Horizontal"`, який ставить
  `Width="Auto"`, `Height="10"` і власний `ControlTemplate`: `Track` із
  `Orientation="Horizontal"`, `IsDirectionReversed="False"`, командами
  `PageLeftCommand`/`PageRightCommand` і повзунком, стилізованим на `Height="10"`
  (окремий `HorizontalScrollBarThumbStyle`).

Перевірка — око на вкладках аркушів у конструкторі відомості та на сітці
попереднього перегляду: смуга має тягтися на всю ширину `ScrollViewer`.

## B. Форматування блока

### Рівень

Форматування задається **на блок цілком**, не на окремий елемент. Один рядок
підпису чи одна колонка таблиці не можуть відрізнятися від свого блока. Рішення
свідоме; ціна — щоб додати рівень елемента пізніше, доведеться знову чіпати
модель.

### Модель

```csharp
public enum BlockAlignment { Left, Center, Right, Justify }

public record BlockStyle(
    string? FontFamily = null,
    double? FontSize = null,
    bool? Bold = null,
    bool? Italic = null,
    string? Color = null,          // RRGGBB, без «#»
    BlockAlignment? Alignment = null);
```

`TemplateBlock` отримує хвостове `BlockStyle? Style = null`.

**Кожне поле nullable і означає «успадкувати типове для цього типу блока».**
Це не косметика, а вимога сумісності: у збереженому `BuilderJson` поля `Style`
немає взагалі, тож старий шаблон розв'язується в сьогоднішню поведінку
символ у символ. `DefaultIgnoreCondition = WhenWritingNull` у
`TemplateBuilderJson` уже налаштований, тож незаданий стиль і не серіалізується.

### Спільний розв'язувач

`BlockStyleDefaults` поруч із `TemplateSheetLayout` і з тієї самої причини: один
розрахунок годує docx-writer, xlsx-writer **і** прев'ю, інакше вони розійдуться.

```csharp
public record ResolvedBlockStyle(
    string? FontFamily, double? FontSize,
    bool Bold, bool Italic, string? Color, BlockAlignment Alignment);

public static ResolvedBlockStyle Resolve(TemplateBlockKind kind, BlockStyle? style);
```

Типові значення кодують те, що сьогодні зашите в код:

| Блок          | Alignment | Bold  |
|---------------|-----------|-------|
| Header        | Right     | false |
| Title         | Center    | true  |
| DateAndCity   | Justify   | false |
| Paragraph     | Justify   | false |
| Signatures    | Left      | false |
| Table (дані)  | Left      | false |

`FontFamily`, `FontSize`, `Color` типово **null**, і це важливо:

- у .docx незадані означає «не писати `RunFonts`/`FontSize`/`Color` взагалі» —
  Word бере своє з `docDefaults`, як і зараз;
- у .xlsx незадані означає сьогоднішні `Times New Roman` 11, які писав
  `TemplateBlockXlsxWriter.Style(...)`.

Тобто розв'язувач не вигадує шрифт за оператора. У поповері незадане значення
показується як «(типовий)».

### Writers

- **docx** — усе вже тече крізь `Text(...)`/`Cell(...)`, тож підпис міняється на
  `Text(string, ResolvedBlockStyle)`. `RunProperties` набирає `RunFonts`,
  `FontSize` (пів-пункти, тобто `size * 2`), `Bold`, `Italic`, `Color`;
  `Justification` бере `Alignment`.
- **xlsx** — `Style(cell, ResolvedBlockStyle)` кладе `Font.FontName/FontSize/
  Bold/Italic/FontColor` та `Alignment.Horizontal`. `BlockAlignment.Justify`
  відображається в `Left`: у книзі це об'єднана клітинка-смуга, і саме так
  writer поводиться сьогодні.
- **прев'ю** — `PreviewLine`/`SheetPreviewRow` носять уже не `bool Bold`, а
  `ResolvedBlockStyle`, і в'юха малює ним `Run`/`TextBlock`.

### Таблиця

Шапка таблиці — структура, а не оформлення: вона лишається жирною і (в книзі)
центрованою. Від стилю блока шапка бере `FontFamily`, `FontSize`, `Color`,
`Italic`. `Bold` і `Alignment` зі стилю застосовуються до **рядка даних**.

### UI

Кнопка `[Aa]` в шапці кожної картки блока, поряд із `↑ ↓ ✕`, відкриває `Popup`
(`StaysOpen="False"`, прив'язаний до кнопки):

- гарнітура — `ComboBox`: (типовий), Times New Roman, Arial, Calibri, Verdana,
  Courier New;
- розмір — `ComboBox`: (типовий), 8…24;
- Ж / К — два `ToggleButton`;
- вирівнювання — чотири `ToggleButton` (ліворуч / центр / праворуч / ширина);
- колір — рядок кольорових зразків: чорний `000000`, темно-сірий `595959`,
  синій `1F4E79`, червоний `C00000`, зелений `2E7D32`, плюс «типовий»;
- «Скинути» — повертає блок до типового для його типу.

Ручного вводу HEX немає — за наскрізною директивою «мінімум ручного вводу».
Для таблиці перемикачі Ж і вирівнювання підписані як такі, що стосуються рядка
даних.

`TextBox` у картці блока прив'язується до того самого стилю, тож блок в
редакторі виглядає приблизно як результат.

### Тести

- `BlockStyleDefaults.Resolve` — типові значення на кожен `TemplateBlockKind`;
  часткове перекриття (задано лише `FontSize`) лишає решту типовою.
- docx-writer — `RunFonts`/`FontSize` у пів-пунктах/`Color`/`Bold`/`Italic` та
  `Justification` для заданого стилю; **для блока без стилю жодного з цих
  елементів у `RunProperties` немає** (регресія на сумісність).
- xlsx-writer — шрифт, колір і вирівнювання клітинки; шапка таблиці лишається
  жирною при `Bold = false` на блоці.
- JSON — round-trip зі `Style`; старий JSON без поля `Style` дає блок із
  `Style == null` і сьогоднішньою розв'язаною поведінкою.
- прев'ю — розв'язаний стиль рядка збігається з тим, що поклав writer.

## Порядок

1. A цілком (окремий комміт — це загальносистемний фікс оболонки).
2. Модель + `BlockStyleDefaults` + тести на розв'язувач.
3. Writers + прев'ю + їхні тести.
4. VM + XAML-поповер.
5. Жива перевірка: запуск застосунку, «Шаблони» → конструктор.
