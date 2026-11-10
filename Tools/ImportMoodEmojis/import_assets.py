"""Fetch pinned Telegram emoji mirrors; never execute upstream code.

Assets are registered with EF by ClientMoodTypesInitializeOperation, not by this
download tool. Run render_previews.ps1 after this script, then validate_assets.py.
"""
from __future__ import annotations

import base64
import gzip
import hashlib
import json
from pathlib import Path
import time
import unicodedata
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "Mizon.Server.API" / "App_Data" / "MoodEmojis"
CACHE = ROOT / "Mizon.Server.API" / "obj" / "MoodEmojiImport"
TELEMOJI = "4f45fca016c71de9d2a9e15c07ae1fe3d8327a28"
EFFECTS = "7b68fd20784d297a812f1345db48f28e3934b6e1"
CLDR = "7d2b9cb11cec08f4d333ad73209deb3df0711c3b"


def fetch(url: str, path: Path) -> bytes:
    if path.exists():
        return path.read_bytes()
    for attempt in range(4):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "Mizon-Mood-Asset-Import"})
            with urllib.request.urlopen(request, timeout=120) as response:
                data = response.read()
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            return data
        except Exception:
            if attempt == 3:
                raise
            time.sleep(attempt + 1)
    raise AssertionError("unreachable")


def archive(repo: str, revision: str, name: str) -> zipfile.ZipFile:
    path = CACHE / f"{name}.zip"
    fetch(f"https://codeload.github.com/{repo}/zip/{revision}", path)
    return zipfile.ZipFile(path)


def archive_file(archive: zipfile.ZipFile, relative: str) -> bytes:
    # Read only an explicitly selected entry. No archive entry is ever extracted.
    prefix = archive.namelist()[0].split("/", 1)[0]
    entry = archive.getinfo(f"{prefix}/{relative}")
    if entry.file_size > 12 * 1024 * 1024:
        raise ValueError(f"Unexpectedly large source file: {relative}")
    return archive.read(entry)


def annotations(language: str) -> dict[str, dict]:
    output: dict[str, dict] = {}
    for kind in ("annotations", "annotationsDerived"):
        package = "cldr-annotations-full" if kind == "annotations" else "cldr-annotations-derived-full"
        relative = f"cldr-json/{package}/{kind}/{language}/annotations.json"
        url = f"https://api.github.com/repos/unicode-org/cldr-json/contents/{relative}?ref={CLDR}"
        cached = CACHE / f"cldr-{language}-{kind}.json"
        if cached.exists():
            data = json.loads(cached.read_text(encoding="utf-8"))
        else:
            content = json.loads(fetch(url, CACHE / f"cldr-{language}-{kind}-response.json"))
            data = json.loads(base64.b64decode(content["content"]))
            cached.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        values = next(iter(data.values()))["annotations"]
        output.update(values)
    return output


def normalized(emoji: str) -> str:
    # Lookup aliases only; original Unicode sequences remain unchanged in catalog.
    return emoji.replace("\ufe0f", "").replace("\ufe0e", "")


def name_for(emoji: str, values: dict, fallback: str | None = None) -> str | None:
    match = values.get(emoji) or values.get(normalized(emoji))
    if match and match.get("tts"):
        return match["tts"][0]
    return fallback


def write_animation(data: bytes, relative: str) -> dict:
    if not data.startswith(b"\x1f\x8b"):
        raise ValueError(f"Source is not gzip-compressed TGS: {relative}")
    decoded = gzip.decompress(data)
    animation = json.loads(decoded)
    if not isinstance(animation.get("layers"), list) or animation.get("fr", 0) <= 0:
        raise ValueError(f"Invalid Lottie document: {relative}")
    output = DEST / relative
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(decoded)
    original = DEST / "originals" / Path(relative).with_suffix(".tgs")
    original.parent.mkdir(parents=True, exist_ok=True)
    original.write_bytes(data)
    return {
        "path": relative,
        "originalPath": original.relative_to(DEST).as_posix(),
        "sha256": hashlib.sha256(decoded).hexdigest(),
        "originalSha256": hashlib.sha256(data).hexdigest(),
        "size": len(decoded),
        "width": animation.get("w"),
        "height": animation.get("h"),
        "frameRate": animation["fr"],
        "durationSeconds": (animation.get("op", 0) - animation.get("ip", 0)) / animation["fr"],
    }


def main() -> None:
    DEST.mkdir(parents=True, exist_ok=True)
    CACHE.mkdir(parents=True, exist_ok=True)
    for folder in ("animations", "effects", "previews", "originals"):
        (DEST / folder).mkdir(exist_ok=True)
    print("Reading pinned source archives...", flush=True)
    with archive("saeedtahmtan/telemoji", TELEMOJI, "telemoji") as loops, archive(
        "ilyhalight/telegram-emoji-effects", EFFECTS, "effects"
    ) as effects:
        entries = json.loads(archive_file(loops, "emojis.json"))
        effect_manifest = json.loads(archive_file(effects, "manifest.json"))
        english, persian = annotations("en"), annotations("fa")
        selected: dict[str, dict] = {}
        for item in entries:
            if item["type"] != "animated":
                continue
            previous = selected.get(item["emoji"])
            if previous is None or item.get("variant", 1) < previous.get("variant", 1):
                selected[item["emoji"]] = item

        # Preserve the real emoji-to-effect mapping; 1-keycap is Telegram's metadata
        # bucket containing ALL effects and must not become a user-facing mapping.
        effect_lookup = {
            normalized(emoji): variants for emoji, variants in effect_manifest.items()
            if normalized(emoji) != "1\u20e3"
        }
        file_info = []
        effect_files: dict[str, str] = {}
        items = []
        for index, (emoji, entry) in enumerate(selected.items(), 1):
            code = "U_" + "_".join(f"{ord(char):X}" for char in emoji)
            animation_path = f"animations/{code}.json"
            file_info.append(write_animation(archive_file(loops, entry["file_tgs"]), animation_path))
            paths = []
            for variant in effect_lookup.get(normalized(emoji), []):
                document_id = variant["id"]
                if document_id not in effect_files:
                    relative = f"effects/{document_id}.json"
                    file_info.append(write_animation(archive_file(effects, variant["file"]["tgs"]), relative))
                    effect_files[document_id] = f"originals/{Path(relative).with_suffix('.tgs').as_posix()}"
                if effect_files[document_id] not in paths:
                    paths.append(effect_files[document_id])
            fallback = " ".join(unicodedata.name(char, "") for char in emoji if char not in "\ufe0f\ufe0e\u200d").lower()
            title = name_for(emoji, english, fallback)
            items.append({
                "emoji": emoji,
                "title": title,
                "persianTitle": name_for(emoji, persian),
                "animationPath": f"originals/{Path(animation_path).with_suffix('.tgs').as_posix()}",
                "previewPath": f"previews/{code}.png",
                "effectPaths": paths,
            })
            if index % 100 == 0:
                print(f"Prepared {index}/{len(selected)} emoji animations", flush=True)

    source_version = f"telemoji@{TELEMOJI[:12]}+effects@{EFFECTS[:12]}-tgs1"
    catalog = {"sourceVersion": source_version, "items": items}
    (DEST / "catalog.pending.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding="utf-8")
    provenance = {
        "sources": [
            {"repository": "https://github.com/saeedtahmtan/telemoji", "commit": TELEMOJI, "lastPush": "2026-06-22T06:58:54Z", "use": "ordinary animated emoji"},
            {"repository": "https://github.com/ilyhalight/telegram-emoji-effects", "commit": EFFECTS, "lastPush": "2026-09-12T12:18:41Z", "use": "interactive overlay effects"},
            {"repository": "https://github.com/unicode-org/cldr-json", "commit": CLDR, "use": "English and Persian accessible names"},
        ],
        "emojiCount": len(items),
        "emojiWithEffects": sum(bool(item["effectPaths"]) for item in items),
        "uniqueEffectFiles": len(effect_files),
        "files": file_info,
    }
    (DEST / "provenance.json").write_text(json.dumps(provenance, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in provenance.items() if key != "files"}, ensure_ascii=True), flush=True)


if __name__ == "__main__":
    main()
