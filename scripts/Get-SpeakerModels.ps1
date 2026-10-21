param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\speaker-models'))
$ErrorActionPreference = 'Stop'
$destinationPath = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($destinationPath) | Out-Null
function Get-Verified([string]$Url, [string]$Path, [string]$Hash) {
    if (-not (Test-Path -LiteralPath $Path)) {
        $temporary = $Path + '.download-' + [guid]::NewGuid().ToString('N')
        Invoke-WebRequest -Uri $Url -OutFile $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Hash) { throw "Download verification failed: $temporary" }
        Move-Item -LiteralPath $temporary -Destination $Path
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw "Existing file is damaged; it was preserved: $Path" }
}
$archive = Join-Path $destinationPath 'segmentation.tar.bz2'
Get-Verified 'https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2' $archive '24615EE884C897D9D2BA09BB4D30DA6BB1B15E685065962DB5B02E76E4996488'
$segmentation = Join-Path $destinationPath 'segmentation.onnx'
if (-not (Test-Path -LiteralPath $segmentation)) {
    # Only named model/license files are extracted from the verified archive; no scripts execute.
    tar -xjf $archive -C $destinationPath sherpa-onnx-pyannote-segmentation-3-0/model.onnx sherpa-onnx-pyannote-segmentation-3-0/LICENSE
    if ($LASTEXITCODE -ne 0) { throw 'Speaker model extraction failed.' }
    Copy-Item -LiteralPath (Join-Path $destinationPath 'sherpa-onnx-pyannote-segmentation-3-0\model.onnx') -Destination $segmentation
}
if ((Get-FileHash -LiteralPath $segmentation -Algorithm SHA256).Hash -ne '220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079') { throw 'Segmentation model verification failed.' }
Get-Verified 'https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx' (Join-Path $destinationPath 'embedding.onnx') '1A331345F04805BADBB495C775A6DDFFCDD1A732567D5EC8B3D5749E3C7A5E4B'
Write-Output "Verified local speaker models: $destinationPath"
