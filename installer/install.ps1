[CmdletBinding()]
param(
    [string]$SdrSharpPath,
    [switch]$NoStartup
)

$ErrorActionPreference = 'Stop'
$interactiveInstall = [string]::IsNullOrWhiteSpace($SdrSharpPath)
$launchers = @('SDRSharp.dotnet9.exe', 'SDRSharp.dotnet8.exe', 'SDRSharp.exe')

function Test-SdrSharpDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }
    foreach ($launcher in $launchers) {
        if (Test-Path -LiteralPath (Join-Path $Path $launcher) -PathType Leaf) { return $true }
    }
    return $false
}

if ($interactiveInstall) {
    Add-Type -AssemblyName System.Windows.Forms
    $knownPaths = @(@(
        (Join-Path $env:USERPROFILE 'Downloads\sdrsharp-x86'),
        (Join-Path $env:USERPROFILE 'Desktop\sdrsharp-x86'),
        'C:\SDRSharp',
        'C:\sdrsharp-x86',
        'D:\sdrsharp-x86'
    ) | Where-Object { Test-SdrSharpDirectory $_ } | Select-Object -Unique)

    if (@($knownPaths).Count -eq 1) {
        $SdrSharpPath = $knownPaths[0]
    }
    else {
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Select the SDR# folder (the folder containing SDRSharp.exe or SDRSharp.dotnet9.exe).'
        $picker.ShowNewFolderButton = $false
        if (@($knownPaths).Count -gt 0) { $picker.SelectedPath = $knownPaths[0] }
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            throw 'Installation canceled.'
        }
        $SdrSharpPath = $picker.SelectedPath
    }
}

if (-not (Test-SdrSharpDirectory $SdrSharpPath)) {
    throw "SDR# was not found in '$SdrSharpPath'. Select the folder containing SDRSharp.exe or SDRSharp.dotnet9.exe."
}

$SdrSharpPath = (Resolve-Path $SdrSharpPath).Path
$runningSdrSharp = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'SDRSharp*' }
if ($runningSdrSharp) {
    $closeSdrSharp = $false
    if ($interactiveInstall) {
        $choice = [System.Windows.Forms.MessageBox]::Show(
            'SDR# must be closed while the plugin is installed. Close it now and continue?',
            'Install SDRNexus',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question)
        $closeSdrSharp = $choice -eq [System.Windows.Forms.DialogResult]::Yes
    }
    if (-not $closeSdrSharp) { throw 'Close SDR# before installing SDRNexus.' }
    $runningSdrSharp | Stop-Process -Force
    $runningSdrSharp | ForEach-Object { $_.WaitForExit(5000) }
}

Get-Process DXNexus.Bridge -ErrorAction SilentlyContinue | Stop-Process -Force

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
    $shortcut.IconLocation = "$(Join-Path $bridgeDirectory 'DXNexus.Bridge.exe'),0"
    $shortcut.Description = 'DXNexus companion for SDR#'
    $shortcut.Save()
}

$programs = [Environment]::GetFolderPath('Programs')
$programShortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $programs 'DXNexus Bridge.lnk'))
$programShortcut.TargetPath = Join-Path $bridgeDirectory 'DXNexus.Bridge.exe'
$programShortcut.WorkingDirectory = $bridgeDirectory
$programShortcut.IconLocation = "$(Join-Path $bridgeDirectory 'DXNexus.Bridge.exe'),0"
$programShortcut.Description = 'DXNexus companion for SDR#'
$programShortcut.Save()

$credentialPath = Join-Path $env:LOCALAPPDATA 'DXNexus\device-credential.bin'
$bridgeExecutable = Join-Path $bridgeDirectory 'DXNexus.Bridge.exe'
if (Test-Path $credentialPath) {
    Start-Process $bridgeExecutable -WorkingDirectory $bridgeDirectory -WindowStyle Hidden
}
else {
    Start-Process $bridgeExecutable -ArgumentList '--pair' -WorkingDirectory $bridgeDirectory -WindowStyle Hidden
}
Write-Host 'SDRNexus installed. Restart SDR# and open Radio tools -> DXNexus.'
Write-Host "Plugin directory: $pluginDirectory"
Write-Host "Bridge directory: $bridgeDirectory"

if ($interactiveInstall) {
    [System.Windows.Forms.MessageBox]::Show(
        "SDRNexus was installed successfully.`n`nOpen SDR# and choose Radio tools > DXNexus.`nOn first install, approve the DXNexus connection in your browser.",
        'SDRNexus installed',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
