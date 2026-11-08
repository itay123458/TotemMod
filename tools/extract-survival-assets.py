"""Extract survival UI assets from an installed Minecraft client, never the network."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

TEXTURES = {
    'heart_container.png': 'gui/sprites/hud/heart/container.png',
    'heart_full.png': 'gui/sprites/hud/heart/full.png',
    'heart_half.png': 'gui/sprites/hud/heart/half.png',
    'food_empty.png': 'gui/sprites/hud/food_empty.png',
    'food_full.png': 'gui/sprites/hud/food_full.png',
    'food_half.png': 'gui/sprites/hud/food_half.png',
    'hotbar.png': 'gui/sprites/hud/hotbar.png',
    'hotbar_selection.png': 'gui/sprites/hud/hotbar_selection.png',
    'offhand.png': 'gui/sprites/hud/hotbar_offhand_left.png',
    'inventory.png': 'gui/container/inventory.png',
    'apple.png': 'item/apple.png',
    'bread.png': 'item/bread.png',
    'golden_apple.png': 'item/golden_apple.png',
}
SOUNDS = {
    'click': ('minecraft/sounds/random/click.ogg',),
    'eat': ('minecraft/sounds/random/eat1.ogg', 'minecraft/sounds/random/eat2.ogg'),
    'burp': ('minecraft/sounds/random/burp.ogg',),
    'hurt': ('minecraft/sounds/game/player/hurt1.ogg', 'minecraft/sounds/damage/hit1.ogg'),
}


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def verified_cache(mc, index, entry):
    """Verify the content-addressed Mojang cache before decoding any sound."""
    digest = entry['hash']
    if len(digest) != 40 or any(c not in '0123456789abcdef' for c in digest):
        raise ValueError('Invalid Minecraft cache hash in ' + str(index))
    path = mc / 'assets/objects' / digest[:2] / digest
    if not path.is_file():
        return None
    data = path.read_bytes()
    if hashlib.sha1(data).hexdigest() != digest or len(data) != entry.get('size', len(data)):
        raise ValueError('Minecraft cache verification failed: ' + str(path))
    return path


def extract(mc, out):
    out.mkdir(parents=True, exist_ok=True)
    manifest = {'textures': {}, 'sounds': {}, 'sample_rate': 22050, 'channels': 1,
                'pcm_format': 'f32le', 'usage': 'Local installation only. Minecraft assets belong to Mojang/Microsoft.'}
    jars = sorted((mc / 'versions').glob('*/*.jar'), key=lambda p: p.stat().st_mtime, reverse=True)
    for jar in jars:
        with zipfile.ZipFile(jar) as archive:
            paths = {name: 'assets/minecraft/textures/' + path for name, path in TEXTURES.items()}
            if not all(path in archive.namelist() for path in paths.values()):
                continue
            manifest['texture_source'] = str(jar)
            manifest['jar_sha256'] = sha256(jar.read_bytes())
            for name, entry in paths.items():
                data = archive.read(entry)
                (out / name).write_bytes(data)
                manifest['textures'][name] = {'entry': entry, 'sha256': sha256(data)}
            break
    else:
        raise ValueError('No installed Minecraft client contains the survival sprite set.')
    indexes = [(p, json.loads(p.read_text(encoding='utf-8'))['objects'])
               for p in sorted((mc / 'assets/indexes').glob('*.json'), key=lambda p: p.stat().st_mtime, reverse=True)]
    ffmpeg = shutil.which('ffmpeg')
    for name, candidates in SOUNDS.items():
        found = False
        for index, objects in indexes:
            for key in candidates:
                entry = objects.get(key)
                if entry is None:
                    continue
                source = verified_cache(mc, index, entry)
                if source is None:
                    continue
                ogg = out / (name + '.ogg')
                ogg.write_bytes(source.read_bytes())
                info = {'index': str(index), 'entry': key, 'source': str(source),
                        'source_sha1': entry['hash'], 'ogg_sha256': sha256(ogg.read_bytes())}
                pcm = out / (name + '.pcm')
                if ffmpeg:
                    subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-i', str(ogg),
                                    '-ar', '22050', '-ac', '1', '-f', 'f32le', str(pcm)], check=True)
                    info['pcm_sha256'] = sha256(pcm.read_bytes())
                else:
                    pcm.unlink(missing_ok=True)
                    info['decode'] = 'ffmpeg unavailable; runtime audio optional'
                manifest['sounds'][name] = info
                found = True
                break
            if found:
                break
        if not found:
            (out / (name + '.pcm')).unlink(missing_ok=True)
            (out / (name + '.ogg')).unlink(missing_ok=True)
            manifest['sounds'][name] = {'missing': 'Not present in verified local Minecraft cache'}
    (out / 'provenance.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--minecraft', type=Path, default=Path(os.environ.get('APPDATA', '.')) / '.minecraft')
    parser.add_argument('--out', type=Path, default=Path(__file__).resolve().parents[1] / 'assets/survival')
    args = parser.parse_args()
    result = extract(args.minecraft, args.out)
    print('Extracted %s textures and %s cached sound records.' % (len(result['textures']), len(result['sounds'])))
