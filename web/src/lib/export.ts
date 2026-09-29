import type { TailoredSection } from '../lib/api'

/* Client-side export with zero dependencies.
 *
 *  - DOCX: a valid .docx is a ZIP archive; we write the ZIP ourselves using
 *    stored (uncompressed) entries, which Word and LibreOffice accept.
 *  - PDF: a minimal Helvetica PDF where every string is a WinAnsi byte string.
 *    The content is the approved sections, paginated to the page.
 */

const WINANSI: Record<string, number> = {
  '\u2018': 0x91, '\u2019': 0x92, '\u201c': 0x93, '\u201d': 0x94,
  '\u2013': 0x96, '\u2014': 0x97, '\u2026': 0x85, '\u00b7': 0xb7,
  '\u2022': 0x95, '\u00a0': 0xa0, '\u00e9': 0xe9, '\u00e8': 0xe8,
  '\u00ea': 0xea, '\u00fc': 0xfc, '\u00f6': 0xf6, '\u00e4': 0xe4,
  '\u00fb': 0xfb, '\u00f1': 0xf1, '\u00e0': 0xe0, '\u201a': 0x82,
}

function toWinAnsi(s: string): string {
  let out = ''
  for (const ch of s) {
    const mapped = WINANSI[ch]
    if (mapped !== undefined) out += String.fromCharCode(mapped)
    else {
      const cp = ch.codePointAt(0)!
      out += String.fromCharCode(cp > 255 ? 0x3f : cp)
    }
  }
  return out
}

/* ------------------------------------------------------------------ ZIP ---- */

const CRC_TABLE = (() => {
  const t = new Int32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    t[n] = c
  }
  return t
})()

function crc32(bytes: Uint8Array): number {
  let c = -1
  for (const b of bytes) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8)
  return (c ^ -1) >>> 0
}

interface ZipEntry {
  name: string
  data: Uint8Array
}

function buildZip(entries: ZipEntry[]): Blob {
  const parts: Uint8Array[] = []
  const central: Uint8Array[] = []
  let offset = 0

  for (const entry of entries) {
    const name = new TextEncoder().encode(entry.name)
    const crc = crc32(entry.data)

    const local = new DataView(new ArrayBuffer(30))
    local.setUint32(0, 0x04034b50, true)
    local.setUint16(4, 20, true)
    local.setUint16(6, 0x0800, true)
    local.setUint16(8, 0, true) // stored
    local.setUint16(10, 0, true)
    local.setUint16(12, 0x0021, true)
    local.setUint32(14, crc, true)
    local.setUint32(18, entry.data.length, true)
    local.setUint32(22, entry.data.length, true)
    local.setUint16(26, name.length, true)
    local.setUint16(28, 0, true)
    parts.push(new Uint8Array(local.buffer), name, entry.data)

    const cen = new DataView(new ArrayBuffer(46))
    cen.setUint32(0, 0x02014b50, true)
    cen.setUint16(4, 20, true)
    cen.setUint16(6, 20, true)
    cen.setUint16(8, 0x0800, true)
    cen.setUint16(10, 0, true)
    cen.setUint16(12, 0, true)
    cen.setUint16(14, 0x0021, true)
    cen.setUint32(16, crc, true)
    cen.setUint32(20, entry.data.length, true)
    cen.setUint32(24, entry.data.length, true)
    cen.setUint16(28, name.length, true)
    cen.setUint16(30, 0, true)
    cen.setUint16(32, 0, true)
    cen.setUint16(34, 0, true)
    cen.setUint16(36, 0, true)
    cen.setUint32(38, 0, true)
    cen.setUint32(42, offset, true)
    central.push(new Uint8Array(cen.buffer), name)
    offset += 30 + name.length + entry.data.length
  }

  const end = new DataView(new ArrayBuffer(22))
  end.setUint32(0, 0x06054b50, true)
  end.setUint16(8, entries.length, true)
  end.setUint16(10, entries.length, true)
  const centralSize = central.reduce((n, p) => n + p.length, 0)
  end.setUint32(12, centralSize, true)
  end.setUint32(16, offset, true)

  const total = parts.reduce((n, p) => n + p.length, 0) + central.reduce((n, p) => n + p.length, 0) + 22
  const out = new Uint8Array(total)
  let pos = 0
  for (const p of parts) { out.set(p, pos); pos += p.length }
  for (const p of central) { out.set(p, pos); pos += p.length }
  out.set(new Uint8Array(end.buffer), pos)
  return new Blob([out], { type: 'application/octet-stream' })
}

function xmlEscape(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

export function exportDocx(filename: string, sections: TailoredSection[]): void {
  const paragraphs: string[] = []
  for (const section of sections) {
    paragraphs.push(`<w:p><w:pPr><w:pStyle w:val="Heading"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>${xmlEscape(section.heading.toUpperCase())}</w:t></w:r></w:p>`)
    paragraphs.push(
      `<w:p><w:pPr><w:ind w:left="0"/></w:pPr>${section.body
        .split('\n')
        .map((line, i) =>
          i === 0
            ? `<w:r><w:t xml:space="preserve">${xmlEscape(line)}</w:t></w:r>`
            : `<w:r><w:br/></w:r><w:r><w:t xml:space="preserve">${xmlEscape(line)}</w:t></w:r>`,
        )
        .join('')}</w:p>`,
    )
  }

  const documentXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:body>
${paragraphs.join('\n')}
</w:body>
</w:document>`

  const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>`

  const rootRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>`

  const docRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>`

  const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:style w:type="paragraph" w:default="1" w:styleId="Normal">
<w:name w:val="Normal"/>
<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr>
</w:style>
<w:style w:type="paragraph" w:styleId="Heading">
<w:name w:val="heading 1"/>
<w:basedOn w:val="Normal"/>
<w:rPr><w:b/><w:sz w:val="24"/></w:rPr>
</w:style>
</w:styles>`

  const zip = buildZip([
    { name: '[Content_Types].xml', data: new TextEncoder().encode(contentTypes) },
    { name: '_rels/.rels', data: new TextEncoder().encode(rootRels) },
    { name: 'word/document.xml', data: new TextEncoder().encode(documentXml) },
    { name: 'word/_rels/document.xml.rels', data: new TextEncoder().encode(docRels) },
    { name: 'word/styles.xml', data: new TextEncoder().encode(styles) },
  ])

  saveBlob(zip, `${filename}.docx`)
}

/* ------------------------------------------------------------------ PDF ---- */

const PAPER_W = 595
const PAPER_H = 842
const MARGIN = 48

interface PdfLine {
  text: string
  size: number
  leading: number
}

function escapePdfString(s: string): string {
  // toWinAnsi has already produced one JS code unit per WinAnsi byte; writing
  // those code units straight into the string (no TextEncoder round-trip) keeps
  // the bytes exactly what the font's WinAnsiEncoding expects.
  let out = ''
  for (const ch of toWinAnsi(s)) {
    const code = ch.charCodeAt(0)
    if (code === 0x28 || code === 0x29 || code === 0x5c) out += `\\${ch}`
    else out += ch
  }
  return `(${out})`
}

export function exportPdf(filename: string, sections: TailoredSection[]): void {
  const lines: PdfLine[] = []
  for (const section of sections) {
    lines.push({ text: section.heading.toUpperCase(), size: 11, leading: 15 })
    lines.push({ text: '', size: 10, leading: 12 })
    for (const block of section.body.split('\n')) {
      lines.push({ text: block, size: 9.5, leading: 14 })
    }
    lines.push({ text: '', size: 10, leading: 10 })
  }

  // Paginate: a page may hold as many lines as fit its height.
  const usable = PAPER_H - MARGIN * 2
  const pages: { start: number; count: number }[] = []
  let start = 0
  while (start < lines.length) {
    let used = 0
    let count = 0
    while (start + count < lines.length && used + lines[start + count].leading <= usable) {
      used += lines[start + count].leading
      count++
    }
    pages.push({ start, count })
    start += count
  }

  const objects: string[] = []
  const add = (body: string) => {
    objects.push(body)
    return objects.length
  }

  add('<< /Type /Catalog /Pages 2 0 R >>')
  const pagesRef = add(
    `<< /Type /Pages /Kids [${pages.map((_, i) => `${4 + i * 3} 0 R`).join(' ')}] /Count ${pages.length} >>`,
  )
  add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>')

  for (const page of pages) {
    let y = PAPER_H - MARGIN - 14
    const ops: string[] = []
    for (let i = 0; i < page.count; i++) {
      const line = lines[page.start + i]
      if (line.text !== '') ops.push(`BT /F3 ${line.size} Tf 48 ${y.toFixed(2)} Td ${escapePdfString(line.text)} Tj ET`)
      y -= line.leading
    }
    const stream = ops.join('\n')
    const streamRef = add(`<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`)
    add(`<< /Type /Page /Parent ${pagesRef} /MediaBox [0 0 ${PAPER_W} ${PAPER_H}] /Contents ${streamRef} 0 R >>`)
  }

  // Byte offsets for the xref table.
  const chunks: string[] = ['%PDF-1.4\n']
  const offsets: number[] = []
  let total = chunks[0].length
  for (let i = 0; i < objects.length; i++) {
    offsets.push(total)
    chunks.push(`${i + 1} 0 obj\n${objects[i]}\nendobj\n`)
    total += chunks[chunks.length - 1].length
  }
  const xref = offsets.length + 1
  chunks.push(
    `xref\n0 ${xref}\n0000000000 65535 f \n` +
      offsets.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`).join(''),
  )
  const xrefStart = total
  chunks.push(
    `trailer\n<< /Size ${xref} /Root 1 0 R >>\nstartxref\n${xrefStart}\n%%EOF\n`,
  )

  // Every chunk is ASCII except the WinAnsi-encoded text strings, whose code
  // units already hold the exact bytes the font expects. Writing them through
  // Blob(string) would UTF-8-encode those units again (", " -> C2 B7), so the
  // file is assembled as raw bytes instead.
  const joined = chunks.join('')
  const bytes = new Uint8Array(joined.length)
  for (let i = 0; i < joined.length; i++) bytes[i] = joined.charCodeAt(i) & 0xff

  saveBlob(new Blob([bytes], { type: 'application/pdf' }), `${filename}.pdf`)
}

function saveBlob(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 4000)
}