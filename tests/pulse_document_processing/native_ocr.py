"""Real Tesseract/Poppler on generated documents only; no network."""
import tempfile
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from clamd_client import BoundaryError
from gateway import run_ocr
with tempfile.TemporaryDirectory() as folder:
    root = Path(folder); image = Image.new("RGB", (1600, 300), "white")
    ImageDraw.Draw(image).text((40, 80), "Pulse document test 12345",
        font=ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 64), fill="black")
    for kind in ("PNG", "PDF"):
        with tempfile.TemporaryDirectory(dir=root) as case:
            directory = Path(case); source = directory / "source.upload"; image.save(source, format=kind)
            payload = run_ocr(source, directory)
            assert "Pulse document test 12345" in payload["pages"][0]["text"], kind
    for rejected in (b"/etc/shadow\n", b"<svg xmlns='http://www.w3.org/2000/svg'></svg>", b"%PDF-invalid"):
        with tempfile.TemporaryDirectory(dir=root) as case:
            directory = Path(case); source = directory / "source.upload"; source.write_bytes(rejected)
            try: run_ocr(source, directory)
            except BoundaryError: pass
            else: raise AssertionError("Rejected input reached successful OCR")
print("NATIVE_OCR=PASS normal_png=true normal_pdf=true rejected_inputs=3 synthetic_only=true")
