#!/usr/bin/env python3
"""Enable bounded legacy .doc extraction in the generated compiler copy.

The canonical extractor stays reviewable and the existing build continues to
apply its ClosedXML compatibility substitutions first. This post-processor then
makes exact, fail-closed edits to the generated source so legacy Word documents
remain private and non-executable while supporting both real OLE Word files and
the text-compatible .doc files already present in Work Register.

The generated extractor:
1. admits .doc only inside the existing extractor safety assessment;
2. recognizes the OLE compound-file signature used by binary Word documents;
3. recognizes bounded non-binary .doc content as legacy text/HTML/RTF;
4. routes every admitted .doc through the single private legacy Word adapter;
5. lets that adapter use antiword only for real OLE content and in-process
   bounded parsing for text-compatible content; and
6. requires a matching legacy Word/text-compatible signature before parsing.
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

    # Read enough of the source to reject mislabeled binary payloads rather than
    # deciding text compatibility from only the first dozen bytes.
    text = replace_once(
        text,
        '        var header = new byte[12];\n',
        '        var header = new byte[256];\n',
        "legacy Word bounded signature probe",
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
        '        if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))\n'
        '        {\n'
        '            var legacyPrefix = Encoding.ASCII.GetString(header).TrimStart();\n'
        '            if (legacyPrefix.StartsWith("{\\\\rtf", StringComparison.OrdinalIgnoreCase)) return "legacy_doc_rtf";\n'
        '            if (legacyPrefix.StartsWith("<", StringComparison.Ordinal)) return "legacy_doc_html";\n'
        '            if (!header.Any(value => value == 0)) return "legacy_doc_text";\n'
        '        }\n'
        '        if (header.Any(value => value == 0)) return "binary_unknown";\n',
        "legacy Word OLE and text-compatible signatures",
    )

    text = replace_once(
        text,
        '        ".docx" or ".pptx" or ".xlsx" => detected == "zip_openxml",\n',
        '        ".doc" => detected is "ole_compound_word" or "legacy_doc_text" or "legacy_doc_html" or "legacy_doc_rtf",\n'
        '        ".docx" or ".pptx" or ".xlsx" => detected == "zip_openxml",\n',
        "legacy Word signature match",
    )

    required = [
        '".doc" => await PulseAiLegacyBinaryWordExtraction.ExtractAsync',
        'extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)',
        'var header = new byte[256]',
        'return "ole_compound_word"',
        'return "legacy_doc_text"',
        'return "legacy_doc_html"',
        'return "legacy_doc_rtf"',
        '".doc" => detected is "ole_compound_word" or "legacy_doc_text" or "legacy_doc_html" or "legacy_doc_rtf"',
    ]
    missing = [marker for marker in required if marker not in text]
    if missing:
        raise SystemExit(f"legacy Word generated-source verification failed: {missing}")

    path.write_text(text, encoding="utf-8")


if __name__ == "__main__":
    main()
