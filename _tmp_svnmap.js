// Minimal pure-JS SQLite reader for SVN wc.db NODES table.
// Usage: node _tmp_svnmap.js <wc.db path> [filterRegex]
const fs = require('fs');

const dbPath = process.argv[2];
const filter = process.argv[3] ? new RegExp(process.argv[3], 'i') : null;
const buf = fs.readFileSync(dbPath);

const PAGE_SIZE = buf.readUInt16BE(16);
const RESERVED = buf[20];
const usableSize = PAGE_SIZE - RESERVED;

function pageOffset(pgno) { return (pgno - 1) * PAGE_SIZE; }

function readVarint(off) {
  let v = 0n;
  let i = 0;
  for (; i < 9; i++) {
    const b = buf[off + i];
    if (i === 8) { v = (v << 8n) | BigInt(b); i++; break; }
    v = (v << 7n) | BigInt(b & 0x7f);
    if ((b & 0x80) === 0) { i++; break; }
  }
  return [v, i];
}

function readSerial(off, type) {
  switch (type) {
    case 0: return null;
    case 1: return buf.readInt8(off);
    case 2: return buf.readInt16BE(off);
    case 3: return (buf.readInt8(off) << 16) | buf.readUInt16BE(off + 1);
    case 4: return buf.readInt32BE(off);
    case 5: return Number(buf.readBigInt64BE(off));
    case 6: return buf.readDoubleBE(off);
    case 7: return buf.readDoubleBE(off);
    case 8: return 0;
    case 9: return 1;
    default:
      if (type >= 12) {
        const len = Math.floor((type - 12) / 2);
        if (type % 2 === 0) return buf.toString('utf8', off, off + len);
        return buf.subarray(off, off + len);
      }
      return null;
  }
}
function serialSize(type) {
  if (type >= 12) return Math.floor((type - 12) / 2);
  if (type >= 10) return 0;
  return [0,1,2,3,4,6,8,8,0,0][type];
}

function parsePage(pgno, out) {
  const base = pageOffset(pgno);
  const hdr = pgno === 1 ? 100 : 0;
  const type = buf[base + hdr];
  const nCells = buf.readUInt16BE(base + hdr + 3);
  const cellPtrStart = base + hdr + ((type === 13 || type === 10) ? 8 : 12);
  const rightMost = (type === 5 || type === 2) ? buf.readUInt32BE(base + hdr + 8) : 0;

  if (type === 5) {
    for (let i = 0; i < nCells; i++) {
      const cp = buf.readUInt16BE(cellPtrStart + i * 2);
      const child = buf.readUInt32BE(base + cp);
      parsePage(child, out);
    }
    if (rightMost) parsePage(rightMost, out);
    return;
  }
  if (type === 13) {
    for (let i = 0; i < nCells; i++) {
      const cp = buf.readUInt16BE(cellPtrStart + i * 2);
      let off = base + cp;
      let [payloadLen, n1] = readVarint(off); off += n1;
      let [rowid, n2] = readVarint(off); off += n2;
      payloadLen = Number(payloadLen);
      let p = off;
      let [hdrLen, nh] = readVarint(p); p += nh;
      hdrLen = Number(hdrLen);
      const hdrStart = p;
      const types = [];
      let hp = hdrStart;
      while (hp < hdrStart + hdrLen - nh) {
        let [t, nt] = readVarint(hp); hp += nt;
        types.push(Number(t));
      }
      const vals = [];
      let vp = hdrStart + hdrLen - nh;
      for (const t of types) {
        vals.push(readSerial(vp, t));
        vp += serialSize(t);
      }
      out.push(vals);
    }
    return;
  }
}

function findTableRoot(name) {
  const out = [];
  parsePage(1, out);
  for (const row of out) {
    if (row[1] === name) return row[3];
  }
  return null;
}

const root = findTableRoot('NODES');
if (!root) { console.error('NODES not found'); process.exit(1); }
const rows = [];
parsePage(root, rows);
for (const r of rows) {
  const relpath = r[1];
  if (filter && !filter.test(String(relpath))) continue;
  const chk = r.find(x => typeof x === 'string' && x.startsWith('$sha1$'));
  console.log(String(relpath) + '\t' + (chk || '') + '\t' + r[8]);
}
