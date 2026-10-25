$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x。' }
$dist = Join-Path $projectDir 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
& $compiler /nologo /target:winexe /platform:anycpu32bitpreferred /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /win32manifest:"$projectDir\app.manifest" /out:"$dist\SitLess.exe" "$projectDir\App.cs" "$projectDir\TimerEngine.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
& $compiler /nologo /target:exe /optimize+ /out:"$projectDir\TimerEngineTests.exe" "$projectDir\TimerEngine.cs" "$projectDir\TimerEngineTests.cs"
if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
& "$projectDir\TimerEngineTests.exe"
if ($LASTEXITCODE -ne 0) { throw '计时测试未通过。' }
Copy-Item -LiteralPath "$projectDir\使用说明.md" -Destination $dist -Force
Write-Output "构建成功：$dist\SitLess.exe"
