"""Create portable/artwork ZIPs, the payload inventory and SHA-256 release checksums."""
from pathlib import Path
import argparse, hashlib, json, zipfile

p = argparse.ArgumentParser()
p.add_argument('--app', required=True)
p.add_argument('--output', default='artifacts/release-0.7.0')
a = p.parse_args()
repo = Path(__file__).resolve().parents[1]
app, output = Path(a.app).resolve(), Path(a.output).resolve()
output.mkdir(parents=True, exist_ok=True)

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()

def files(folder):
    return sorted(f for f in folder.rglob('*') if f.is_file())

def archive(folder, name):
    target = output / name
    with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for source in files(folder):
            info = zipfile.ZipInfo(source.relative_to(folder).as_posix(), (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, source.read_bytes())
    with zipfile.ZipFile(target) as z:
        assert z.testzip() is None
    return target

assert (app / 'ElkaVoicePlay.exe').is_file()
assert not (app / 'Workers').exists() and not (app / 'Models/Speakers').exists()
inventory = [{'Path': f.relative_to(app).as_posix(), 'Size': f.stat().st_size, 'Sha256': digest(f)} for f in files(app)]
(output / 'bundle-files.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
archive(app, 'ElkaVoicePlay-0.7.0-win-x64-game.zip')
archive(repo / 'graphics', 'ElkaVoicePlay-0.7.0-artwork.zip')
names = ['ElkaVoicePlay-0.7.0-setup-win-x64.exe', 'ElkaVoicePlay-0.7.0-win-x64-game.zip',
         'ElkaVoicePlay-0.7.0-artwork.zip', 'VoicePlay-transcription-0.7.0.zip',
         'optional-components.json', 'bundle-files.json']
assert all((output / name).is_file() for name in names)
(output / 'SHA256SUMS').write_text(''.join(f'{digest(output / name)}  {name}\n' for name in names), encoding='utf-8')
print(json.dumps({'Files': len(inventory), 'InstalledBytes': sum(f['Size'] for f in inventory),
                  'Assets': [{'Name': name, 'Bytes': (output / name).stat().st_size} for name in names]}, indent=2))
