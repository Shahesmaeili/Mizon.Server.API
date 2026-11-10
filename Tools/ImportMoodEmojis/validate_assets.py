"""Validate every imported media asset, then atomically publish catalog.json."""
import hashlib
import gzip
import json
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[2] / "Mizon.Server.API" / "App_Data" / "MoodEmojis"
pending = root / "catalog.pending.json"
source = pending if pending.exists() else root / "catalog.json"
catalog = json.loads(source.read_text(encoding="utf-8"))
items = catalog["items"]
assert len(items) == len({item["emoji"] for item in items}), "Duplicate Unicode sequence"
errors = []
previews = []
for item in items:
    for relative in (item["animationPath"], *item["effectPaths"]):
        path = (root / relative).resolve()
        assert path.is_relative_to(root.resolve())
        content = path.read_bytes()
        if path.suffix == '.tgs':
            assert content.startswith(b'\x1f\x8b'), 'Expected gzip-compressed Telegram animation'
            content = gzip.decompress(content)
            assert len(content) <= 8 * 1024 * 1024, 'Animation exceeds client decompression budget'
        data = json.loads(content)
        assert data["layers"] and data["op"] > data.get("ip", 0)
    path = root / item["previewPath"]
    with Image.open(path) as image:
        assert image.format == "PNG" and image.size == (96, 96)
        bounds = image.convert("RGBA").getchannel("A").getbbox()
        if bounds is None:
            errors.append({"emoji": item["emoji"], "path": item["previewPath"]})
    content = path.read_bytes()
    previews.append({"path": item["previewPath"], "size": len(content), "sha256": hashlib.sha256(content).hexdigest()})
if errors:
    (root / "empty-previews.json").write_text(json.dumps(errors, ensure_ascii=False, indent=2), encoding="utf-8")
    raise ValueError(f"{len(errors)} previews have no visible pixels")
provenance_path = root / "provenance.json"
provenance = json.loads(provenance_path.read_text(encoding="utf-8"))
provenance["previews"] = previews
provenance_path.write_text(json.dumps(provenance, ensure_ascii=False, indent=2), encoding="utf-8")
if pending.exists():
    pending.replace(root / "catalog.json")
print(json.dumps({"validatedEmojis": len(items), "previews": len(previews), "previewBytes": sum(p["size"] for p in previews), "published": True}))
