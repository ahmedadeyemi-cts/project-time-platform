#!/usr/bin/env python3
"""Enable bounded legacy .doc extraction in the generated compiler copy.

The canonical extractor stays reviewable and the existing build continues to
apply its ClosedXML compatibility substitutions first. This post-processor then
makes four exact, fail-closed edits to the generated source:

1. admit .doc only inside the extractor safety assessment;
2. recognize the OLE compound-file signature used by binary Word documents;
3. route .doc to the private antiword text-only adapter; and
4. require the matching legacy Word signature.
"""

from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source anchor, found {count}")
    return text.replace(old, new, 1)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--path", required=True)
    args = parser.parse_args()
    path = Path(args.path)
    text = path.read_text(encoding="utf-8")

    text = replace_once(
        text,
        '                ".docx" => ExtractDocx(source, options, safety, cancellationToken),\n',
        '                ".doc" => await PulseAiLegacyBinaryWordExtraction.ExtractAsync(source, options, safety, cancellationToken),\n'
        '                ".docx" => ExtractDocx(source, options, safety, cancellationToken),\n',
        "legacy Word extraction switch",
    )

    text = replace_once(
        text,
        '        var extensionAllowed = PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions\n'
        '            .Contains(extension, StringComparer.OrdinalIgnoreCase);\n',
        '        var extensionAllowed = PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions\n'
        '            .Contains(extension, StringComparer.OrdinalIgnoreCase)\n'
        '            || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase);\n',
        "legacy Word extension admission",
    )

    text = replace_once(
        text,
        '        if (header.Any(value => value == 0)) return "binary_unknown";\n',
        '        if (header.Length >= 8\n'
        '            && header[0] == 0xD0\n'
        '            && header[1] == 0xCF\n'
        '            && header[2] == 0x11\n'
        '            && header[3] == 0xE0\n'
        '            && header[4] == 0xA1\n'
        '            && header[5] == 0xB1\n'
        '            && header[6] == 0x1A\n'
        '            && header[7] == 0xE1) return "ole_compound_word";\n'
        '        if (header.Any(value => value == 0)) return "binary_unknown";\n',
        "legacy Word OLE signature",
    )

    text = replace_once(
        text,
        '        ".docx" or ".pptx" or ".xlsx" => detected == "zip_openxml",\n',
        '        ".doc" => detected == "ole_compound_word",\n'
        '        ".docx" or ".pptx" or ".xlsx" => detected == "zip_openxml",\n',
        "legacy Word signature match",
    )

    required = [
        '".doc" => await PulseAiLegacyBinaryWordExtraction.ExtractAsync',
        'extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)',
        'return "ole_compound_word"',
        '".doc" => detected == "ole_compound_word"',
    ]
    missing = [marker for marker in required if marker not in text]
    if missing:
        raise SystemExit(f"legacy Word generated-source verification failed: {missing}")

    path.write_text(text, encoding="utf-8")


if __name__ == "__main__":
    main()
