"""Offline extraction tests: reject corrupt local objects and preserve exact jar bytes."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import hashlib
import zipfile

class AssetTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('extract_mace', Path(__file__).parents[1] / 'tools/extract-assets.py')
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
    def test_verified_object_rejects_tampering(self):
        with tempfile.TemporaryDirectory() as temp:
            base=Path(temp); raw=b'Minecraft cached sound'; digest=hashlib.sha1(raw).hexdigest()
            target=base/digest[:2]/digest; target.parent.mkdir(); target.write_bytes(raw)
            self.assertEqual(self.module.verified_object(base, {'hash':digest}), target)
            target.write_bytes(b'corrupted')
            with self.assertRaises(ValueError): self.module.verified_object(base, {'hash':digest})
    def test_texture_is_copied_exactly(self):
        with tempfile.TemporaryDirectory() as temp:
            base=Path(temp); jar=base/'minecraft.jar'; name='assets/minecraft/textures/item/mace.png'; raw=b'original PNG bytes'
            with zipfile.ZipFile(jar,'w') as z: z.writestr(name,raw)
            self.assertEqual(self.module.read_texture([jar],name), (raw,jar))

if __name__=='__main__': unittest.main()
