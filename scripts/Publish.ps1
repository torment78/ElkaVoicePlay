param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts\ElkaVoicePlay-win-x64-0.7.0',
    [string]$WorkerDirectory = 'artifacts\transcription-worker-0.7.0',
    [string]$ReleaseDirectory = 'artifacts\release-0.7.0',
    [string]$ModelPath = 'artifacts\gpu-downloads\ggml-base.bin'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $worker = [IO.Path]::GetFullPath($WorkerDirectory)
    foreach ($destination in @($output,$worker)) {
        if ((Test-Path -LiteralPath $destination) -and (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) { throw 'Publish to fresh empty app and worker directories.' }
    }
    $models = @{
        $ModelPath = '60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE'
        'artifacts\speaker-models\segmentation.onnx' = '220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079'
        'artifacts\speaker-models\embedding.onnx' = '1A331345F04805BADBB495C775A6DDFFCDD1A732567D5EC8B3D5749E3C7A5E4B'
    }
    foreach ($file in $models.Keys) { if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $models[$file]) { throw "Required model missing or damaged: $file" } }
    # Build the worker against the stable model catalog that omits the worker's own package hash.
    $workerCatalog = Join-Path $repo 'src\ElkaVoicePlay.Shared\worker-components.json'
    dotnet publish src/ElkaVoicePlay.ModelWorker -c $Configuration --self-contained true -p:Platform=x64 -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false "-p:ComponentCatalogFile=$workerCatalog" -o $worker
    if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }
    foreach ($relative in @('runtimes\win-x86','runtimes\win-arm64')) {
        $path = [IO.Path]::GetFullPath((Join-Path $worker $relative))
        if (-not $path.StartsWith($worker + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected worker cleanup path.' }
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    python scripts/Package-Transcription.py --worker $worker --output $ReleaseDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Transcription packaging failed.' }
    dotnet publish src/ElkaVoicePlay.App -c $Configuration --self-contained true -p:Platform=x64 -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    foreach ($relative in @('runtimes\win-x86','runtimes\win-arm64')) {
        $path = [IO.Path]::GetFullPath((Join-Path $output $relative))
        if (-not $path.StartsWith($output + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected app cleanup path.' }
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    foreach ($native in @('onnxruntime.dll','sherpa-onnx-c-api.dll')) {
        $path=Join-Path $output $native
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    [IO.Directory]::CreateDirectory((Join-Path $output 'Models')) | Out-Null
    Copy-Item -LiteralPath $ModelPath -Destination (Join-Path $output 'Models\ggml-base.bin') -Force
    [IO.Directory]::CreateDirectory((Join-Path $output 'licenses')) | Out-Null
    foreach ($name in @('WHISPER-MODEL-LICENSE.txt','SHERPA-ONNX-LICENSE.txt','WHISPER-NET-LICENSE.txt','WHISPER-CPP-LICENSE.txt','NAUDIO-LICENSE.txt','DOTNET-RUNTIME-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','DOTNET-DESKTOP-LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path 'licenses' $name) -Destination (Join-Path $output ('licenses\'+$name)) }
    Copy-Item -LiteralPath 'docs\START-HERE.txt' -Destination (Join-Path $output 'START-HERE.txt')
    Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md' -Destination $output
    $forbidden = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.FullName -match '(?i)ggml-medium|large-v3|nemotron|cublas|cudart|ggml-cuda|[\\/]Workers[\\/]|[\\/]Speakers[\\/]|onnxruntime\.dll|sherpa-onnx-c-api\.dll' }
    if ($forbidden) { throw 'Optional transcription or runtime files leaked into the game bundle.' }
    Write-Output "Game-only Base bundle: $output"
} finally { Pop-Location }
