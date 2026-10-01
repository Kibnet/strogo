"""Independent raw-file comparison of two retained validation-only runs."""
import hashlib
import json
import sys
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def build_root(case):
    roots = list((case / "compiled").iterdir())
    if len(roots) != 1 or not roots[0].is_dir():
        raise ValueError("Expected one retained clean build directory")
    return roots[0]


def verify_inventory(root, receipt):
    entries = receipt["publishInventory"]
    expected = {entry["name"] for entry in entries}
    paths = list((root / "publish").iterdir())
    actual = {path.name.lower() for path in paths}
    if len(entries) != 190 or len(expected) != 190 or len(paths) != 190 or expected != actual:
        raise ValueError("Publish inventory membership mismatch")
    for entry in entries:
        path = root / "publish" / entry["name"]
        if not path.is_file() or path.stat().st_size != int(entry["length"]) or digest(path) != entry["sha256"]:
            raise ValueError("Publish file mismatch: " + entry["name"])
    for filename, field in [("Generated.cs", "generatedSourceSha256"),
                            ("Generated.csproj", "projectSha256")]:
        if digest(root / filename) != receipt[field]:
            raise ValueError("Build source hash mismatch: " + filename)
    if digest(root / "publish" / "strogo.generated.dll") != receipt["assemblySha256"]:
        raise ValueError("Entry assembly binding mismatch")


left, right, output = map(Path, sys.argv[1:])
if left.resolve() == right.resolve():
    raise ValueError("Two distinct clean input roots required")
rows = []
for name in ["scalar-primary", "scalar-alternative", "allocation-primary", "allocation-alternative"]:
    a, b = left / name, right / name
    ba, bb = build_root(a), build_root(b)
    if ba.resolve() == bb.resolve():
        raise ValueError("Two distinct retained build roots required")
    ra, rb = load(ba / "receipt.json"), load(bb / "receipt.json")
    verify_inventory(ba, ra)
    verify_inventory(bb, rb)
    files = ["candidate.dfy", "source-map.json", "transcript.json", "obligations.json", "invocations.json"]
    equal = {file: digest(a / file) == digest(b / file) for file in files}
    equal.update({file: digest(ba / file) == digest(bb / file) for file in ["Generated.cs", "Generated.csproj", "receipt.json"]})
    equal["all190PublishFiles"] = ra["publishInventory"] == rb["publishInventory"]
    for case in (a, b):
        for phase in ("proof", "translation"):
            if int(load(case / phase / "process.json")["exitCode"]) != 0:
                raise ValueError("Unsuccessful proof/translation")
    if digest(a / "transcript.json") != ra["transcriptSha256"] or digest(b / "transcript.json") != rb["transcriptSha256"]:
        raise ValueError("Transcript binding mismatch")
    rows.append({"id": name, "equal": equal, "assemblySha256": rb["assemblySha256"],
                 "publishInventoryDigest": rb["publishInventoryDigest"]})
report_equal = digest(left / "report.json") == digest(right / "report.json")
passed = report_equal and all(all(row["equal"].values()) for row in rows)
report = {"purpose": "validation-only-no-human-admission", "leftRoot": str(left),
          "rightRoot": str(right), "publishFilesIndependentlyChecked": 1520,
          "rootReportByteEqual": report_equal, "rows": rows, "passed": passed,
          "boundaries": "No public closure formula, approval, native execution or benchmark claim. Timing/path logs excluded; no canonical inventory digest recomputation."}
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": passed, "cases": len(rows), "publishFiles": 1520}))
sys.exit(0 if passed else 1)
