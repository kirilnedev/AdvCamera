<#
.SYNOPSIS
    Compiles the AdvCamera installer with Inno Setup.

.DESCRIPTION
    Expects the app to be published to publish\AdvCamera first:
      dotnet publish src/AdvCamera -c Release -r win-x64 --self-contained -o publish/AdvCamera
    Installs Inno Setup with Chocolatey if it isn't found (as on GitHub's build machines).
    The installer is written to artifacts\.

.EXAMPLE
    ./installer/build-installer.ps1 -Version 1.2.0
#>
param(
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host 'Inno Setup not found, installing with Chocolatey...'
    choco install innosetup -y --no-progress | Out-Host
    $iscc = Find-Iscc
    if (-not $iscc) { throw 'Inno Setup (ISCC.exe) could not be found or installed.' }
}

$script = Join-Path $PSScriptRoot 'AdvCamera.iss'
# Windows file versions must be purely numeric: "1.2.0-ci.5" -> "1.2.0"
$fileVersion = ($Version -split '[-+]')[0]
& $iscc "/DAppVersion=$Version" "/DFileVersion=$fileVersion" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
