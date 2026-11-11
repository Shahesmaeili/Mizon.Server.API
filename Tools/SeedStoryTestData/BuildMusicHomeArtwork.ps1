param([string]$FFmpeg = 'ffmpeg')

$ErrorActionPreference = 'Stop'
$artworkDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Mizon.Server.API\App_Data\MusicHomeArtwork'))
$names = @('blue-live-guitar', 'blue-stage', 'electric-guitar', 'festival-crowd', 'headphones', 'studio-microphone')

# Silent, twelve-second photo motion loops for the existing VIDEO demo entries.
# These are animated photographs, not recordings of a live performance.
foreach ($name in $names) {
    $imagePath = Join-Path $artworkDirectory "$name.jpg"
    $videoPath = Join-Path $artworkDirectory "$name.mp4"
    $posterPath = Join-Path $artworkDirectory "$name-poster.jpg"
    if (-not (Test-Path -LiteralPath $imagePath)) { throw "Missing licensed source photograph: $name" }
    $filter = "scale=1280:720:force_original_aspect_ratio=increase:in_range=pc:out_range=tv,crop=1280:720,zoompan=z='1+0.035*sin(on*PI/360)*sin(on*PI/360)':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=360:s=1280x720:fps=30,setsar=1"
    & $FFmpeg -hide_banner -loglevel error -y -i $imagePath -vf $filter -t 12 -an -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -color_range tv -movflags +faststart $videoPath
    if ($LASTEXITCODE -ne 0) { throw "Could not encode $name" }
    & $FFmpeg -hide_banner -loglevel error -y -i $videoPath -frames:v 1 -q:v 2 $posterPath
    if ($LASTEXITCODE -ne 0) { throw "Could not extract the matching poster for $name" }
}
