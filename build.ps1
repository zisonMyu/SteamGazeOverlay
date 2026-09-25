param([string]$Output = (Join-Path $PSScriptRoot 'dist'), [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $Compiler = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
    }
    if (!$Compiler) { $Compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
}
if (!(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found. Install Visual Studio Build Tools or pass -Compiler.' }
$Output = [IO.Path]::GetFullPath($Output)
$sdk = Join-Path $PSScriptRoot 'vendor\openvr'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $sdk 'openvr_api.cs'
& $Compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$Output\SteamGazeOverlay.exe" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $sdk 'openvr_api.dll') -Destination $Output -Force
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.json' | Copy-Item -Destination $Output -Force
foreach ($name in @('README.md','README.zh-CN.md','EXTENSIONS.md','THIRD-PARTY.md','CHANGELOG.md','LICENSE')) { if(Test-Path (Join-Path $PSScriptRoot $name)){Copy-Item (Join-Path $PSScriptRoot $name) $Output -Force} }
Copy-Item -LiteralPath (Join-Path $sdk 'LICENSE') -Destination (Join-Path $Output 'OpenVR-LICENSE.txt') -Force
$imagePath = Join-Path $PSScriptRoot 'docs\settings.png'
if (Test-Path -LiteralPath $imagePath) { New-Item -ItemType Directory -Path (Join-Path $Output 'docs') -Force | Out-Null; Copy-Item -LiteralPath $imagePath -Destination (Join-Path $Output 'docs\settings.png') -Force }
$process=Start-Process -FilePath (Join-Path $Output 'SteamGazeOverlay.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw 'Self tests failed; see data/fatal.txt' }
Get-Content (Join-Path $Output 'data\self-test.txt')
Get-FileHash (Join-Path $Output 'SteamGazeOverlay.exe') -Algorithm SHA256
