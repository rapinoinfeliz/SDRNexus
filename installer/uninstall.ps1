[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'SDRSharp*' }) {
    throw 'Close SDR# before uninstalling SDRNexus.'
}
Get-Process DXNexus.Bridge -ErrorAction SilentlyContinue | Stop-Process -Force
$bridgeDirectory = $PSScriptRoot
$statePath = Join-Path $bridgeDirectory 'install-state.json'
if (Test-Path $statePath) {
    $state = Get-Content $statePath -Raw | ConvertFrom-Json
    foreach ($file in $state.pluginFiles) {
        $target = Join-Path $state.pluginDirectory $file
        if ((Test-Path $target) -and $PSCmdlet.ShouldProcess($target, 'Remove SDRNexus plugin file')) { Remove-Item $target -Force }
    }
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'DXNexus Bridge.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
$programShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'DXNexus Bridge.lnk'
if (Test-Path $programShortcut) { Remove-Item $programShortcut -Force }
Write-Host 'SDRNexus plugin files and startup shortcut removed.'
Write-Host "You may now remove $bridgeDirectory. Credentials and offline data under LocalAppData\DXNexus were preserved."
