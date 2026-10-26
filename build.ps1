param([string]$OutputDirectory = 'dist')

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x。' }
$dist = Join-Path $projectDir $OutputDirectory
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$sources = @('App.cs', 'TimerEngine.cs', 'FullscreenQuietMode.cs', 'DailyCheckIns.cs', 'DailyCheckInWindow.cs', 'ThemeMenu.cs', 'WindowSizing.cs', 'UserDataServices.cs', 'DailyCalendar.cs') | ForEach-Object { Join-Path $projectDir $_ }
& $compiler /nologo /target:winexe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /win32manifest:"$projectDir\app.manifest" /out:"$dist\SitLess.exe" @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
& $compiler /nologo /target:exe /optimize+ /out:"$dist\TimerEngineTests.exe" "$projectDir\TimerEngine.cs" "$projectDir\TimerEngineTests.cs"
if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
& "$dist\TimerEngineTests.exe"
if ($LASTEXITCODE -ne 0) { throw '计时测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:ButtonRenderingTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\ButtonRenderingTests.exe" @sources "$projectDir\ButtonRenderingTests.cs"
if ($LASTEXITCODE -ne 0) { throw '按钮绘制测试编译失败。' }
& "$dist\ButtonRenderingTests.exe"
if ($LASTEXITCODE -ne 0) { throw '按钮绘制测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:FullscreenQuietModeTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\FullscreenQuietModeTests.exe" @sources "$projectDir\FullscreenQuietModeTests.cs"
if ($LASTEXITCODE -ne 0) { throw '免打扰测试编译失败。' }
& "$dist\FullscreenQuietModeTests.exe"
if ($LASTEXITCODE -ne 0) { throw '免打扰测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:DailyCheckInTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\DailyCheckInTests.exe" @sources "$projectDir\DailyCheckInTests.cs"
if ($LASTEXITCODE -ne 0) { throw '每日打卡测试编译失败。' }
& "$dist\DailyCheckInTests.exe"
if ($LASTEXITCODE -ne 0) { throw '每日打卡测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:WindowSizingTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\WindowSizingTests.exe" @sources "$projectDir\WindowSizingTests.cs"
if ($LASTEXITCODE -ne 0) { throw '浮窗缩放测试编译失败。' }
& "$dist\WindowSizingTests.exe"
if ($LASTEXITCODE -ne 0) { throw '浮窗缩放测试未通过。' }
& $compiler /nologo /target:exe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /main:RobustnessTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /out:"$dist\RobustnessTests.exe" @sources "$projectDir\RobustnessTests.cs"
if ($LASTEXITCODE -ne 0) { throw '健壮性测试编译失败。' }
& "$dist\RobustnessTests.exe"
if ($LASTEXITCODE -ne 0) { throw '健壮性测试未通过。' }
$distributionFiles = @(Get-Content -LiteralPath (Join-Path $projectDir 'distribution.files.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
foreach ($document in $distributionFiles) {
    if ($document -eq 'SitLess.exe') { continue }
    $destination = Join-Path $dist $document
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectDir $document) -Destination $destination -Force
}
Write-Output "构建成功：$dist\SitLess.exe"
