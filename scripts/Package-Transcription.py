"""Package the separately published worker and standard speakers, then pin its descriptor."""
from pathlib import Path
import argparse, hashlib, json, zipfile
p=argparse.ArgumentParser();p.add_argument('--worker',required=True);p.add_argument('--output',required=True);a=p.parse_args()
repo=Path(__file__).resolve().parents[1];worker=Path(a.worker).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True)
def sha(path):
    with path.open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest().upper()
files={'Workers/'+f.relative_to(worker).as_posix():f for f in worker.rglob('*') if f.is_file() and f.suffix.lower()!='.pdb'}
for name in ('embedding.onnx','segmentation.onnx'): files['Models/Speakers/'+name]=repo/'artifacts/speaker-models'/name
for name in ('WHISPER-MODEL-LICENSE.txt','SHERPA-ONNX-LICENSE.txt','PYANNOTE-SEGMENTATION-LICENSE.txt','3D-SPEAKER-LICENSE.txt','ONNX-RUNTIME-LICENSE.txt','WHISPER-NET-LICENSE.txt','WHISPER-CPP-LICENSE.txt','NAUDIO-LICENSE.txt','DOTNET-RUNTIME-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','DOTNET-DESKTOP-LICENSE.txt'): files['licenses/'+name]=repo/'licenses'/name
assert files and 'Workers/ElkaVoicePlay.ModelWorker.exe' in files
assert not any(x in name.lower() for name in files for x in ('ggml-base.bin','ggml-medium','large-v3','cublas','cudart','nemo','/cuda/'))
target=out/'VoicePlay-transcription-0.7.0.zip'
with zipfile.ZipFile(target,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for name,source in sorted(files.items()):
        info=zipfile.ZipInfo(name,(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
        z.writestr(info,source.read_bytes())
item={'Id':'transcription','Name':'Transcribe audio','Description':'Optional multi-input transcription, loopback capture, timestamped TXT files, English translation and standard speaker labels. Uses the Base model already included with the game app.','Version':'0.7.0','Format':'zip','Assets':[{'Name':target.name,'Size':target.stat().st_size,'Sha256':sha(target)}],'Files':[{'Path':n,'Size':f.stat().st_size,'Sha256':sha(f)} for n,f in sorted(files.items())]}
catalog=[item]+json.loads((repo/'src/ElkaVoicePlay.Shared/worker-components.json').read_text())
text=json.dumps(catalog,indent=2)+'\n'
(repo/'src/ElkaVoicePlay.Shared/optional-components.json').write_text(text,encoding='utf-8')
(out/'optional-components.json').write_text(text,encoding='utf-8')
print(f'Transcription: {target.stat().st_size:,} download bytes; {sum(f.stat().st_size for f in files.values()):,} installed bytes; {len(files)} files')
