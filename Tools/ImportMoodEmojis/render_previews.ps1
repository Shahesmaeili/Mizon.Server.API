param(
    [string] $CatalogPath = (Join-Path $PSScriptRoot '..\..\Mizon.Server.API\App_Data\MoodEmojis\catalog.pending.json'),
    [int] $Size = 96
)

$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $env:USERPROFILE '.nuget\packages'
$nativeDirectory = Join-Path $packageRoot 'skiasharp.nativeassets.win32\2.88.3\runtimes\win-x64\native'
$env:PATH = "$nativeDirectory;$env:PATH"
# Existing local renderer used only by this import tool, never by the production client.
Add-Type -Path (Join-Path $packageRoot 'skiasharp\2.88.3\lib\net6.0\SkiaSharp.dll')
Add-Type -Path (Join-Path $packageRoot 'skiasharp.skottie\2.88.3\lib\net6.0\SkiaSharp.Skottie.dll')

$catalogFile = (Resolve-Path -LiteralPath $CatalogPath).Path
$assetRoot = Split-Path -Parent $catalogFile
$catalog = Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json
$imageInfo = [SkiaSharp.SKImageInfo]::new($Size, $Size, [SkiaSharp.SKColorType]::Bgra8888, [SkiaSharp.SKAlphaType]::Premul)
$rectangle = [SkiaSharp.SKRect]::new(0, 0, $Size, $Size)
$index = 0
foreach ($item in $catalog.items) {
    $animation = $null
    $surface = $null
    $image = $null
    $data = $null
    try {
        $animationPath = $item.animationPath
        if ($animationPath.StartsWith('originals/') -and $animationPath.EndsWith('.tgs')) {
            $animationPath = [IO.Path]::ChangeExtension($animationPath.Substring('originals/'.Length), '.json')
        }
        $animation = [SkiaSharp.Skottie.Animation]::Create((Join-Path $assetRoot $animationPath))
        if ($null -eq $animation) { throw "Could not parse $($item.animationPath)" }
        $surface = [SkiaSharp.SKSurface]::Create($imageInfo)
        $surface.Canvas.Clear([SkiaSharp.SKColors]::Transparent)
        $animation.Seek(0, $null)
        $animation.Render($surface.Canvas, $rectangle)
        $image = $surface.Snapshot()
        $data = $image.Encode([SkiaSharp.SKEncodedImageFormat]::Png, 100)
        [IO.File]::WriteAllBytes((Join-Path $assetRoot $item.previewPath), $data.ToArray())
    }
    finally {
        if ($null -ne $data) { $data.Dispose() }
        if ($null -ne $image) { $image.Dispose() }
        if ($null -ne $surface) { $surface.Dispose() }
        if ($null -ne $animation) { $animation.Dispose() }
    }
    $index++
    if (($index % 100) -eq 0) { Write-Output "Rendered $index/$($catalog.items.Count) static previews" }
}
Write-Output "Rendered $index transparent ${Size}x${Size} previews."
