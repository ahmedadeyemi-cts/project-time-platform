#!/usr/bin/env python3
from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = root / "src/backend/ProjectTime.Api/Program.cs"
text = path.read_text(encoding="utf-8")

legacy = '    var storedFileName = $"{documentType}_{Guid.NewGuid():N}_{originalFileName}";'
safe = '    var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}"; // SECURITY_20260729_SAFE_PROJECT_DOCUMENT_PATH_COMPONENT'

count = text.count(legacy)
if count > 1:
    raise RuntimeError(f"project document storage path: expected at most one legacy match, found {count}")
if count == 1:
    text = text.replace(legacy, safe, 1)

if legacy in text:
    raise RuntimeError("project document storage path still contains the legacy document-type filename prefix")
if "SECURITY_20260729_SAFE_PROJECT_DOCUMENT_PATH_COMPONENT" not in text:
    raise RuntimeError("project document storage path safety marker is missing")

path.write_text(text, encoding="utf-8")
print("SECURITY_SAFE_PROJECT_DOCUMENT_STORAGE_FINALIZER=PASSED")
