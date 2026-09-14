#!/usr/bin/env node
/*
 * Причісує переноси в шаблонах «шаблони/комплекти/**.docx».
 *
 * Чотири правила, однакові для всіх шаблонів:
 *
 *   1. Переноси слів — у word/settings.xml вмикається autoHyphenation з
 *      hyphenationZone=357 (≈0,63 см) і doNotHyphenateCaps, щоб ПРІЗВИЩА
 *      великими літерами й абревіатури (ХНУПС, ВКСС) не розривалися.
 *      Сім із чотирнадцяти шаблонів мали це від початку — решта вирівнюється
 *      під них. suppressAutoHyphens зі styles.xml знімається: у шаблонах зі
 *      старих .doc він стояв у docDefaults і мовчки вимикав переноси.
 *
 *   2. Адресат — абзац із лівим відступом >= 3000 twips перестає бути
 *      вирівняним по ширині. У колонці «кому» завширшки ~7 см justify
 *      розганяє міжслівні проміжки на пів-рядка.
 *
 *   3. Посада підписанта — абзац просто перед рядком підпису теж по лівому
 *      краю, плюс keepNext, щоб посада не відривалася від підпису на межі
 *      сторінки.
 *
 *   4. Рядок підпису — «{{звання_підписанта}} <таб> {{піб_підписанта}}» з
 *      однією ПРАВОЮ табуляцією на правому полі замість ланцюжка табів і
 *      літеральних пробілів. Так само пари командира й засвідчувача в наказах
 *      і власний підпис особи «{{звання}} <таб> {{піб_підпис}}». Позиція табуляції рахується з pgSz/pgMar
 *      кожного файлу, тож альбомні аркуші й нетипові поля не ламаються.
 *      Правило спрацьовує лише тоді, коли текст абзацу — це рівно два теги
 *      з пробілами між ними; бланки з лінійками підкреслень (роздавально-
 *      здавальна відомість) лишаються як є.
 *
 * Запуск:
 *   node tools/tidy-docx-templates.js --dry   — лише звіт, файли не чіпає
 *   node tools/tidy-docx-templates.js         — править файли на місці
 */
'use strict';

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const ROOT = path.resolve(__dirname, '..', 'шаблони', 'комплекти');
const DRY = process.argv.includes('--dry');

// Пари «звання — ПІБ», що стоять рядком підпису: підписант документа,
// командир у наказі, засвідчувач витягу і сама особа у власному рапорті.
const SIGNATURE_PAIRS = [
    ['{{звання_підписанта}}', '{{піб_підписанта}}'],
    ['{{звання_командира}}', '{{піб_командира}}'],
    ['{{звання_засвідчувача}}', '{{піб_засвідчувача}}'],
    ['{{звання}}', '{{піб_підпис}}']
];
const ADDRESSEE_INDENT = 3000;
const HYPHENATION_ZONE = 357;

// ──────────────────────────────── ZIP ────────────────────────────────
// Пакет .docx перезбирається вручну: змінені записи стискаються наново,
// решта копіюється байт у байт — щоб у коміті були рівно ті XML, що ми чіпали.

const CRC_TABLE = (() => {
    const table = new Int32Array(256);
    for (let i = 0; i < 256; i++) {
        let c = i;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[i] = c;
    }
    return table;
})();

function crc32(buffer) {
    let c = -1;
    for (let i = 0; i < buffer.length; i++) c = CRC_TABLE[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
    return (c ^ -1) >>> 0;
}

function readZip(buffer) {
    let eocd = -1;
    for (let i = buffer.length - 22; i >= 0 && i > buffer.length - 66000; i--) {
        if (buffer.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
    }
    if (eocd < 0) throw new Error('не знайдено кінець центрального каталогу zip');

    const count = buffer.readUInt16LE(eocd + 10);
    let offset = buffer.readUInt32LE(eocd + 16);
    const entries = [];

    for (let i = 0; i < count; i++) {
        if (buffer.readUInt32LE(offset) !== 0x02014b50) throw new Error('пошкоджений центральний каталог zip');

        const nameLength = buffer.readUInt16LE(offset + 28);
        const extraLength = buffer.readUInt16LE(offset + 30);
        const commentLength = buffer.readUInt16LE(offset + 32);
        const localOffset = buffer.readUInt32LE(offset + 42);

        const entry = {
            name: buffer.slice(offset + 46, offset + 46 + nameLength).toString('utf8'),
            versionMadeBy: buffer.readUInt16LE(offset + 4),
            flags: buffer.readUInt16LE(offset + 8),
            method: buffer.readUInt16LE(offset + 10),
            time: buffer.readUInt16LE(offset + 12),
            date: buffer.readUInt16LE(offset + 14),
            crc: buffer.readUInt32LE(offset + 16),
            compressedSize: buffer.readUInt32LE(offset + 20),
            uncompressedSize: buffer.readUInt32LE(offset + 24),
            externalAttributes: buffer.readUInt32LE(offset + 38)
        };

        const dataStart = localOffset + 30
            + buffer.readUInt16LE(localOffset + 26)
            + buffer.readUInt16LE(localOffset + 28);
        entry.data = buffer.slice(dataStart, dataStart + entry.compressedSize);

        entries.push(entry);
        offset += 46 + nameLength + extraLength + commentLength;
    }
    return entries;
}

function readText(entry) {
    return (entry.method === 0 ? entry.data : zlib.inflateRawSync(entry.data)).toString('utf8');
}

function writeText(entry, text) {
    const raw = Buffer.from(text, 'utf8');
    entry.data = zlib.deflateRawSync(raw, { level: 9 });
    entry.method = 8;
    entry.flags &= ~0x08; // розміри відомі наперед — дескриптор даних не потрібен
    entry.crc = crc32(raw);
    entry.compressedSize = entry.data.length;
    entry.uncompressedSize = raw.length;
}

function writeZip(entries) {
    const parts = [];
    const central = [];
    let offset = 0;

    for (const entry of entries) {
        const name = Buffer.from(entry.name, 'utf8');

        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(entry.flags, 6);
        local.writeUInt16LE(entry.method, 8);
        local.writeUInt16LE(entry.time, 10);
        local.writeUInt16LE(entry.date, 12);
        local.writeUInt32LE(entry.crc, 14);
        local.writeUInt32LE(entry.compressedSize, 18);
        local.writeUInt32LE(entry.uncompressedSize, 22);
        local.writeUInt16LE(name.length, 26);
        local.writeUInt16LE(0, 28);

        const header = Buffer.alloc(46);
        header.writeUInt32LE(0x02014b50, 0);
        header.writeUInt16LE(entry.versionMadeBy, 4);
        header.writeUInt16LE(20, 6);
        header.writeUInt16LE(entry.flags, 8);
        header.writeUInt16LE(entry.method, 10);
        header.writeUInt16LE(entry.time, 12);
        header.writeUInt16LE(entry.date, 14);
        header.writeUInt32LE(entry.crc, 16);
        header.writeUInt32LE(entry.compressedSize, 20);
        header.writeUInt32LE(entry.uncompressedSize, 24);
        header.writeUInt16LE(name.length, 28);
        header.writeUInt32LE(entry.externalAttributes, 38);
        header.writeUInt32LE(offset, 42);

        parts.push(local, name, entry.data);
        central.push(header, name);
        offset += local.length + name.length + entry.data.length;
    }

    const centralBuffer = Buffer.concat(central);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(entries.length, 8);
    end.writeUInt16LE(entries.length, 10);
    end.writeUInt32LE(centralBuffer.length, 12);
    end.writeUInt32LE(offset, 16);

    return Buffer.concat([...parts, centralBuffer, end]);
}

// ────────────────────────── XML: дрібна хірургія ──────────────────────────

const PARAGRAPH = /<w:p\b[^>]*\/>|<w:p\b[^>]*>(?:(?!<w:p\b)[\s\S])*?<\/w:p>/g;
const RUN = /<w:r\b[^>]*\/>|<w:r\b[^>]*>(?:(?!<w:r\b)[\s\S])*?<\/w:r>/g;
// Порядок дітей CT_PPr задано схемою: tabs стоїть перед spacing/ind/jc/rPr.
const BEFORE_TABS_ANCHOR = /<w:(?:spacing|ind|jc|rPr)\b/;

const escapeXml = text => text
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// Самозакритий <w:t xml:space="preserve"/> — це порожній прогін, а не початок
// тексту: без (?<!\/) регекс ковтав розмітку до наступного </w:t>.
const TEXT_ELEMENT = /<w:t\b[^>]*?(?<!\/)>[\s\S]*?<\/w:t>/g;

const textOf = xml => (xml.match(TEXT_ELEMENT) || [])
    .map(t => t.replace(/^<w:t\b[^>]*>/, '').replace(/<\/w:t>$/, ''))
    .join('')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');

function findProperties(paragraph) {
    const match = /<w:pPr\b[^>]*>[\s\S]*?<\/w:pPr>|<w:pPr\b[^>]*\/>/.exec(paragraph);
    return match ? { xml: match[0], start: match.index, end: match.index + match[0].length } : null;
}

/** Правка <w:pPr> абзацу; якщо властивостей не було — створює порожні. */
function editProperties(paragraph, edit) {
    let target = paragraph;
    if (!findProperties(target)) {
        const open = /<w:p\b[^>]*>/.exec(target);
        const at = open.index + open[0].length;
        target = target.slice(0, at) + '<w:pPr></w:pPr>' + target.slice(at);
    }

    const properties = findProperties(target);
    const inner = properties.xml.endsWith('/>')
        ? ''
        : properties.xml.replace(/^<w:pPr\b[^>]*>/, '').replace(/<\/w:pPr>$/, '');

    return target.slice(0, properties.start)
        + '<w:pPr>' + edit(inner) + '</w:pPr>'
        + target.slice(properties.end);
}

function setAlignment(inner, value) {
    if (/<w:jc\b[^>]*\/>/.test(inner)) {
        return inner.replace(/<w:jc\b[^>]*\/>/, `<w:jc w:val="${value}"/>`);
    }
    const tag = `<w:jc w:val="${value}"/>`;
    const anchor = /<w:rPr\b/.exec(inner);
    return anchor ? inner.slice(0, anchor.index) + tag + inner.slice(anchor.index) : inner + tag;
}

function setKeepNext(inner) {
    if (/<w:keepNext\b/.test(inner)) return inner;
    const style = /<w:pStyle\b[^>]*\/>/.exec(inner);
    const at = style ? style.index + style[0].length : 0;
    return inner.slice(0, at) + '<w:keepNext/>' + inner.slice(at);
}

function setRightTabStop(inner, position) {
    const cleaned = inner.replace(/<w:tabs\b[^>]*>[\s\S]*?<\/w:tabs>|<w:tabs\b[^>]*\/>/g, '');
    const tag = `<w:tabs><w:tab w:val="right" w:pos="${position}"/></w:tabs>`;
    const anchor = BEFORE_TABS_ANCHOR.exec(cleaned);
    return anchor ? cleaned.slice(0, anchor.index) + tag + cleaned.slice(anchor.index) : cleaned + tag;
}

const alignmentOf = paragraph => {
    const properties = findProperties(paragraph);
    return properties ? (/<w:jc w:val="([^"]+)"/.exec(properties.xml) || [])[1] || null : null;
};

/** Лівий відступ абзацу у twips (w:left або новіший w:start). */
function leftIndentOf(paragraph) {
    const properties = findProperties(paragraph);
    if (!properties) return 0;
    const indent = /<w:ind\b[^>]*\/>/.exec(properties.xml);
    return indent ? Number((/w:(?:left|start)="(-?\d+)"/.exec(indent[0]) || [])[1] || 0) : 0;
}

function rightIndentOf(paragraph) {
    const properties = findProperties(paragraph);
    if (!properties) return 0;
    const indent = /<w:ind\b[^>]*\/>/.exec(properties.xml);
    const value = indent ? Number((/w:(?:right|end)="(-?\d+)"/.exec(indent[0]) || [])[1] || 0) : 0;
    return Math.max(0, value);
}

// ─────────────────────────────── Правила ───────────────────────────────

function tidySettings(xml) {
    const stripped = xml.replace(
        /<w:(?:autoHyphenation|hyphenationZone|doNotHyphenateCaps)\b[^>]*\/>/g, '');

    const trio = '<w:autoHyphenation w:val="true"/>'
        + `<w:hyphenationZone w:val="${HYPHENATION_ZONE}"/>`
        + '<w:doNotHyphenateCaps w:val="true"/>';

    // За схемою CT_Settings ця трійка стоїть одразу після defaultTabStop.
    const anchor = /<w:defaultTabStop\b[^>]*\/>/.exec(stripped);
    if (anchor) {
        const at = anchor.index + anchor[0].length;
        return stripped.slice(0, at) + trio + stripped.slice(at);
    }
    return stripped.replace(/(<w:settings\b[^>]*>)/, `$1${trio}`);
}

// Шаблони, перенесені зі старих .doc, мають suppressAutoHyphens у docDefaults
// або в стилі — і тоді autoHyphenation у settings.xml не діє зовсім.
function tidyStyles(xml) {
    return xml.replace(/<w:suppressAutoHyphens\b[^>]*\/>/g, '');
}

function textWidthOf(documentXml) {
    const size = /<w:pgSz\b[^>]*\/>/.exec(documentXml);
    const margins = /<w:pgMar\b[^>]*\/>/.exec(documentXml);
    if (!size || !margins) return null;

    const width = Number((/w:w="(\d+)"/.exec(size[0]) || [])[1]);
    const left = Number((/w:left="(-?\d+)"/.exec(margins[0]) || [])[1] || 0);
    const right = Number((/w:right="(-?\d+)"/.exec(margins[0]) || [])[1] || 0);
    return Number.isFinite(width) ? width - left - right : null;
}

/** Текст абзацу — рівно звання і ПІБ пари, між ними лише пробіли. */
function isSignatureLine(text, [rank, name]) {
    const trimmed = text.trim();
    return trimmed.startsWith(rank) && trimmed.endsWith(name)
        && trimmed.slice(rank.length, trimmed.length - name.length).trim() === '';
}

/** Три чисті прогони: звання — права табуляція — ПІБ. */
function rebuildSignature(paragraph, tabPosition, [rank, name]) {
    const withProperties = editProperties(paragraph, inner =>
        setRightTabStop(setAlignment(inner, 'left'), tabPosition));

    const properties = findProperties(withProperties);
    const body = withProperties.slice(properties.end, withProperties.lastIndexOf('</w:p>'));

    const firstRun = (body.match(RUN) || [])[0] || '';
    const runProperties = (/<w:rPr\b[^>]*>[\s\S]*?<\/w:rPr>|<w:rPr\b[^>]*\/>/.exec(firstRun) || [''])[0];

    const run = content => `<w:r>${runProperties}${content}</w:r>`;
    const rebuilt = run(`<w:t xml:space="preserve">${escapeXml(rank)}</w:t>`)
        + run('<w:tab/>')
        + run(`<w:t xml:space="preserve">${escapeXml(name)}</w:t>`);

    return withProperties.slice(0, properties.end) + rebuilt + '</w:p>';
}

function tidyDocument(xml, report) {
    const textWidth = textWidthOf(xml);

    const paragraphs = [];
    let match;
    PARAGRAPH.lastIndex = 0;
    while ((match = PARAGRAPH.exec(xml)) !== null) {
        paragraphs.push({ xml: match[0], start: match.index, end: match.index + match[0].length });
    }

    const replacements = new Map();
    const replace = (index, value) => replacements.set(index, value);

    paragraphs.forEach((paragraph, index) => {
        const text = textOf(paragraph.xml);

        // Правило 2 — адресат.
        if (leftIndentOf(paragraph.xml) >= ADDRESSEE_INDENT && alignmentOf(paragraph.xml) === 'both') {
            replace(index, editProperties(replacements.get(index) || paragraph.xml,
                inner => setAlignment(inner, 'left')));
            report.addressee.push(text.trim() || '(порожній абзац блоку)');
        }

        const pair = SIGNATURE_PAIRS.find(([rank, name]) => text.includes(rank) && text.includes(name));
        if (!pair) return;

        // Правило 4 — рядок підпису. Бланки з лінійками сюди не потрапляють.
        if (!isSignatureLine(text, pair)) {
            report.skipped.push(text.trim().slice(0, 90));
            return;
        }

        if (textWidth === null) {
            report.skipped.push('не вдалося визначити ширину тексту: ' + text.trim().slice(0, 60));
            return;
        }

        const tabPosition = textWidth - rightIndentOf(paragraph.xml);
        replace(index, rebuildSignature(replacements.get(index) || paragraph.xml, tabPosition, pair));
        report.signatures.push(tabPosition);

        // Правило 3 — посада підписанта просто над підписом.
        const previous = paragraphs[index - 1];
        if (previous && textOf(previous.xml).trim()) {
            replace(index - 1, editProperties(replacements.get(index - 1) || previous.xml,
                inner => setKeepNext(alignmentOf(previous.xml) === 'both'
                    ? setAlignment(inner, 'left')
                    : inner)));
            report.positions.push(textOf(previous.xml).trim().slice(0, 60));
        }
    });

    if (replacements.size === 0) return xml;

    let result = '';
    let cursor = 0;
    for (const [index, value] of [...replacements.entries()].sort((a, b) => a[0] - b[0])) {
        const paragraph = paragraphs[index];
        result += xml.slice(cursor, paragraph.start) + value;
        cursor = paragraph.end;
    }
    return result + xml.slice(cursor);
}

// ──────────────────────────────── Прогін ────────────────────────────────

function templates(directory, found = []) {
    for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, item.name);
        if (item.isDirectory()) templates(full, found);
        else if (full.toLowerCase().endsWith('.docx')) found.push(full);
    }
    return found;
}

function run() {
let changedFiles = 0;

for (const file of templates(ROOT).sort()) {
    const entries = readZip(fs.readFileSync(file));
    const documentEntry = entries.find(e => e.name === 'word/document.xml');
    const settingsEntry = entries.find(e => e.name === 'word/settings.xml');

    const report = { addressee: [], positions: [], signatures: [], skipped: [] };

    const documentBefore = readText(documentEntry);
    const documentAfter = tidyDocument(documentBefore, report);

    const settingsBefore = settingsEntry ? readText(settingsEntry) : null;
    const settingsAfter = settingsBefore === null ? null : tidySettings(settingsBefore);
    const hyphenationWasOn = settingsBefore !== null && /autoHyphenation/.test(settingsBefore);

    const stylesEntry = entries.find(e => e.name === 'word/styles.xml');
    const stylesBefore = stylesEntry ? readText(stylesEntry) : null;
    const stylesAfter = stylesBefore === null ? null : tidyStyles(stylesBefore);

    const changed = documentAfter !== documentBefore || settingsAfter !== settingsBefore
        || stylesAfter !== stylesBefore;

    console.log('### ' + path.basename(file));
    console.log('  переноси: ' + (hyphenationWasOn ? 'уже були' : 'увімкнено')
        + (settingsBefore === null ? ' (немає settings.xml!)' : ''));
    if (stylesAfter !== stylesBefore) console.log('  стилі    → знято suppressAutoHyphens, що вимикав переноси');
    for (const item of report.addressee) console.log('  адресат  → по лівому краю: ' + item);
    for (const item of report.positions) console.log('  посада   → лівий край + keepNext: ' + item);
    for (const item of report.signatures) console.log('  підпис   → права табуляція на ' + item + ' twips');
    for (const item of report.skipped) console.log('  ПРОПУЩЕНО (не типовий рядок підпису): ' + item);
    if (!changed) console.log('  без змін');

    if (changed && !DRY) {
        if (documentAfter !== documentBefore) writeText(documentEntry, documentAfter);
        if (settingsAfter !== settingsBefore) writeText(settingsEntry, settingsAfter);
        if (stylesAfter !== stylesBefore) writeText(stylesEntry, stylesAfter);
        fs.writeFileSync(file, writeZip(entries));
    }
    if (changed) changedFiles++;
}

console.log();
console.log(DRY
    ? `Пробний прогін: змінилося б ${changedFiles} файлів.`
    : `Змінено ${changedFiles} файлів.`);
}

if (require.main === module) run();

// Читання й перезбирання .docx стають у пригоді й іншим скриптам (наприклад,
// щоб зібрати зразок документа й подивитися на нього очима).
module.exports = { readZip, writeZip, readText, writeText, textOf };
