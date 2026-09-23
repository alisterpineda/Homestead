import { deflateSync } from 'node:zlib';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { PDFDocument, StandardFonts } from 'pdf-lib';

/**
 * In-memory upload payloads. Accepted by both `locator.setInputFiles()` and an API `multipart`
 * field, so UI and API uploads share one fixture shape.
 *
 * Every payload's bytes are unique: the backend rejects an upload whose content hash matches an
 * existing version (`duplicate_content`), and tests running in parallel (or the same test on
 * another device project) must never trip over each other's files.
 */
export interface FilePayload {
  name: string;
  mimeType: string;
  buffer: Buffer;
}

/** Real-world PDFs shared with the backend test projects (see tests/fixtures/README.md). */
export const REPO_FIXTURES_DIR = path.resolve(__dirname, '../../tests/fixtures');

/** A one-page PDF with `text` in its text layer, so extraction and full-text search find it. */
export async function makePdf(baseName: string, text: string): Promise<FilePayload> {
  const doc = await PDFDocument.create();
  doc.setTitle(baseName);
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const page = doc.addPage([612, 792]);
  page.drawText(text, { x: 50, y: 700, size: 14, font });
  return { name: `${baseName}.pdf`, mimeType: 'application/pdf', buffer: Buffer.from(await doc.save()) };
}

/** A copy of a repo PDF fixture, re-saved with `baseName` as its title so its hash is unique. */
export async function copyRepoPdf(fixtureFile: string, baseName: string): Promise<FilePayload> {
  const doc = await PDFDocument.load(fs.readFileSync(path.join(REPO_FIXTURES_DIR, fixtureFile)));
  doc.setTitle(baseName);
  return { name: `${baseName}.pdf`, mimeType: 'application/pdf', buffer: Buffer.from(await doc.save()) };
}

/** A small solid-colour PNG whose `tEXt` chunk carries `baseName`, making its bytes unique. */
export function makePng(baseName: string): FilePayload {
  const width = 10;
  const height = 10;
  // Each row: filter byte (0 = none) followed by RGB triples — solid red.
  const raw = Buffer.alloc(height * (1 + width * 3));
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) raw[y * (1 + width * 3) + 1 + x * 3] = 255;
  }

  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 2; // colour type: RGB

  const buffer = Buffer.concat([
    Buffer.from('89504e470d0a1a0a', 'hex'),
    pngChunk('IHDR', ihdr),
    pngChunk('tEXt', Buffer.from(`Title\0${baseName}`, 'latin1')),
    pngChunk('IDAT', deflateSync(raw)),
    pngChunk('IEND', Buffer.alloc(0)),
  ]);
  return { name: `${baseName}.png`, mimeType: 'image/png', buffer };
}

function pngChunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}

function crc32(buf: Buffer): number {
  let c = 0xffffffff;
  for (const byte of buf) {
    c ^= byte;
    for (let j = 0; j < 8; j++) c = (c >>> 1) ^ (c & 1 ? 0xedb88320 : 0);
  }
  return (c ^ 0xffffffff) >>> 0;
}
