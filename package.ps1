param([string]$OutputDirectory = 'dist/release')

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
& (Join-Path $projectDir 'build.ps1') -OutputDirectory $OutputDirectory
$dist = Join-Path $projectDir $OutputDirectory
$manifest = [xml](Get-Content -LiteralPath (Join-Path $projectDir 'app.manifest') -Raw)
$version = ([version]$manifest.assembly.assemblyIdentity.version).ToString(3)
$packageName = "SitLess-Windows-v$version.zip"
$package = Join-Path $dist $packageName
$files = @('SitLess.exe', 'RELEASE_NOTES.md', '使用说明.md', '优化记录.md', '验证说明.md', '优化方案清单.md')
$packageFiles = @($files | ForEach-Object { Join-Path $dist $_ })
Compress-Archive -LiteralPath $packageFiles -DestinationPath $package -CompressionLevel Optimal -Force
$checksums = foreach ($name in @('SitLess.exe', $packageName)) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$checksums, [Text.Encoding]::ASCII)
Write-Output "发布文件：$dist"
