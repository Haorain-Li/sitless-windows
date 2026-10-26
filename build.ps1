param([string]$OutputDirectory = 'dist')

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x。' }
$dist = Join-Path $projectDir $OutputDirectory
New-Item -ItemType Directory -Path $dist -Force | Out-Null
& $compiler /nologo /target:winexe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /win32manifest:"$projectDir\app.manifest" /out:"$dist\SitLess.exe" "$projectDir\App.cs" "$projectDir\TimerEngine.cs" "$projectDir\FullscreenQuietMode.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
& $compiler /nologo /target:exe /optimize+ /out:"$projectDir\TimerEngineTests.exe" "$projectDir\TimerEngine.cs" "$projectDir\TimerEngineTests.cs"
if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
& "$projectDir\TimerEngineTests.exe"
if ($LASTEXITCODE -ne 0) { throw '计时测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:ButtonRenderingTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\ButtonRenderingTests.exe" "$projectDir\App.cs" "$projectDir\TimerEngine.cs" "$projectDir\FullscreenQuietMode.cs" "$projectDir\ButtonRenderingTests.cs"
if ($LASTEXITCODE -ne 0) { throw '按钮绘制测试编译失败。' }
& "$dist\ButtonRenderingTests.exe"
if ($LASTEXITCODE -ne 0) { throw '按钮绘制测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:FullscreenQuietModeTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\FullscreenQuietModeTests.exe" "$projectDir\App.cs" "$projectDir\TimerEngine.cs" "$projectDir\FullscreenQuietMode.cs" "$projectDir\FullscreenQuietModeTests.cs"
if ($LASTEXITCODE -ne 0) { throw '免打扰测试编译失败。' }
& "$dist\FullscreenQuietModeTests.exe"
if ($LASTEXITCODE -ne 0) { throw '免打扰测试未通过。' }
foreach ($document in @('使用说明.md', '优化记录.md', '验证说明.md', '优化方案清单.md', 'RELEASE_NOTES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectDir $document) -Destination $dist -Force
}
Write-Output "构建成功：$dist\SitLess.exe"
