[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceDir,[Parameter(Mandatory)][string]$OutputDir,[switch]$InstallerTest)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$payload=(Resolve-Path -LiteralPath $SourceDir).Path
$exe=Join-Path $payload 'ElkaVoicePlay.exe'
if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne '0.7.0') { throw 'Installer and payload versions differ.' }
$forbidden=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Name -match 'settings|\.log$|\.pdb$|ggml-medium|large-v3|cublas|cudart|onnxruntime\.dll' -or $_.FullName -match '[\\/](Workers|Speakers)[\\/]' })
if ($forbidden) { throw 'Unexpected optional files or private data in the installer payload.' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$output=(Resolve-Path -LiteralPath $OutputDir).Path
$compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
$arguments=@('/Qp',"/DSourceDir=$payload","/DOutputDir=$output")
if ($InstallerTest) { $arguments+='/DInstallerTest' }
& $compiler @arguments (Join-Path $root 'installer\ElkaVoicePlay.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
