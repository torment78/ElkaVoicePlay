param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts\ElkaVoicePlay-win-x64-0.6.0',
    [string]$ModelPath = 'artifacts\gpu-downloads\ggml-base.bin'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    # A fresh destination is required: stale optional DLLs must not enter the small bundle.
    if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
        throw 'Publish to an empty output directory. An existing bundle will not be overwritten.'
    }
    $models = @{
        $ModelPath = @('Models\ggml-base.bin', '60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE')
        'artifacts\speaker-models\segmentation.onnx' = @('Models\Speakers\segmentation.onnx', '220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079')
        'artifacts\speaker-models\embedding.onnx' = @('Models\Speakers\embedding.onnx', '1A331345F04805BADBB495C775A6DDFFCDD1A732567D5EC8B3D5749E3C7A5E4B')
    }
    foreach ($file in $models.Keys) {
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $models[$file][1]) { throw "Required model missing or damaged: $file. Run Get-Model.ps1 / Get-SpeakerModels.ps1." }
    }
    dotnet publish src/ElkaVoicePlay.App -c $Configuration --self-contained true -p:Platform=x64 -p:RestoreLockedMode=true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    dotnet publish src/ElkaVoicePlay.ModelWorker -c $Configuration --self-contained true -p:Platform=x64 -p:RestoreLockedMode=true -o (Join-Path $output 'Workers')
    if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }
    # The CPU Whisper package also contains other Windows architectures. This release is x64.
    foreach ($relative in @('runtimes\win-x86','runtimes\win-arm64','Workers\runtimes\win-x86','Workers\runtimes\win-arm64')) {
        $path = [IO.Path]::GetFullPath((Join-Path $output $relative))
        if (-not $path.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected cleanup path.' }
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    foreach ($file in $models.Keys) {
        $destination = Join-Path $output $models[$file][0]
        [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination -Force
    }
    [IO.Directory]::CreateDirectory((Join-Path $output 'licenses')) | Out-Null
    foreach ($name in @('WHISPER-MODEL-LICENSE.txt','SHERPA-ONNX-LICENSE.txt','PYANNOTE-SEGMENTATION-LICENSE.txt','3D-SPEAKER-LICENSE.txt','ONNX-RUNTIME-LICENSE.txt')) {
        Copy-Item -LiteralPath (Join-Path $repo ('licenses\' + $name)) -Destination (Join-Path $output ('licenses\' + $name)) -Force
    }
    Copy-Item -LiteralPath 'docs\START-HERE.txt' -Destination (Join-Path $output 'START-HERE.txt')
    Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md' -Destination $output
    $forbidden = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.FullName -match '(?i)ggml-medium|large-v3|nemotron|cublas|cudart|ggml-cuda|[\\/]Nemo(Cpu|Cuda)[\\/]' }
    if ($forbidden) { throw 'Optional model/runtime leaked into the small bundle.' }
    Write-Output "Small Base + standard-speaker bundle ready: $output"
} finally { Pop-Location }
