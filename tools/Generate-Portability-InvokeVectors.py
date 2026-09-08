#!/usr/bin/env python3
import argparse
import json
from pathlib import Path


def wire(value):
    declared = value["type"]
    payload = value["value"]
    if declared == "I64":
        return {"kind": "i64", "value": payload}
    if declared == "Bool":
        return {"kind": "bool", "value": payload}
    if isinstance(declared, dict) and declared["kind"] == "Seq":
        return {"kind": "sequence", "items": [wire(item) for item in payload]}
    fields = sorted(payload, key=lambda item: item["fieldId"])
    return {"kind": "record", "typeId": declared, "fields": [{"fieldId": item["fieldId"], "value": wire(item["value"])} for item in fields]}


def compact(value):
    return json.dumps(value, ensure_ascii=True, separators=(",", ":"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    source = json.loads(Path(args.input).read_text(encoding="utf-8"))
    rows = []
    for item in source["valid"]:
        request = {"schema": "strogo.invoke.v0.1", "functionId": item["functionId"], "arguments": [wire(value) for value in item["arguments"]]}
        expected = {"schema": "strogo.invoke-result.v0.1", "kind": "success", "value": wire(item["expected"])}
        rows.append({"id": item["id"], "request": compact(request), "expected": compact(expected)})
    for item in source["ownerRefusals"]:
        request = {"schema": "strogo.invoke.v0.1", "functionId": item["functionId"], "arguments": [wire(value) for value in item["arguments"]]}
        expected = {"schema": "strogo.invoke-result.v0.1", "kind": "refusal", "code": item["code"], "locus": item["locus"], "details": {}}
        rows.append({"id": item["id"], "request": compact(request), "expected": compact(expected)})
    data = "".join(compact(row) + "\n" for row in rows)
    Path(args.output).write_text(data, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
