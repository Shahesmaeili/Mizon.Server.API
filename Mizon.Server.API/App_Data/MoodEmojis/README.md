# Mizon mood emoji assets

This directory contains data-only copies of Telegram emoji animations. No upstream
download or conversion scripts are executed. The application reads `catalog.json`
through its business-layer initialization operation, creates Media relationships,
and serves the resulting media IDs through the existing media API and cache.

## Sources and versions

* Ordinary emoji loops: [saeedtahmtan/telemoji](https://github.com/saeedtahmtan/telemoji),
  commit `4f45fca016c71de9d2a9e15c07ae1fe3d8327a28`, last push June 22, 2026.
* Interactive click overlays: [ilyhalight/telegram-emoji-effects](https://github.com/ilyhalight/telegram-emoji-effects),
  commit `7b68fd20784d297a812f1345db48f28e3934b6e1`, last push September 12, 2026.
* Accessible English/Persian names: [Unicode CLDR](https://github.com/unicode-org/cldr-json),
  commit `7d2b9cb11cec08f4d333ad73209deb3df0711c3b`.

The newer effects repository contains interactive overlays, not the ordinary emoji
loops. Both are therefore needed. These snapshots are pinned for reproducibility;
they are not advertised as a complete live copy of Telegram's current catalog.

`originals/animations/` holds one source TGS per exact Unicode sequence.
`originals/effects/` holds only actual mapped interactive effects, deduplicated by Telegram
document ID. The synthetic `1-keycap` metadata bucket is excluded. Missing effects
are left empty; an unrelated animation is never assigned to an emoji.

`catalog.json` serves these original gzip-compressed TGS files through the media
pipeline. The client expands each downloaded animation once into a bounded local
JSON cache. This reduces the full animation transfer footprint from approximately
183 MiB of JSON to 22 MiB of TGS. Both header loops and effects are downloaded lazily.
The JSON files in `animations/` and `effects/` are retained as their decompressed
contents for offline preview generation and renderer verification, without
redrawing or converting the vector animations. They are not registered as the
downloadable animation media by the current catalog.
`previews/` contains static transparent 96x96 PNG rest-frame thumbnails generated
locally with the existing SkiaSharp.Skottie renderer. They do not animate in a grid
and require no Lottie renderer or Skottie package in the production client.

`provenance.json` records original/decompressed SHA-256 values and preview hashes.

## Rebuilding

The import tools are in `Tools/ImportMoodEmojis`. Run `import_assets.py`, then
`render_previews.ps1`, then `validate_assets.py` with a Python environment providing
Pillow. The renderer uses already-cached SkiaSharp packages. No packages are
installed by these scripts. Validation atomically publishes `catalog.json` after
checking every animation and nonempty preview. Database inserts remain in the
.NET business operation; the asset tools never connect to the database.

## Attribution and licensing

Original animations are Telegram artwork. The mirror repositories explicitly
separate their website/code license from the underlying TGS artwork. Their presence
here does not grant additional redistribution rights. See the source repositories
and [Telegram content licensing](https://telegram.org/tos/content-licensing).

Unicode CLDR names are provided under the
[Unicode License v3](https://www.unicode.org/license.txt), included in
`Unicode-LICENSE.txt`. Copyright Unicode, Inc. and others.
