"""Build reproducible optional component assets and the pinned application catalog."""
from pathlib import Path
import hashlib, json, zipfile, shutil

repo = Path(__file__).resolve().parents[1]
output = repo / 'artifacts/release-0.6.0'
output.mkdir(parents=True, exist_ok=True)
downloads = repo / 'artifacts/gpu-downloads'
runtime = repo / 'artifacts/transcription-runtime'
version = '1'

def sha(path):
    with path.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest().upper()

def record(path, name_key='Name', name=None):
    return {name_key: name or path.name, 'Size': path.stat().st_size, 'Sha256': sha(path)}

def zip_component(id, title, description, files):
    target = output / f'VoicePlay-{id}-v{version}.zip'
    # Stable file order and timestamps keep the app catalog reproducible.
    with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
        for name, source in sorted(files.items()):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            with z.open(info, 'w', force_zip64=True) as out, source.open('rb') as inp:
                while block := inp.read(4*1024*1024): out.write(block)
    return {'Id': id, 'Name': title, 'Description': description, 'Version': version, 'Format': 'zip',
            'Assets': [record(target)], 'Files': [record(source, 'Path', name) for name, source in sorted(files.items())]}

large = downloads / 'ggml-large-v3.bin'
assert sha(large) == '64D182B440B98D5203C4F9BD541544D84C605196C4F7B845DFA11FB23594D1E2'
assets = []
with large.open('rb') as inp:
    for index in (1,2):
        target = output / f'VoicePlay-large-v3-v1.bin.part{index}'
        remaining = 1_600_000_000
        with target.open('wb') as out:
            while remaining:
                block = inp.read(min(4*1024*1024, remaining))
                if not block: break
                out.write(block); remaining -= len(block)
        assets.append(record(target))
catalog = [{'Id': 'large-v3', 'Name': 'Whisper Large-v3', 'Description': 'Higher-quality transcription and English translation. Works on CPU; add NVIDIA support for faster processing. Uses more memory than Base.', 'Version': version, 'Format': 'parts', 'Assets': assets, 'Files': [record(large, 'Path', 'Models/ggml-large-v3.bin')]}]
medium = downloads / 'ggml-medium.bin'
assert sha(medium) == '6C14D5ADEE5F86394037B4E4E8B59F1673B6CEE10E3CF0B11BBDBEE79C156208'
medium_asset = output/'VoicePlay-medium-v1.bin'
shutil.copyfile(medium, medium_asset)
catalog.insert(0, {'Id': 'medium', 'Name': 'Whisper Medium', 'Description': 'A middle-size transcription and English translation model, between Base and Large-v3. Works on CPU or with optional NVIDIA support.', 'Version': version, 'Format': 'parts', 'Assets': [record(medium_asset)], 'Files': [record(medium, 'Path', 'Models/ggml-medium.bin')]})
nemo_files = {'Models/Nemotron-3-Diarization.q8_0.gguf': downloads / 'Nemotron-3-Diarization.q8_0.gguf'}
for file in (runtime / 'NemoCpu').rglob('*'):
    if file.is_file(): nemo_files['NemoCpu/' + file.relative_to(runtime / 'NemoCpu').as_posix()] = file
for name in ('NEMOTRON-MODEL-LICENSE.txt','NEMOTRON-MODEL-CARD.md'):
    nemo_files['licenses/' + name] = repo / 'licenses' / name
catalog.append(zip_component('nemotron', 'Nemotron-3 speakers', 'An alternative speaker model, with up to eight speakers per input. Includes its CPU engine; NVIDIA support is a separate optional download. Speaker labels remain estimates.', nemo_files))
gpu_files = {}
for folder in ('Cuda', 'NemoCuda'):
    for file in (runtime / folder).rglob('*'):
        if not file.is_file(): continue
        if folder == 'NemoCuda' and ((runtime/'Cuda'/file.name).exists()): continue
        gpu_files[folder + '/' + file.relative_to(runtime/folder).as_posix()] = file
cuda_whisper = Path.home()/'.nuget/packages/whisper.net.runtime.cuda.windows/1.9.1/build/win-x64'
for file in cuda_whisper.glob('*.dll'): gpu_files['runtimes/cuda/win-x64/' + file.name] = file
assert 'runtimes/cuda/win-x64/whisper.dll' in gpu_files
gpu_files['licenses/WHISPER-MODEL-LICENSE.txt'] = repo/'licenses/WHISPER-MODEL-LICENSE.txt'
catalog.append(zip_component('nvidia', 'NVIDIA GPU support', 'Speeds up Whisper and optional Nemotron on a compatible NVIDIA GPU. Requires a CUDA 13-capable NVIDIA driver. Includes app-local runtimes; it does not install or change your driver.', gpu_files))
for item in catalog:
    for asset in item['Assets']: asset['ReleaseTag'] = 'v0.6.0-preview.1'
text = json.dumps(catalog, indent=2) + '\n'
(repo/'src/ElkaVoicePlay.Shared/worker-components.json').write_text(text, encoding='utf-8')
print('Rebuilt legacy model catalog. Run Publish.ps1 to compose the current application catalog.')
(output/'optional-components.json').write_text(text, encoding='utf-8')
for item in catalog:
    print(item['Id'], 'download bytes:', sum(a['Size'] for a in item['Assets']), 'installed bytes:', sum(f['Size'] for f in item['Files']), flush=True)
