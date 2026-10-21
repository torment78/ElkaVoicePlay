param(
    [ValidateSet('tiny','base','small','medium','large-v3')][string]$Model = 'base',
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'ElkaSoftware\ElkaVoicePlay\Models')
)
$ErrorActionPreference = 'Stop'
$catalog = @{
    tiny = @{ Bytes = 77691713L; Sha256 = 'be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21' }
    base = @{ Bytes = 147951465L; Sha256 = '60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe' }
    small = @{ Bytes = 487601967L; Sha256 = '1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b' }
    'large-v3' = @{ Bytes = 3095033483L; Sha256 = '64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2' }
    medium = @{ Bytes = 1533763059L; Sha256 = '6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208' }
}
$spec = $catalog[$Model]
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$target = Join-Path $Destination "ggml-$Model.bin"
function Test-ModelFile([string]$Path) {
    if ((Get-Item -LiteralPath $Path).Length -ne $spec.Bytes) { throw "Wrong size for $Path" }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $spec.Sha256) { throw "SHA-256 verification failed for $Path" }
}
if (Test-Path -LiteralPath $target) {
    Test-ModelFile $target
    Write-Output "Verified existing model: $target"
    exit 0
}
$partial = "$target.$([Guid]::NewGuid().ToString('N')).partial"
try {
    Write-Output "Downloading multilingual $Model ($([Math]::Round($spec.Bytes / 1MB)) MiB)…"
    & curl.exe --fail --location --retry 3 --output $partial "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$Model.bin"
    if ($LASTEXITCODE -ne 0) { throw "Model download failed ($LASTEXITCODE)." }
    Test-ModelFile $partial
    Move-Item -LiteralPath $partial -Destination $target -ErrorAction Stop
    Write-Output "SHA-256 verified: $target"
} finally {
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
}
