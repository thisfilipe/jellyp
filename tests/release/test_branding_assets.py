import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('audit_publication', ROOT / 'scripts/audit-publication.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class BrandingAssetTests(unittest.TestCase):
    def test_installed_plugin_uses_the_approved_catalog_artwork(self):
        self.assertEqual(
            (ROOT / 'assets/branding/jellyp-catalog.png').read_bytes(),
            (ROOT / 'assets/branding/jellyp-plugin.png').read_bytes(),
        )

    def test_only_the_nine_approved_pngs_are_hash_allowlisted(self):
        files = {str(path.relative_to(ROOT)) for folder in ('assets/branding', 'Jellyfin.Plugin.JellyP/Web/branding') for path in (ROOT / folder).rglob('*.png')}
        self.assertEqual(9, len(files))
        self.assertEqual(files, set(audit.BRANDING_SHA256))
        for name in files:
            with self.subTest(name=name):
                self.assertTrue(audit.approved_branding(name, (ROOT / name).read_bytes()))

    def test_unreviewed_modified_or_non_png_binary_is_not_approved(self):
        name = 'assets/branding/jellyp-catalog.png'
        data = (ROOT / name).read_bytes()
        self.assertFalse(audit.approved_branding('assets/branding/unreviewed.png', data))
        self.assertFalse(audit.approved_branding(name, data + b'changed'))
        self.assertFalse(audit.approved_branding(name, b'not a PNG'))
