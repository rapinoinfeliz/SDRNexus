[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts"
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stage = Join-Path $OutputDirectory "stage\SDRNexus-$Runtime"
$bridge = Join-Path $stage 'Bridge'
$plugin = Join-Path $stage 'Plugin'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $bridge, $plugin -Force | Out-Null
dotnet publish (Join-Path $root 'src\DXNexus.Bridge\DXNexus.Bridge.csproj') --configuration $Configuration --runtime $Runtime --self-contained false --output $bridge
dotnet build (Join-Path $root 'src\DXNexus.SdrSharp.Plugin\DXNexus.SdrSharp.Plugin.csproj') --configuration $Configuration
$pluginOutput = Join-Path $root "src\DXNexus.SdrSharp.Plugin\bin\$Configuration\net9.0-windows"
@('DXNexus.SdrSharp.Plugin.dll', 'DXNexus.Contracts.dll', 'DXNexus.LocalTransport.dll', 'DXNexus.Plugin.Core.dll') |
    ForEach-Object { Copy-Item (Join-Path $pluginOutput $_) $plugin -Force }
Copy-Item (Join-Path $root 'installer\install.ps1') $stage
Copy-Item (Join-Path $root 'installer\uninstall.ps1') $stage
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md')
@{ version = '0.1.0'; protocol = '1.0'; runtime = $Runtime; sdrSharpRevision = 1921; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
    ConvertTo-Json | Set-Content (Join-Path $stage 'release-manifest.json') -Encoding UTF8
$checksums = Get-ChildItem $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$checksums | Set-Content (Join-Path $stage 'checksums.sha256') -Encoding ASCII
$archive = Join-Path $OutputDirectory "SDRNexus-0.1.0-$Runtime.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $archive -CompressionLevel Optimal
Write-Host $archive
