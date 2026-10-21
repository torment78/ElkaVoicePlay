param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$downloads = Join-Path $repo 'artifacts\gpu-downloads'
$runtime = Join-Path $repo 'artifacts\transcription-runtime'
[IO.Directory]::CreateDirectory($downloads) | Out-Null
function Get-Verified([string]$Name, [string]$Uri, [string]$Hash) {
    $target = Join-Path $downloads $Name
    if (-not (Test-Path -LiteralPath $target)) {
        $partial = "$target.partial"
        & curl.exe -fLsS --retry 3 $Uri -o $partial
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $Name" }
        if ((Get-FileHash -LiteralPath $partial).Hash -ne $Hash) { throw "Checksum mismatch: $Name" }
        Move-Item -LiteralPath $partial -Destination $target
    }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $Hash) { throw "Checksum mismatch: $Name" }
    return $target
}
Get-Verified 'ggml-medium.bin' 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin' '6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208' | Out-Null
Get-Verified 'ggml-large-v3.bin' 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin' '64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2' | Out-Null
Get-Verified 'Nemotron-3-Diarization.q8_0.gguf' 'https://huggingface.co/nvidia/Nemotron-3-Diarization/resolve/main/Nemotron-3-Diarization.q8_0.gguf' '08456d9e22cd9a323c0364d98375f3746d6e68507ebb705cd46438c534c7a3a1' | Out-Null
foreach ($item in @(
    @('nemo-cpu', 'https://github.com/NVIDIA/NeMo-Speech.cpp/releases/download/v0.2.0/nemo-speech-0.2.0-windows-x86_64-cpu.zip', '82c80451086e86194aba9af19f800da77cd66d1c28d993d0efceffd6cdb49bb8'),
    @('nemo-cuda', 'https://github.com/NVIDIA/NeMo-Speech.cpp/releases/download/v0.2.0/nemo-speech-0.2.0-windows-x86_64-cuda.zip', '9ffe5e842c39a7d3211f3b2c3b3aa1bca8c0b2455d96d961ed5f22ec5108f98d'),
    @('cuda_cudart', 'https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-13.0.96-archive.zip', 'a2ed875f9997aa24904fb70cc9db3acd9308433cde99bc8e63ec1271c9da31b4'),
    @('libcublas', 'https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-13.1.0.3-archive.zip', '4ac4847bbe4f7709b244956fcfc32197a2954ee70b155cb67eebd9ee26f7e339')
)) {
    $archive = Get-Verified ($item[0]+'.zip') $item[1] $item[2]
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $downloads $item[0]) -Force
}
foreach ($pair in @(@('nemo-cpu','NemoCpu'), @('nemo-cuda','NemoCuda'))) {
    $sdk = Get-ChildItem -LiteralPath (Join-Path $downloads $pair[0]) -Directory | Select-Object -First 1
    $target = Join-Path $runtime $pair[1]
    [IO.Directory]::CreateDirectory($target) | Out-Null
    Copy-Item -Path (Join-Path $sdk.FullName 'bin\*.dll') -Destination $target -Force
    $share = Join-Path $sdk.FullName 'share'
    foreach ($file in Get-ChildItem -LiteralPath $share -Recurse -File) {
        $destination = Join-Path (Join-Path $target 'share') $file.FullName.Substring($share.Length + 1)
        [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}
foreach ($target in @((Join-Path $runtime 'Cuda'), (Join-Path $runtime 'NemoCuda'))) {
    [IO.Directory]::CreateDirectory($target) | Out-Null
    foreach ($component in @('cuda_cudart','libcublas')) {
        Get-ChildItem -LiteralPath (Join-Path $downloads $component) -Filter '*.dll' -Recurse | Where-Object Name -NotLike 'nvblas*' | Copy-Item -Destination $target -Force
        $license = Get-ChildItem -LiteralPath (Join-Path $downloads $component) -Filter LICENSE -Recurse | Select-Object -First 1
        if (-not $license) { $license = Get-ChildItem -LiteralPath (Join-Path $downloads $component) -Filter LICENSE.txt -Recurse | Select-Object -First 1 }
        if (-not $license) { throw "License missing for $component" }
        Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $target ($component+'-LICENSE.txt')) -Force
    }
}
Write-Output 'Large-v3, Nemotron-3, native CPU/CUDA runtimes and CUDA redistributables verified and prepared.'
