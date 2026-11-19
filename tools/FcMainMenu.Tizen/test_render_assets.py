import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from PIL import Image

SCRIPT = Path(__file__).with_name('render_assets.py')


class RenderAssetsTest(unittest.TestCase):
    def test_each_menu_uses_supplied_background_and_marks_selection(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            background = root / 'background.png'
            Image.new('RGB', (640, 480), (24, 79, 133)).save(background)
            output = root / 'output'
            subprocess.run([sys.executable, str(SCRIPT), str(background), str(output)], check=True, capture_output=True, text=True)
            images = [Image.open(output / f'menu-{index}.bmp').convert('RGB') for index in (0, 1, 3, 4, 6)]
            for image in images:
                self.assertEqual((640, 480), image.size)
                self.assertEqual((24, 79, 133), image.getpixel((0, 0)))
            self.assertNotEqual(images[0].tobytes(), images[1].tobytes())


if __name__ == '__main__':
    unittest.main()
