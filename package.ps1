param([string]$Version = '0.1.1', [string]$OutputRoot = (Join-Path $PSScriptRoot 'release'))
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$archive = Join-Path $OutputRoot "SteamGaze-$Version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
$build = Join-Path $OutputRoot ('build-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'build.ps1') -Output $build
$names = @('SteamGazeOverlay.exe','openvr_api.dll','actions.json','binding_generic.json','binding_psvr2.json','README.md','README.zh-CN.md','EXTENSIONS.md','THIRD-PARTY.md','CHANGELOG.md','LICENSE','OpenVR-LICENSE.txt')
$files = @($names | ForEach-Object { $path = Join-Path $build $_; if (!(Test-Path -LiteralPath $path)) { throw "Missing release file: $_" }; $path })
$files += Join-Path $build 'docs'
$files += Join-Path $build 'bridges'
Compress-Archive -LiteralPath $files -DestinationPath $archive -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath (Join-Path $OutputRoot 'SHA256SUMS.txt') -Encoding Ascii
Write-Output $archive
Write-Output $hash
