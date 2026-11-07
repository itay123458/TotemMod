"""Extract real Minecraft resources from this user's installation; never downloads."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile


def verified_object(objects, entry):
    digest = entry['hash']
    path = objects / digest[:2] / digest
    if not path.is_file():
        return None
    if hashlib.sha1(path.read_bytes()).hexdigest() != digest:
        raise ValueError('Local Minecraft object failed its SHA-1 asset check: ' + str(path))
    return path


def read_texture(jars, name):
    for jar in jars:
        with zipfile.ZipFile(jar) as archive:
            if name in archive.namelist():
                return archive.read(name), jar
    raise FileNotFoundError('No local Minecraft jar contains ' + name)


def main():
    root = Path(__file__).resolve().parents[1]
    mc = Path(os.environ['APPDATA']) / '.minecraft'
    out = root / 'assets'
    out.mkdir(exist_ok=True)
    jars = sorted((mc / 'versions').glob('*/*.jar'), reverse=True)
    # Prefer the user's stable 1.21 item artwork when present.
    preferred = mc / 'versions/1.21.11/1.21.11.jar'
    if preferred in jars:
        jars.remove(preferred)
        jars.insert(0, preferred)
    manifest = {'textures': {}, 'sounds': {}, 'usage': 'Personal local installation only. Minecraft assets belong to Mojang/Microsoft; do not redistribute the extracted assets.'}
    textures = {'mace.png': 'item/mace.png', 'glint.png': 'misc/enchanted_glint_item.png',
                'critical_hit.png': 'particle/critical_hit.png', 'gust.png': 'particle/gust_0.png'}
    for output, texture in textures.items():
        name = 'assets/minecraft/textures/' + texture
        data, jar = read_texture(jars, name)
        (out / output).write_bytes(data)
        manifest['textures'][output] = {'source': str(jar), 'entry': name, 'sha256': hashlib.sha256(data).hexdigest()}
    sounds = {'hit.pcm': 'minecraft/sounds/item/mace/smash_air1.ogg',
              'smash.pcm': 'minecraft/sounds/item/mace/smash_ground_heavy.ogg',
              'burst.pcm': 'minecraft/sounds/entity/wind_charge/wind_burst1.ogg'}
    indexes = sorted((mc / 'assets/indexes').glob('*.json'), reverse=True)
    for output, name in sounds.items():
        for index in indexes:
            entry = json.loads(index.read_text(encoding='utf-8'))['objects'].get(name)
            if not entry:
                continue
            source = verified_object(mc / 'assets/objects', entry)
            if source is None:
                continue
            subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', str(source),
                            '-ar', '44100', '-ac', '1', '-f', 'f32le', str(out / output)], check=True)
            manifest['sounds'][output] = {'source': str(source), 'index': str(index), 'entry': name,
                                         'sha1': entry['hash'], 'sample_rate': 44100, 'channels': 1,
                                         'samples': (out / output).stat().st_size // 4}
            break
        else:
            raise FileNotFoundError('Minecraft sound is absent from the local asset cache: ' + name)
    (out / 'provenance.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__':
    main()
