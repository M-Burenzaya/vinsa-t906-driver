"""Extract digest-checked resource payloads from retained REA resource evidence.

No executable is run; each exported file is copied from its source-bound range.
"""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
TARGETS = {"tablet-service": "TabletService.exe", "tstart": "TStart.exe", "tablet-com": "TabletCom_vc.dll"}

def main():
    facts = json.loads((ROOT / "artifacts/mytablet/architecture/package-facts.json").read_text(encoding="utf-8-sig"))
    for folder, filename in TARGETS.items():
        evidence = json.loads((ROOT / f"artifacts/mytablet/{folder}-resources.json").read_text(encoding="utf-8-sig"))
        payload = evidence["normalized_result"]
        source = ROOT / "downloads/windows/installed/MyTablet" / filename
        data = source.read_bytes()
        assert hashlib.sha256(data).hexdigest() == payload["artifact"]["sha256"]
        output = ROOT / "artifacts/mytablet" / folder
        resource_dir = output / "resources"
        resource_dir.mkdir(parents=True, exist_ok=True)
        manifest = []
        for item in payload["resources"]:
            location = item["payload"]["location"]
            content = data[location["offset"]:location["offset"] + location["bytes"]]
            assert hashlib.sha256(content).hexdigest() == item["payload"]["sha256"]
            export_name = f"resource-{item['index']:03d}.bin"
            with (resource_dir / export_name).open("xb") as stream:
                stream.write(content)
            manifest.append({**item, "file": export_name, "binary_dfm": content.startswith(b"TPF0")})
        (resource_dir / "manifest.json").write_text(json.dumps({"evidence_id": evidence["evidence_id"], "source_sha256": payload["artifact"]["sha256"], "resources": manifest}, indent=2), encoding="utf-8")
        identity = next(image for image in facts["images"] if Path(image["path"]).name == filename)
        (output / "pe-and-methods.json").write_text(json.dumps(identity, indent=2), encoding="utf-8")
        (output / "README.md").write_text(f"# {filename} static analysis\n\nSource SHA-256: `{payload['artifact']['sha256']}`.\n\n`pseudocode/*.c` contains generated Ghidra pseudocode, **not original source or buildable C**. `assembly/*.asm` covers the complete discovered function body ranges. `functions.json` records per-function status, ranges, signatures and static call targets; `summary.json` records coverage/failures. Autoanalysis may miss code and recover incorrect types/boundaries.\n\n`resources/` contains every payload from the complete REA PE resource inventory, with original offsets, names, hashes and evidence reference in its manifest. Binary Delphi forms are tagged `binary_dfm`. `pe-and-methods.json` records package identity and candidate Delphi method descriptors from a separate byte parser. No vendor program or hardware command was executed.\n", encoding="utf-8")
        print(filename, "resources", len(manifest), "binary_forms", sum(item["binary_dfm"] for item in manifest))

if __name__ == "__main__":
    main()
