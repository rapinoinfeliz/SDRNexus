[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path (Join-Path $_ 'SDRSharp.exe') })]
    [string]$SdrSharpPath,
    [switch]$NoStartup
)

$ErrorActionPreference = 'Stop'
$SdrSharpPath = (Resolve-Path $SdrSharpPath).Path
if (Get-Process SDRSharp -ErrorAction SilentlyContinue) { throw 'Close SDR# before installing SDRNexus.' }

$configPath = Join-Path $SdrSharpPath 'SDRSharp.config'
$pluginDirectory = $SdrSharpPath
if (Test-Path $configPath) {
    [xml]$config = Get-Content $configPath
    $configured = $config.configuration.add | Where-Object { $_.key -eq 'core.pluginsDirectory' } | Select-Object -First 1
    if ($configured -and $configured.value) {
        $pluginDirectory = if ([IO.Path]::IsPathRooted($configured.value)) { $configured.value } else { Join-Path $SdrSharpPath $configured.value }
    }
}
$pluginDirectory = [IO.Path]::GetFullPath($pluginDirectory)
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
$pluginFiles = @('DXNexus.SdrSharp.Plugin.dll', 'DXNexus.Contracts.dll', 'DXNexus.LocalTransport.dll', 'DXNexus.Plugin.Core.dll')
foreach ($file in $pluginFiles) { Copy-Item (Join-Path $PSScriptRoot "Plugin\$file") (Join-Path $pluginDirectory $file) -Force }

$bridgeDirectory = Join-Path $env:LOCALAPPDATA 'Programs\SDRNexus Bridge'
New-Item -ItemType Directory -Path $bridgeDirectory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Bridge\*') $bridgeDirectory -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $bridgeDirectory -Force
@{ sdrSharpPath = $SdrSharpPath; pluginDirectory = $pluginDirectory; pluginFiles = $pluginFiles } |
    ConvertTo-Json | Set-Content (Join-Path $bridgeDirectory 'install-state.json') -Encoding UTF8

if (-not $NoStartup) {
    $startup = [Environment]::GetFolderPath('Startup')
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $startup 'DXNexus Bridge.lnk'))
    $shortcut.TargetPath = Join-Path $bridgeDirectory 'DXNexus.Bridge.exe'
    $shortcut.WorkingDirectory = $bridgeDirectory
    $shortcut.Description = 'DXNexus companion for SDR#'
    $shortcut.Save()
}
Start-Process (Join-Path $bridgeDirectory 'DXNexus.Bridge.exe')
Write-Host 'SDRNexus installed. Restart SDR# and open Radio tools -> DXNexus.'
Write-Host "Plugin directory: $pluginDirectory"
Write-Host "Bridge directory: $bridgeDirectory"
