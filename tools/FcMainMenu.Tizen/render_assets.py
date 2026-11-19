import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser(description='Render menu states over the reconstructed original background.')
parser.add_argument('background', type=Path, help='Reconstructed 640x480 background image')
parser.add_argument('output', nargs='?', type=Path, default=Path(__file__).parent / 'assets')
args = parser.parse_args()
with Image.open(args.background) as source:
    if source.size != (640, 480):
        parser.error('background must be 640x480')
    background = source.convert('RGB')
out = args.output
out.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf', 15)
small = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf', 12)
entries = [(0, 'SINGLE PLAYER', 72), (1, '2 PLAYER COOPERATIVE', 83), (3, 'SINGLE PLAYER', 124), (4, '2 PLAYER COMPETITIVE', 135), (6, 'LOAD GAME', 176)]
for selected, _, _ in entries:
    image = background.copy()
    draw = ImageDraw.Draw(image)
    def text(value, y, color, face=font):
        draw.text((320, y * 2 + 7), value, anchor='mm', font=face, fill=color)
    for heading, y in [('CRIME WAR', 56), ('PRECINCT ASSAULT', 108), ('MEMORY CARD', 160)]:
        text(heading, y, '#73c5ff')
    for index, label, y in entries:
        text(label, y, '#ffff88' if index == selected else '#ffffff')
    text('UP / DOWN: SCROLL    ENTER: ACCEPT', 218, '#c0c0c0', small)
    image.save(out / f'menu-{selected}.bmp')
    print(f'wrote menu-{selected}.bmp', flush=True)
