"""Recognize exact reviewed controllers for the known zero-job orphan."""
import subprocess
import hashlib
import argparse

BASE = "245b0915d895d83f1ceaed32460ad95a4a3d79be"
CONTROLLER = ".github/workflows/projectpulse-deploy-test.yml"
REPLACEMENTS = (
    (b"        timeout-minutes: 35", b"        timeout-minutes: 50"),
    (b"MODULE025_GENERATION_TIMEOUT_SECONDS: '1500'", b"MODULE025_GENERATION_TIMEOUT_SECONDS: '2520'"),
)


# PR #1106 changes acceptance only. The exact-main pre-Azure guard remains
# byte-identical to the stale run's controller. No arbitrary future controller
# receives this exception; the supervisor still verifies run identity, zero jobs,
# unchanged timestamps, ancestry and absence of pending environment approvals.
ORIGINAL_SHA256 = "5e20ba22c8f135393172b8110802094f7bfd742b54b0fda19df4ec7c51a2bfb7"
EXPORT_SHA256 = "276d23806215df92246a8b37eb7291f55f1dd1b7938ec38cdaff45570bebd2c3"

# PR #1116 adds only migrations 112–114 and their verification to the export
# controller. The pre-Azure exact-main guard is byte-identical. This exact
# digest is approved for recovery; future controller edits remain rejected.
ENTERPRISE_SHA256 = "3cbd088ab607eb3f3399ab254332adb818c946c7287c33e7a4a52f7e04a3f179"

# PR1158 packages migration125 with its source receipt. Its pre-Azure exact-main
# guard is unchanged. Only this complete controller content is recognized;
# run identity, zero jobs, ancestry and native Test protection still apply.
DOCUMENT_ADMISSION_SHA256 = "b886247b63b313b2038202461a1f370b72b93bd03823edbd8d74cf3ce679728a"


# Exact PR1192 controller; no other future controller inherits this registration.
NORMAL_SA_SHA256 = "53e9ae43fb8eeee5efefdc28e56c6ac3c90883b815cb65288d7b948a38837d7e"


def permitted(original, current):
    # Exact document-runtime prerequisite controller; all prior admission and UAT gates remain.
    if hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256, NORMAL_SA_SHA256, "6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff", "11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3", "6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62") and hashlib.sha256(current).hexdigest() == "941c13231b28d0fa40e0e72af02a079c31f4a6b6a78c27e2af156bf93ee2eae6":
        return True
    # Exact builder-path repair; no other controller bytes inherit this exception.
    if hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256, NORMAL_SA_SHA256, "6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff", "11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3", "6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62", "941c13231b28d0fa40e0e72af02a079c31f4a6b6a78c27e2af156bf93ee2eae6") and hashlib.sha256(current).hexdigest() == "b1583d34382fc774c9b40ac5b145f2a5f7154ab06e48203310a1f402e262fdef":
        return True
    # Exact stale-SOW maintenance controller; unknown future bytes remain rejected.
    if hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256, NORMAL_SA_SHA256, "6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff", "11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3", "6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62", "941c13231b28d0fa40e0e72af02a079c31f4a6b6a78c27e2af156bf93ee2eae6", "b1583d34382fc774c9b40ac5b145f2a5f7154ab06e48203310a1f402e262fdef") and hashlib.sha256(current).hexdigest() == "267e6105d955f372617ac9376ff5bb4ae16e6089fb0490ea6feaead7d3b4b71b":
        return True
    # Exact reviewed reordered controller; every existing UAT gate remains mandatory.
    if hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256, NORMAL_SA_SHA256, "6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff", "11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3") and hashlib.sha256(current).hexdigest() == "6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62":
        return True
    # PR1229 adds one exact service phase after the previous full acceptance.
    # Unknown old bases or any different new bytes still fail quarantine admission.
    if hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256, NORMAL_SA_SHA256, "6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff") and hashlib.sha256(current).hexdigest() == "11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3":
        return True
    # Exact secured controller only; unknown bases and future controller edits fail.
    if (hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256, NORMAL_SA_SHA256)
            and hashlib.sha256(current).hexdigest() == "6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff"):
        return True
    if (hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256, DOCUMENT_ADMISSION_SHA256)
            and hashlib.sha256(current).hexdigest() == NORMAL_SA_SHA256):
        return True
    if (hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256, ENTERPRISE_SHA256)
            and hashlib.sha256(current).hexdigest() == DOCUMENT_ADMISSION_SHA256):
        return True
    if (hashlib.sha256(original).hexdigest() in (ORIGINAL_SHA256, EXPORT_SHA256)
            and hashlib.sha256(current).hexdigest() == ENTERPRISE_SHA256):
        return True
    if (hashlib.sha256(original).hexdigest() == ORIGINAL_SHA256
            and hashlib.sha256(current).hexdigest() == EXPORT_SHA256):
        return True
    reviewed = original
    for old, new in REPLACEMENTS:
        if reviewed.count(old) != 1:
            return False
        reviewed = reviewed.replace(old, new, 1)
    return current in (original, reviewed)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", choices=(BASE, "57c8d0264bdd828e6b3b53a8c5cb8b1b841e8f61", "c15ef12d5ce1bc54c15d8b31c87a50daa94bad17", "045b66ca01baa68b2f5b3f6eb9e063c23c335981"), default=BASE)
    args = parser.parse_args()
    original = subprocess.check_output(["git", "show", args.base + ":" + CONTROLLER])
    current = subprocess.check_output(["git", "show", "HEAD:" + CONTROLLER])
    if not permitted(original, current):
        raise SystemExit("Deployment controller is not an exact reviewed timing, export, or enterprise migration controller")
    print("MODULE025_QUARANTINE_CONTROLLER=PASS")


if __name__ == "__main__":
    main()
