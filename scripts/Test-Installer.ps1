[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceDir)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $root ('artifacts\installer-tests\'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$target=Join-Path $fixture 'Installed'
$registration='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ElkaVoicePlayInstallerSmokeTest_is1'
$shortcut=Join-Path ([Environment]::GetFolderPath('Desktop')) 'Elka VoicePlay Installer Test.lnk'
$startShortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'ElkaVoicePlayInstallerSmokeTest\Elka VoicePlay Test.lnk'
if ((Test-Path -LiteralPath $registration) -or (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath $startShortcut)) { throw 'A prior installer test remains; inspect it before retrying.' }
& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -SourceDir $SourceDir -OutputDir $fixture -InstallerTest
$setup=Join-Path $fixture 'ElkaVoicePlay-0.7.0-installer-test.exe'
function Install-Fixture([string]$phase) {
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS="desktopicon"',('/DIR="'+$target+'"'),('/LOG="'+(Join-Path $fixture "$phase.log")+'"'))
    $process=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw "Installer failed during $phase." }
}
Install-Fixture 'install'
$payloadFiles=@(Get-ChildItem -LiteralPath $SourceDir -Recurse -File)
foreach ($file in $payloadFiles) {
    $relative=[IO.Path]::GetRelativePath((Resolve-Path -LiteralPath $SourceDir).Path,$file.FullName)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash) { throw "Installed file mismatch: $relative" }
}
if (-not (Test-Path -LiteralPath $registration)) { throw 'Windows uninstall registration missing.' }
if ((Get-ItemProperty -LiteralPath $registration).InstallLocation.TrimEnd('\') -ne $target) { throw 'Wrong installation directory in uninstall registration.' }
$shell=New-Object -ComObject WScript.Shell
foreach ($link in @($shortcut,$startShortcut)) {
    if (-not (Test-Path -LiteralPath $link)) { throw "Missing shortcut: $link" }
    if ($shell.CreateShortcut($link).TargetPath -ne (Join-Path $target 'ElkaVoicePlay.exe')) { throw 'Shortcut points to wrong app.' }
}
$sentinel=Join-Path $target 'user-created-file.keep'; [IO.File]::WriteAllText($sentinel,'Preserve this user file.')
$sentinelHash=(Get-FileHash -LiteralPath $sentinel).Hash
Install-Fixture 'reinstall'
if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $sentinelHash) { throw 'Reinstall changed user file.' }
$uninstall=Join-Path $target 'unins000.exe'
$process=Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $fixture 'uninstall.log')+'"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Test uninstall failed.' }
foreach ($removed in @($registration,$shortcut,$startShortcut,(Join-Path $target 'ElkaVoicePlay.exe'))) { if (Test-Path -LiteralPath $removed) { throw "Uninstall left $removed" } }
if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $sentinelHash) { throw 'Uninstall removed user file.' }
$report="PASS: install/reinstall/uninstall; $($payloadFiles.Count) payload hashes; Start-menu and desktop shortcuts; uninstall registration; user-file preservation. Application not launched."
$report | Set-Content -LiteralPath (Join-Path $fixture 'result.txt') -Encoding utf8
Write-Output $report
Write-Output "Evidence: $fixture"
