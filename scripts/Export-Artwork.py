"""Format-only exports: retain the generated artwork without cropping or repainting."""
from pathlib import Path
from PIL import Image
import json, shutil
repo=Path(__file__).resolve().parents[1]
graphics=repo/'graphics'; assets=repo/'src/ElkaVoicePlay.App/Assets'
manifest=[]
for orientation in ('horizontal','vertical'):
    image=Image.open(graphics/f'voiceplay-{orientation}.png').convert('RGB')
    target=graphics/f'voiceplay-{orientation}-under-1mb.jpg'
    for quality in range(94,49,-2):
        image.save(target,format='JPEG',quality=quality,optimize=True,progressive=True)
        if target.stat().st_size < 1_000_000: break
    assert target.stat().st_size<1_000_000
    manifest.append({'File':target.name,'Bytes':target.stat().st_size,'Pixels':image.size,'JPEGQuality':quality})
icon=Image.open(graphics/'voiceplay-icon.png').convert('RGBA')
icon.save(graphics/'ElkaVoicePlay.ico',format='ICO',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
icon.resize((256,256),Image.Resampling.LANCZOS).save(assets/'ElkaVoicePlay.png')
shutil.copyfile(graphics/'ElkaVoicePlay.ico',assets/'ElkaVoicePlay.ico')
(graphics/'exports.json').write_text(json.dumps(manifest,indent=2)+'\n')
for item in manifest: print(item)
print('ICO sizes:',Image.open(graphics/'ElkaVoicePlay.ico').ico.sizes())
