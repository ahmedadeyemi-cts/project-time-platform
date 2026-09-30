"""Isolated, bounded raster/PDF OCR. Only server-created paths reach this process."""
from __future__ import annotations
import json
import os
import re
import resource
import subprocess
import sys
import time
import warnings
from pathlib import Path

MAX_TEXT = 1_000_000
MAX_PIXELS = 16_777_216
MAX_PAGES = 50

def limits():
    resource.setrlimit(resource.RLIMIT_AS, (768 * 1024 * 1024, 768 * 1024 * 1024))
    resource.setrlimit(resource.RLIMIT_FSIZE, (64 * 1024 * 1024, 64 * 1024 * 1024))
    resource.setrlimit(resource.RLIMIT_CPU, (120, 120))
    resource.setrlimit(resource.RLIMIT_NOFILE, (64, 64))
    os.umask(0o077)

def execute(argv: list[str], deadline: float, directory: Path, budget: int = 60) -> str:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise ValueError("ocr_deadline_exceeded")
    output = directory / "tool-output.txt"
    with output.open("wb") as handle:
        result = subprocess.run(argv, stdout=handle, stderr=subprocess.DEVNULL,
                                timeout=min(remaining, budget), check=False)
    if result.returncode or output.stat().st_size > MAX_TEXT:
        raise ValueError("ocr_tool_rejected")
    text = output.read_text(encoding="utf-8", errors="replace")
    output.unlink()
    return text

def raster_text(source: Path, directory: Path, deadline: float) -> str:
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = MAX_PIXELS
    normalized = directory / "normalized.png"
    with warnings.catch_warnings():
        warnings.simplefilter("error", Image.DecompressionBombWarning)
        with Image.open(source) as image:
            if image.format not in {"PNG", "JPEG", "TIFF", "BMP", "WEBP"}:
                raise ValueError("unsupported_raster")
            if getattr(image, "n_frames", 1) != 1:
                raise ValueError("multi_frame_raster_requires_explicit_support")
            width, height = image.size
            if min(width, height) < 1 or width * height > MAX_PIXELS:
                raise ValueError("raster_dimensions_exceeded")
            image.load()
            image.convert("RGB").save(normalized, format="PNG")
    try:
        return execute(["/usr/bin/tesseract", str(normalized), "stdout", "-l", "eng", "--psm", "3"],
                       deadline, directory).replace("\x00", "").strip()
    finally:
        normalized.unlink(missing_ok=True)

def extract(source: Path, output: Path):
    limits()
    deadline = time.monotonic() + 170
    directory = output.parent
    with source.open("rb") as handle:
        pdf = handle.read(5) == b"%PDF-"
    pages = []
    if pdf:
        info = execute(["/usr/bin/pdfinfo", str(source)], deadline, directory, 15)
        if re.search(r"(?mi)^Encrypted:\s+yes\b", info):
            raise ValueError("encrypted_document")
        count = re.search(r"(?m)^Pages:\s*(\d+)\s*$", info)
        if not count or not 1 <= int(count[1]) <= MAX_PAGES:
            raise ValueError("page_limit_exceeded")
        for number in range(1, int(count[1]) + 1):
            prefix = directory / "page"
            execute(["/usr/bin/pdftoppm", "-f", str(number), "-l", str(number), "-singlefile",
                     "-scale-to", "4096", "-png", str(source), str(prefix)], deadline, directory, 30)
            rendered = directory / "page.png"
            text = raster_text(rendered, directory, deadline)
            rendered.unlink()
            pages.append({"pageNumber": number, "text": text})
            if sum(len(page["text"]) for page in pages) > MAX_TEXT:
                raise ValueError("ocr_text_limit_exceeded")
    else:
        pages = [{"pageNumber": 1, "text": raster_text(source, directory, deadline)}]
    if not any(page["text"] for page in pages):
        raise ValueError("ocr_text_unavailable")
    output.write_text(json.dumps({"pages": pages}, ensure_ascii=False), encoding="utf-8")

if __name__ == "__main__":
    try:
        if len(sys.argv) != 3:
            raise ValueError("invalid_invocation")
        extract(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception:
        sys.exit(65)
