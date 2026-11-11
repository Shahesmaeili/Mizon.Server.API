# Music home demo artwork

These photographs illustrate **development fixtures**, not the named song's official
cover or the identity of a story author. User avatars and music covers are separate.
Selected/reviewed on 2026-11-11. The source JPGs below are Wikimedia-provided
thumbnails; their image content has not been retouched or recolored.

| File | Photographer and source | License |
| --- | --- | --- |
| `blue-live-guitar.jpg` | Melanie van Leeuwen / strompictures, 7 May 2015 — [Front row audience](https://commons.wikimedia.org/wiki/File:Front_row_audience_(Unsplash).jpg), [original publication](https://unsplash.com/photos/vu3cYpSsB9U) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |
| `festival-crowd.jpg` | Yvette de Wit, 3 August 2016 — [Audience enjoying a concert](https://commons.wikimedia.org/wiki/File:Audience_enjoying_a_concert_(Unsplash).jpg), [original publication](https://unsplash.com/photos/NYrVisodQ2M) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |
| `headphones.jpg` | Alphacolor 13 / duck58cth, December 2016 — [Headphones on a loudspeaker](https://commons.wikimedia.org/wiki/File:Headphones_on_a_loudspeaker_(Unsplash).jpg), [original publication](https://unsplash.com/photos/XdDyRTVo-iw) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |
| `studio-microphone.jpg` | WillSpirit SBLN / willspirit, 29 June 2016 — [Headphone Microphone Madrid](https://commons.wikimedia.org/wiki/File:Headphone_Microphone_Madrid_(Unsplash).jpg), [original publication](https://unsplash.com/photos/ZEfmWH1nr5Q) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |
| `electric-guitar.jpg` | Jacob Windham, 20 December 2004 — [Guitar electric close up](https://commons.wikimedia.org/wiki/File:Guitar_electric_close_up.jpg), [original publication](https://www.flickr.com/photos/jacobw/2396311/) | [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/) |
| `blue-stage.jpg` | Lemonade2806 — [Concert blue lights](https://commons.wikimedia.org/wiki/File:Concert_blue_lights.jpg) | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |

The Commons pages identify the first four as Unsplash publications made before
the 5 June 2017 license change and released under CC0. All files keep their source
licenses. Photographers do not endorse Mizon or the fictional fixtures.

## Video derivatives and previews

Each `*.mp4` is a **silent animated photograph**, not a recording of a concert.
The corresponding source JPG is center-cropped to 16:9 and gently zoomed in/out
over twelve seconds. Each `*-poster.jpg` is extracted from the first encoded
frame, so previews match the video rather than showing an unrelated picture.
These derivatives retain the source photograph's license, including CC BY-SA 4.0
for `blue-stage.mp4` and `blue-stage-poster.jpg`. Attribution above applies to them.

The explicit local script `Tools/SeedStoryTestData/BuildMusicHomeArtwork.ps1`
reproduces the derivatives with FFmpeg (H.264, 1280×720, 30 fps, yuv420p, faststart,
no audio). It does not modify the six source JPGs. No runtime encoding occurs.

## Database use

Files are committed here, not in a temporary download/cache directory. The explicit
operator stores a local file URI derived from this checkout's canonical path.
After relocating the server repository, run its read-only plan first and then
reapply with the new canonical artwork directory. The same bytes reuse existing
Media rows at the same path; changed paths/bytes get new public IDs for safe caches.

See `Tools/SeedStoryTestData/MUSIC-HOME-ARTWORK.md` for the exact scope and commands.
