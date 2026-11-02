"""Compare exported synthetic QR fixtures with the independent ReportLab encoder."""
import json
from pathlib import Path
import sys
from reportlab.graphics.barcode import qrencoder

fixtures = list(Path(sys.argv[1]).glob('qr-*.json'))
assert len(fixtures) >= 4, 'Export the QR fixtures using Gather.Tests --qr-fixtures first.'
for file in fixtures:
    fixture = json.loads(file.read_text(encoding='utf-8'))
    qr = qrencoder.QRCode(10, qrencoder.QRErrorCorrectLevel.L)
    qr.addData(qrencoder.QR8bitByte(fixture['text'].encode('utf-8')))
    qr.makeImpl(False, 0)
    expected = [''.join('1' if cell else '0' for cell in row) for row in qr.modules]
    assert expected == fixture['rows'], f'QR matrix mismatch: {file.name}'
    print(f'PASS {file.name}: all 3,249 modules match the independent encoder')
