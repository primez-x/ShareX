"""Create test images and seed the portable History.db so the library has content.
Usage: python -I seed_library.py <profile_dir> <count>"""
import os
import sqlite3
import struct
import sys
import zlib
from datetime import datetime, timedelta, timezone

profile, count = sys.argv[1], int(sys.argv[2])
folder = os.path.join(profile, 'Screenshots', 'seed')
os.makedirs(folder, exist_ok=True)


def png(path, w, h, rgb):
    """Write a solid-colour PNG with a darker diagonal band."""
    rows = []
    for y in range(h):
        row = bytearray([0])
        for x in range(w):
            band = abs((x * h // w) - y) < h // 8
            r, g, b = (c // 2 for c in rgb) if band else rgb
            row += bytes((r, g, b))
        rows.append(bytes(row))
    raw = zlib.compress(b''.join(rows), 9)

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)

    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', raw) + chunk(b'IEND', b''))


palette = [(52, 152, 219), (231, 76, 60), (46, 204, 113), (155, 89, 182), (241, 196, 15), (26, 188, 156), (230, 126, 34), (149, 165, 166)]
db = sqlite3.connect(os.path.join(profile, 'History.db'))
db.execute('''CREATE TABLE IF NOT EXISTS History (Id INTEGER PRIMARY KEY AUTOINCREMENT, FileName TEXT, FilePath TEXT, DateTime TEXT,
    Type TEXT, Host TEXT, URL TEXT, ThumbnailURL TEXT, DeletionURL TEXT, ShortenedURL TEXT, Tags TEXT)''')
now = datetime.now(timezone.utc).astimezone()
for i in range(count):
    name = f'Capture {i + 1:03d} {"invoice" if i % 7 == 0 else "screen"}.png'
    path = os.path.join(folder, name)
    png(path, 320 + (i % 3) * 80, 200 + (i % 2) * 60, palette[i % len(palette)])
    when = now - timedelta(hours=(count - i) * 5)
    db.execute('INSERT INTO History (FileName, FilePath, DateTime, Type, Host) VALUES (?, ?, ?, ?, ?)',
               (name, path, when.isoformat(), 'Image', ''))
# One text file and one row whose file is missing.
txt = os.path.join(folder, 'notes.txt')
open(txt, 'w').write('library test')
db.execute('INSERT INTO History (FileName, FilePath, DateTime, Type, Host) VALUES (?, ?, ?, ?, ?)', ('notes.txt', txt, now.isoformat(), 'File', ''))
db.execute('INSERT INTO History (FileName, FilePath, DateTime, Type, Host) VALUES (?, ?, ?, ?, ?)', ('gone.png', os.path.join(folder, 'gone.png'), now.isoformat(), 'Image', ''))
db.commit()
print(f'seeded {count} images + 1 text file + 1 missing file into {folder}')
