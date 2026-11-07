"""Extract assets from the local Minecraft installation without downloading them."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
mc = Path(os.environ['APPDATA']) / '.minecraft'
out = root / 'assets'
out.mkdir(exist_ok=True)
texture = 'assets/minecraft/textures/item/totem_of_undying.png'
for jar in sorted((mc / 'versions').glob('*/*.jar'), reverse=True):
    with zipfile.ZipFile(jar) as archive:
        if texture in archive.namelist():
            data = archive.read(texture)
            (out / 'totem.png').write_bytes(data)
            texture_source = str(jar)
            break
else:
    raise SystemExit('No local Minecraft jar contains the totem texture.')
for index in (mc / 'assets/indexes').glob('*.json'):
    entry = json.loads(index.read_text())['objects'].get('minecraft/sounds/item/totem/use_totem.ogg')
    if entry:
        sound = mc / 'assets/objects' / entry['hash'][:2] / entry['hash']
        if sound.exists():
            if hashlib.sha1(sound.read_bytes()).hexdigest() != entry['hash']:
                raise SystemExit('Local Minecraft sound failed its asset hash check.')
            subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(sound),
                            '-ar', '44100', '-ac', '1', '-f', 'f32le', str(out / 'pop.pcm')], check=True)
            break
else:
    raise SystemExit('Minecraft totem sound is not present in the local asset cache.')
manifest = {'texture_source': texture_source, 'texture_entry': texture,
            'texture_sha256': hashlib.sha256(data).hexdigest(), 'sound_source': str(sound),
            'sound_sha1': entry['hash'], 'sample_rate': 44100, 'channels': 1,
            'samples': (out / 'pop.pcm').stat().st_size // 4,
            'usage': 'Personal local installation only; Minecraft assets belong to Mojang/Microsoft.'}
(out / 'provenance.json').write_text(json.dumps(manifest, indent=2))
print(json.dumps(manifest, indent=2))
