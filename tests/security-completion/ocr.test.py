"""Exercise the production OCR boundary without starting the gateway."""
import ast
from contextlib import contextmanager
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from PIL import Image, PngImagePlugin

source = Path(__file__).resolve().parents[2] / 'deployment/oracle-celar/gateway/gateway.py'
module = ast.parse(source.read_text())
helper = next(node for node in module.body if isinstance(node, ast.FunctionDef) and node.name == '_safe_ocr_image')
namespace = dict(contextmanager=contextmanager, Path=Path, tempfile=tempfile, Image=Image)
exec(compile(ast.Module(body=[helper], type_ignores=[]), str(source), 'exec'), namespace)
normalize = namespace['_safe_ocr_image']

class OcrBoundary(unittest.TestCase):
    def test_image_list_is_rejected_before_tesseract(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'upload.png'
            path.write_text('/private/local/image.png\n')
            with self.assertRaises(Exception):
                with normalize(path):
                    self.fail('A list must not reach OCR')

    def test_raster_is_reencoded_and_metadata_removed(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'original.png'
            metadata = PngImagePlugin.PngInfo()
            metadata.add_text('description', '/private/local/image.png')
            Image.new('RGB', (2, 2), 'red').save(path, pnginfo=metadata)
            with normalize(path) as clean:
                self.assertNotEqual(path, clean)
                with Image.open(clean) as decoded:
                    self.assertEqual(decoded.getpixel((0, 0)), (255, 0, 0))
                    self.assertFalse(decoded.info)
            self.assertFalse(clean.exists())

    def test_large_dimensions_rejected_without_decode(self):
        class Oversized:
            format = 'PNG'
            size = (12001, 1)
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def convert(self, *args): raise AssertionError('Decode must not start')
        with patch.object(Image, 'open', return_value=Oversized()):
            with self.assertRaisesRegex(ValueError, 'image_dimensions_rejected'):
                with normalize(Path('unused')): pass

    def test_svg_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'upload.svg'
            path.write_text('<svg xmlns="http://www.w3.org/2000/svg"><image href="file:///private/image"/></svg>')
            with self.assertRaises(Exception):
                with normalize(path): self.fail('SVG must not reach OCR')

if __name__ == '__main__': unittest.main()
