"""Offline cache-integrity and extracted original-asset checks."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('survival_extractor', ROOT / 'tools/extract-survival-assets.py')
extractor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(extractor)


class CacheVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.mc = Path(self.temporary.name)
        self.data = b'local Minecraft cached sound'
        self.digest = hashlib.sha1(self.data).hexdigest()
        self.path = self.mc / 'assets/objects' / self.digest[:2] / self.digest
        self.path.parent.mkdir(parents=True)
        self.path.write_bytes(self.data)
        self.entry = {'hash': self.digest, 'size': len(self.data)}

    def test_valid_content_addressed_object(self):
        self.assertEqual(self.path, extractor.verified_cache(self.mc, 'index.json', self.entry))

    def test_corruption_rejected(self):
        self.path.write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError, 'verification failed'):
            extractor.verified_cache(self.mc, 'index.json', self.entry)

    def test_size_mismatch_rejected(self):
        self.entry['size'] += 1
        with self.assertRaises(ValueError):
            extractor.verified_cache(self.mc, 'index.json', self.entry)

    def test_missing_cache_is_optional(self):
        self.path.unlink()
        self.assertIsNone(extractor.verified_cache(self.mc, 'index.json', self.entry))

    def test_invalid_hash_cannot_escape_cache(self):
        for bad in ('../../outside', 'x' * 40, 'A' * 40):
            with self.assertRaises(ValueError):
                extractor.verified_cache(self.mc, 'index.json', {'hash': bad})


class LocalExtractionTests(unittest.TestCase):
    def test_provenance_matches_original_client_and_decoded_assets(self):
        out = ROOT / 'assets/survival'
        if not (out / 'provenance.json').exists():
            self.skipTest('Run tools/extract-survival-assets.py against the local install first')
        provenance = json.loads((out / 'provenance.json').read_text())
        jar = Path(provenance['texture_source'])
        self.assertEqual(extractor.sha256(jar.read_bytes()), provenance['jar_sha256'])
        self.assertEqual(set(extractor.TEXTURES), set(provenance['textures']))
        with zipfile.ZipFile(jar) as archive:
            for name, item in provenance['textures'].items():
                data = (out / name).read_bytes()
                self.assertEqual(data, archive.read(item['entry']), name)
                self.assertEqual(extractor.sha256(data), item['sha256'], name)
        for name, item in provenance['sounds'].items():
            if 'missing' in item:
                self.assertFalse((out / (name + '.pcm')).exists())
                continue
            source = Path(item['source']).read_bytes()
            self.assertEqual(hashlib.sha1(source).hexdigest(), item['source_sha1'])
            self.assertEqual(source, (out / (name + '.ogg')).read_bytes())
            self.assertEqual(extractor.sha256(source), item['ogg_sha256'])
            if 'pcm_sha256' in item:
                pcm = (out / (name + '.pcm')).read_bytes()
                self.assertGreater(len(pcm), 0)
                self.assertEqual(len(pcm) % 4, 0)
                self.assertEqual(extractor.sha256(pcm), item['pcm_sha256'])


if __name__ == '__main__':
    unittest.main()
