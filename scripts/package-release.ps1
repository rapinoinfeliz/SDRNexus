[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.1.0',
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts"
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stage = Join-Path $OutputDirectory "stage\SDRNexus-$Version-$Runtime"
$bridge = Join-Path $stage 'Bridge'
$plugin = Join-Path $stage 'Plugin'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $bridge, $plugin -Force | Out-Null
dotnet publish (Join-Path $root 'src\DXNexus.Bridge\DXNexus.Bridge.csproj') --configuration $Configuration --runtime $Runtime --self-contained true --output $bridge
if ($LASTEXITCODE -ne 0) { throw "Bridge publish failed with exit code $LASTEXITCODE." }
dotnet build (Join-Path $root 'src\DXNexus.SdrSharp.Plugin\DXNexus.SdrSharp.Plugin.csproj') --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE." }
$pluginOutput = Join-Path $root "src\DXNexus.SdrSharp.Plugin\bin\$Configuration\net9.0-windows"
@('DXNexus.SdrSharp.Plugin.dll', 'DXNexus.Contracts.dll', 'DXNexus.LocalTransport.dll', 'DXNexus.Plugin.Core.dll') |
    ForEach-Object { Copy-Item (Join-Path $pluginOutput $_) $plugin -Force }
Copy-Item (Join-Path $root 'installer\install.ps1') $stage
Copy-Item (Join-Path $root 'installer\uninstall.ps1') $stage
Copy-Item (Join-Path $root 'installer\Install-SDRNexus.cmd') $stage
Copy-Item (Join-Path $root 'installer\INSTALL.txt') $stage
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md')
$sourceCommit = (git -C $root rev-parse --short HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) { throw 'Could not determine the source commit.' }
@{ version = $Version; protocol = '1.0'; runtime = $Runtime; sdrSharpRevision = 1921; sourceCommit = $sourceCommit; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
    ConvertTo-Json | Set-Content (Join-Path $stage 'release-manifest.json') -Encoding UTF8
$checksums = Get-ChildItem $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$checksums | Set-Content (Join-Path $stage 'checksums.sha256') -Encoding ASCII
$archive = Join-Path $OutputDirectory "SDRNexus-$Version-windows-x64.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $archive -CompressionLevel Optimal
$archiveChecksum = "{0}  {1}" -f (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $archive -Leaf)
$archiveChecksum | Set-Content "$archive.sha256" -Encoding ASCII
Write-Host $archive
Write-Host "$archive.sha256"
