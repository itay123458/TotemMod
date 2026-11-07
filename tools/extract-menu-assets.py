"""Extract menu textures and the button click from the local Minecraft install."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
mc = Path(os.environ['APPDATA']) / '.minecraft'
out = root / 'assets/menu'
out.mkdir(parents=True, exist_ok=True)
entries = {
    'background.png': 'gui/menu_background.png',
    'dirt.png': 'block/dirt.png',
    'button.png': 'gui/sprites/widget/button.png',
    'button-highlighted.png': 'gui/sprites/widget/button_highlighted.png',
    'button-disabled.png': 'gui/sprites/widget/button_disabled.png',
    'slider.png': 'gui/sprites/widget/slider.png',
    'slider-highlighted.png': 'gui/sprites/widget/slider_highlighted.png',
    'handle.png': 'gui/sprites/widget/slider_handle.png',
    'handle-highlighted.png': 'gui/sprites/widget/slider_handle_highlighted.png',
}
for jar in sorted((mc / 'versions').glob('*/*.jar'), reverse=True):
    with zipfile.ZipFile(jar) as archive:
        paths = {name: 'assets/minecraft/textures/' + entry for name, entry in entries.items()}
        if not all(path in archive.namelist() for path in paths.values()):
            continue
        hashes = {}
        for name, path in paths.items():
            data = archive.read(path)
            (out / name).write_bytes(data)
            hashes[name] = hashlib.sha256(data).hexdigest()
        source_jar = str(jar)
        break
else:
    raise SystemExit('No local Minecraft client contains the menu sprite set.')

for index in (mc / 'assets/indexes').glob('*.json'):
    obj = json.loads(index.read_text())['objects'].get('minecraft/sounds/random/click.ogg')
    if obj:
        sound = mc / 'assets/objects' / obj['hash'][:2] / obj['hash']
        if sound.is_file():
            if hashlib.sha1(sound.read_bytes()).hexdigest() != obj['hash']:
                raise SystemExit('Minecraft click sound hash did not match its index.')
            subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(sound),
                            '-ar', '22050', '-ac', '1', '-f', 'f32le', str(out / 'click.pcm')], check=True)
            break
else:
    raise SystemExit('Minecraft click sound is missing from the local asset cache.')

manifest = {'texture_source': source_jar, 'textures': entries, 'texture_sha256': hashes,
            'sound_source': str(sound), 'sound_sha1': obj['hash'], 'sample_rate': 22050,
            'channels': 1, 'usage': 'Personal local installation; Minecraft assets belong to Mojang/Microsoft.'}
(out / 'provenance.json').write_text(json.dumps(manifest, indent=2))
print('Extracted 9 Minecraft menu textures and button click from the local installation.')
