param([string]$OutputDirectory = 'dist/release')

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
& (Join-Path $projectDir 'build.ps1') -OutputDirectory $OutputDirectory
$dist = Join-Path $projectDir $OutputDirectory
$manifest = [xml](Get-Content -LiteralPath (Join-Path $projectDir 'app.manifest') -Raw)
$version = ([version]$manifest.assembly.assemblyIdentity.version).ToString(3)
$packageName = "SitLess-Windows-v$version.zip"
$package = Join-Path $dist $packageName
$files = @(Get-Content -LiteralPath (Join-Path $projectDir 'distribution.files.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream = [IO.File]::Open($package, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $dist $file), $file.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $zipStream.Dispose() }
$checksums = foreach ($name in @('SitLess.exe', $packageName)) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$checksums, [Text.Encoding]::ASCII)
Write-Output "发布文件：$dist"
