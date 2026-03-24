import { PDFDocument, StandardFonts } from 'pdf-lib';
import * as fs from 'fs';
import * as path from 'path';

const FIXTURES_DIR = path.resolve(__dirname, '../fixtures/files');

const SEARCHABLE_TEXT = 'Homestead E2E Test Document - Invoice #12345';

export async function ensureFixtureFiles(): Promise<{
  pdfPath: string;
  imagePath: string;
}> {
  fs.mkdirSync(FIXTURES_DIR, { recursive: true });

  const pdfPath = path.join(FIXTURES_DIR, 'test-document.pdf');
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const page = doc.addPage([612, 792]);
  page.drawText(SEARCHABLE_TEXT, { x: 50, y: 700, size: 14, font });
  fs.writeFileSync(pdfPath, await doc.save());

  const imagePath = path.join(FIXTURES_DIR, 'test-image.png');
  {
    // Create a simple 10x10 PNG using raw pixel data via zlib
    const { deflateSync } = require('zlib') as typeof import('zlib');
    const width = 10;
    const height = 10;

    // Raw pixel rows: filter byte (0) + RGB for each pixel (red)
    const rawData = Buffer.alloc(height * (1 + width * 3));
    for (let y = 0; y < height; y++) {
      const offset = y * (1 + width * 3);
      rawData[offset] = 0; // filter: none
      for (let x = 0; x < width; x++) {
        rawData[offset + 1 + x * 3] = 255; // R
        rawData[offset + 2 + x * 3] = 0;   // G
        rawData[offset + 3 + x * 3] = 0;   // B
      }
    }
    const compressed = deflateSync(rawData);

    const crc32 = (buf: Buffer): number => {
      let c = 0xffffffff;
      for (let i = 0; i < buf.length; i++) {
        c ^= buf[i];
        for (let j = 0; j < 8; j++) {
          c = (c >>> 1) ^ (c & 1 ? 0xedb88320 : 0);
        }
      }
      return (c ^ 0xffffffff) >>> 0;
    };

    const makeChunk = (type: string, data: Buffer): Buffer => {
      const len = Buffer.alloc(4);
      len.writeUInt32BE(data.length);
      const typeAndData = Buffer.concat([Buffer.from(type, 'ascii'), data]);
      const crc = Buffer.alloc(4);
      crc.writeUInt32BE(crc32(typeAndData));
      return Buffer.concat([len, typeAndData, crc]);
    };

    // IHDR: width, height, bit depth, color type (2=RGB), compression, filter, interlace
    const ihdrData = Buffer.alloc(13);
    ihdrData.writeUInt32BE(width, 0);
    ihdrData.writeUInt32BE(height, 4);
    ihdrData[8] = 8;  // bit depth
    ihdrData[9] = 2;  // color type: RGB
    ihdrData[10] = 0;
    ihdrData[11] = 0;
    ihdrData[12] = 0;

    const png = Buffer.concat([
      Buffer.from('89504e470d0a1a0a', 'hex'), // PNG signature
      makeChunk('IHDR', ihdrData),
      makeChunk('IDAT', compressed),
      makeChunk('IEND', Buffer.alloc(0)),
    ]);
    fs.writeFileSync(imagePath, png);
  }

  return { pdfPath, imagePath };
}
