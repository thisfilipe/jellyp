import copy
import hashlib
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts'))
import catalog
import package


class CatalogTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.meta = package.metadata()
        self.archive = Path(self.temporary.name) / f"JellyP_{self.meta['version']}.zip"
        self.write_archive()

    def write_archive(self, **extra):
        with zipfile.ZipFile(self.archive, 'w') as archive:
            archive.writestr(package.DLL_NAME, b'fictional compiled fixture')
            archive.writestr('LICENSE', 'fictional license fixture')
            archive.writestr('meta.json', json.dumps(self.meta))
            for name, content in extra.items():archive.writestr(name, content)

    def test_catalog_hashes_exact_zip_and_uses_versioned_release_url(self):
        result = catalog.update([], self.archive, '2026-01-01T00:00:00Z')
        release = result[0]['versions'][0]
        self.assertEqual(hashlib.md5(self.archive.read_bytes(), usedforsecurity=False).hexdigest(), release['checksum'])
        self.assertEqual(f"https://github.com/{package.REPOSITORY}/releases/download/v{self.meta['version']}/{self.archive.name}", release['sourceUrl'])
        self.assertEqual(self.meta['targetAbi'], release['targetAbi'])
        self.assertTrue(release['changelog'].strip())

    def test_existing_release_is_idempotent_and_keeps_timestamp(self):
        previous = catalog.update([], self.archive, '2026-01-01T00:00:00Z')
        self.assertEqual(previous, catalog.update(previous, self.archive, '2026-01-02T00:00:00Z'))

    def test_modified_published_release_is_rejected(self):
        previous = catalog.update([], self.archive, '2026-01-01T00:00:00Z')
        previous[0]['versions'][0]['checksum'] = '0' * 32
        with self.assertRaisesRegex(ValueError, 'immutable'):
            catalog.update(previous, self.archive, '2026-01-02T00:00:00Z')

    def test_old_and_newer_catalog_entries_are_preserved_and_sorted_numerically(self):
        previous = catalog.update([], self.archive, '2026-01-01T00:00:00Z')
        for version in ('0.0.2.0', '0.0.10.0'):
            entry = copy.deepcopy(previous[0]['versions'][0]);entry['version'] = version
            previous[0]['versions'].append(entry)
        versions = catalog.update(previous, self.archive, '2026-01-02T00:00:00Z')[0]['versions']
        self.assertEqual([self.meta['version'], '0.0.10.0', '0.0.2.0'], [v['version'] for v in versions])

    def test_different_plugin_identity_is_rejected(self):
        previous = catalog.update([], self.archive, '2026-01-01T00:00:00Z')
        previous[0]['guid'] = 'not-this-plugin'
        with self.assertRaisesRegex(ValueError, 'identity'):
            catalog.update(previous, self.archive, '2026-01-02T00:00:00Z')

    def test_duplicate_versions_are_rejected(self):
        previous = catalog.update([], self.archive, '2026-01-01T00:00:00Z')
        previous[0]['versions'] *= 2
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            catalog.update(previous, self.archive, '2026-01-02T00:00:00Z')

    def test_unexpected_archive_members_are_rejected(self):
        self.write_archive(**{'private.txt': 'must not be released'})
        with self.assertRaisesRegex(ValueError, 'archive'):
            catalog.update([], self.archive, '2026-01-01T00:00:00Z')

    def test_archive_metadata_must_match_source_version(self):
        self.meta['version'] = '0.0.0.1';self.write_archive()
        with self.assertRaisesRegex(ValueError, 'metadata'):
            catalog.update([], self.archive, '2026-01-01T00:00:00Z')

    def test_timestamp_requires_utc(self):
        with self.assertRaisesRegex(ValueError, 'UTC'):
            catalog.update([], self.archive, '2026-01-01T00:00:00+03:00')


if __name__ == '__main__':unittest.main()
