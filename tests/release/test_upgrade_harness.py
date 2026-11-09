import copy
import hashlib
import importlib.util
import json
import os
import stat
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('smoke_upgrade', ROOT / 'scripts/smoke-upgrade.py')
harness = importlib.util.module_from_spec(spec)
spec.loader.exec_module(harness)


class UpgradeHarnessTests(unittest.TestCase):
    def test_startup_guard_failure_terminates_only_new_child(self):
        binary = self.root / 'host/usr/lib/jellyfin/bin/jellyfin'
        binary.parent.mkdir(parents=True)
        binary.touch()
        for failure in ('bind', 'pid-write'):
            with self.subTest(failure=failure):
                process = mock.Mock(pid=123456)
                process.poll.return_value = None
                with mock.patch.object(harness, 'host_running', return_value=False), \
                     mock.patch.object(harness, 'port_open', return_value=False), \
                     mock.patch.object(harness.subprocess, 'Popen', return_value=process), \
                     mock.patch.object(harness, 'wait_ready'), \
                     mock.patch.object(harness, 'assert_owned_process'), \
                     mock.patch.object(harness, 'assert_loopback_bind', side_effect=SystemExit('bad bind')):
                    if failure == 'pid-write':
                        with mock.patch.object(Path, 'write_text', side_effect=OSError('pid write failed')):
                            with self.assertRaisesRegex(OSError, 'pid write failed'):
                                harness.start_host(self.root)
                    else:
                        with self.assertRaisesRegex(SystemExit, 'bad bind'):
                            harness.start_host(self.root)
                    process.terminate.assert_called_once_with()
                    process.wait.assert_called_once_with(timeout=10)
                    self.assertFalse((self.root / 'host.pid').exists())

    def test_upgrade_checks_program_data_before_install_and_expected_hash_before_start(self):
        candidate = Path(self.temporary.name) / 'candidate.zip'
        expected = self.archive(candidate)
        installed = self.root / 'data/plugins/JellyP' / harness.DLL_NAME
        installed.parent.mkdir(parents=True)
        installed.write_bytes(b'wrong installed dll')
        names = ['load_snapshot', 'assert_candidate_identity', 'start_host', 'login',
                 'verify_program_data', 'stop_host', 'install_plugin',
                 'assert_single_plugin_install', 'wait_plugin', 'assert_loaded_assembly',
                 'assert_candidate_page']
        with mock.patch.multiple(harness, **{name: mock.DEFAULT for name in names}) as mocks:
            mocks['load_snapshot'].return_value = {'baselineDllSha256': 'A' * 64}
            mocks['login'].return_value = 'test-token'
            mocks['verify_program_data'].side_effect = SystemExit('wrong data root')
            with self.assertRaisesRegex(SystemExit, 'wrong data root'):
                harness.upgrade_host(self.root, candidate, '0.3.0.1', expected)
            mocks['install_plugin'].assert_not_called()
            mocks['stop_host'].assert_not_called()
            mocks['verify_program_data'].side_effect = None
            mocks['start_host'].reset_mock()
            with self.assertRaisesRegex(SystemExit, 'DLL hash'):
                harness.upgrade_host(self.root, candidate, '0.3.0.1', expected)
            # Only the baseline is started; an unexpected installed DLL never is.
            mocks['start_host'].assert_called_once_with(self.root)
            mocks['assert_loaded_assembly'].assert_not_called()
            installed.write_bytes(b'candidate dll')
            for value in mocks.values():
                value.reset_mock()
            harness.upgrade_host(self.root, candidate, '0.3.0.1', expected)
            self.assertEqual(2, mocks['verify_program_data'].call_count)
            mocks['assert_loaded_assembly'].assert_called_once_with(self.root, installed)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'fixture'
        self.root.mkdir()
        (self.root / '.jellyp-smoke-root').write_text('')

    def archive(self, path, dll=b'candidate dll', image=True, extra=None, names=None):
        meta = {'guid': harness.GUID, 'version': '0.3.0.1'}
        members = {'LICENSE': b'license', 'meta.json': json.dumps(meta).encode(), harness.DLL_NAME: dll}
        if image:
            meta['imagePath'] = 'jellyp-plugin.png'
            members['meta.json'] = json.dumps(meta).encode()
            members['jellyp-plugin.png'] = b'image'
        if extra:
            members.update(extra)
        with zipfile.ZipFile(path, 'w') as archive:
            for name, content in (names or members).items():
                archive.writestr(name, content)
        return hashlib.sha256(dll).hexdigest().upper()

    def test_main_rejects_unmarked_root_before_stop_or_install(self):
        unmarked = Path(self.temporary.name) / 'unmarked'
        unmarked.mkdir()
        candidate = Path(self.temporary.name) / 'candidate.zip'
        expected = self.archive(candidate)
        with mock.patch.object(harness, 'stop_host') as stop, mock.patch.object(harness, 'install_plugin') as install:
            with self.assertRaisesRegex(SystemExit, 'unmarked'):
                harness.main(['--root', str(unmarked), '--phase', 'stop'])
            stop.assert_not_called()
            install.assert_not_called()
            with self.assertRaisesRegex(SystemExit, 'unmarked'):
                harness.main([
                    '--root', str(unmarked), '--phase', 'upgrade',
                    '--candidate', str(candidate), '--expected-dll-sha256', expected,
                ])
            stop.assert_not_called()
            install.assert_not_called()

    def test_guard_root_rejects_symlinked_root_marker_and_paths(self):
        target = Path(self.temporary.name) / 'target'
        target.mkdir()
        (target / '.jellyp-smoke-root').write_text('')
        link = Path(self.temporary.name) / 'link'
        link.symlink_to(target)
        with self.assertRaisesRegex(SystemExit, 'symlinked fixture root'):
            harness.guard_root(link)
        marker_root = Path(self.temporary.name) / 'marker-root'
        marker_root.mkdir()
        (marker_root / '.jellyp-smoke-root').symlink_to(target / '.jellyp-smoke-root')
        with self.assertRaisesRegex(SystemExit, 'unmarked'):
            harness.guard_root(marker_root)
        (self.root / 'data').mkdir()
        outside = Path(self.temporary.name) / 'outside'
        outside.write_text('outside')
        (self.root / 'data' / 'escape').symlink_to(outside)
        with self.assertRaisesRegex(SystemExit, 'symlinked fixture path component'):
            harness.safe_path(self.root, 'data/escape', base=self.root, must_exist=True)

    def test_install_rejects_traversal_and_extra_members_without_mutation(self):
        target = self.root / 'data/plugins/JellyP'
        target.mkdir(parents=True)
        sentinel = target / 'keep.txt'
        sentinel.write_text('keep')
        traversal = Path(self.temporary.name) / 'traversal.zip'
        self.archive(traversal, names={
            'LICENSE': b'license',
            'meta.json': json.dumps({'guid': harness.GUID, 'version': '0.3.0.1'}).encode(),
            harness.DLL_NAME: b'candidate dll',
            '../escape.txt': b'escape',
        })
        with self.assertRaisesRegex(SystemExit, 'unsafe'):
            harness.install_plugin(self.root, traversal, '0.3.0.1')
        self.assertTrue(sentinel.is_file())
        self.assertFalse((self.root / 'data/escape.txt').exists())

        extra = Path(self.temporary.name) / 'extra.zip'
        self.archive(extra, extra={'unexpected.txt': b'extra'})
        with self.assertRaisesRegex(SystemExit, 'unexpected'):
            harness.install_plugin(self.root, extra, '0.3.0.1')
        self.assertTrue(sentinel.is_file())

    def test_candidate_identity_rejects_baseline_and_wrong_hash(self):
        baseline = Path(self.temporary.name) / 'baseline.zip'
        candidate = Path(self.temporary.name) / 'candidate.zip'
        baseline_hash = self.archive(baseline, dll=b'baseline dll', image=False)
        candidate_hash = self.archive(candidate)
        harness.assert_candidate_identity(candidate, '0.3.0.1', candidate_hash, baseline_hash)
        with self.assertRaisesRegex(SystemExit, 'baseline'):
            harness.assert_candidate_identity(candidate, '0.3.0.1', candidate_hash, candidate_hash)
        with self.assertRaisesRegex(SystemExit, 'expected'):
            harness.assert_candidate_identity(candidate, '0.3.0.1', baseline_hash, baseline_hash)

    def test_main_rejects_baseline_as_candidate_before_stop_or_install(self):
        baseline = Path(self.temporary.name) / 'baseline.zip'
        baseline_hash = self.archive(baseline, dll=b'baseline dll', image=False)
        (self.root / 'upgrade-snapshot.json').write_text(json.dumps({
            'schema': harness.SCHEMA,
            'root': str(self.root.resolve()),
            'baselineDllSha256': baseline_hash,
        }))
        with mock.patch.object(harness, 'stop_host') as stop, mock.patch.object(harness, 'install_plugin') as install:
            with self.assertRaisesRegex(SystemExit, 'baseline'):
                harness.main([
                    '--root', str(self.root), '--phase', 'upgrade',
                    '--candidate', str(baseline), '--expected-dll-sha256', baseline_hash,
                ])
            stop.assert_not_called()
            install.assert_not_called()

    def test_automation_dictionary_shape_uses_globals_and_rejects_missing_keys(self):
        automation = {
            'Enabled': False, 'DryRun': True, 'PreserveOriginals': True, 'Quality': 80,
            'Lossless': False, 'ResizeImages': False, 'MaximumDimension': 2400,
            'MinimumSsim': 0.9, 'BatchSize': 10, 'ScopeMode': 'items', 'ItemIds': ['A'],
            'LibraryIds': [], 'Policies': [{
                'Category': 'Primary', 'Enabled': True, 'OverrideGlobal': False,
                'Quality': 80, 'Lossless': False, 'ResizeImages': False,
                'MaximumDimension': 2400, 'MinimumSsim': 0.9,
            }], 'SyncUnchangedPolicies': False,
        }
        globals_ = {
            'MinimumFreeSpaceBytes': 67108864, 'MaximumBackupBytes': 536870912,
            'BackupRetentionDays': 0, 'MinimumSavingsBytes': 1024, 'MinimumSavingsRatio': 0.01,
        }
        result = harness.current_automation({'Automation': automation, 'Globals': globals_})
        self.assertEqual(['a'], result['itemIds'])
        self.assertEqual(0.01, result['minimumSavingsRatio'])
        bad = {'Automation': copy.deepcopy(automation), 'Globals': copy.deepcopy(globals_)}
        del bad['Globals']['MinimumSavingsRatio']
        with self.assertRaisesRegex(SystemExit, 'missing keys'):
            harness.current_automation(bad)

    def test_fixture_process_requires_exact_argv(self):
        expected = harness.fixture_argv(self.root)
        self.assertTrue(harness.fixture_process_args(self.root, expected))
        self.assertFalse(harness.fixture_process_args(self.root, expected + ['--extra']))
        changed = list(expected)
        changed[2] = str(self.root / 'other-data')
        self.assertFalse(harness.fixture_process_args(self.root, changed))

    def test_loopback_bind_rejects_wildcard_and_other_process(self):
        (self.root / 'host.pid').write_text('1234')
        with mock.patch.object(harness, 'listening_bindings', return_value=[('tcp', '00000000', '111')]), \
             mock.patch.object(harness, 'socket_inodes', return_value={'111'}):
            with self.assertRaisesRegex(SystemExit, 'loopback'):
                harness.assert_loopback_bind(self.root)
        with mock.patch.object(harness, 'listening_bindings', return_value=[('tcp', '0100007F', '111')]), \
             mock.patch.object(harness, 'socket_inodes', return_value={'999'}):
            with self.assertRaisesRegex(SystemExit, 'another process'):
                harness.assert_loopback_bind(self.root)
        with mock.patch.object(harness, 'listening_bindings', return_value=[('tcp', '0100007F', '111'), ('tcp6', '::1', '222')]), \
             mock.patch.object(harness, 'socket_inodes', return_value={'111', '222'}):
            with self.assertRaisesRegex(SystemExit, 'exactly one'):
                harness.assert_loopback_bind(self.root)
        with mock.patch.object(harness, 'listening_bindings', return_value=[('tcp', '0100007F', '111')]), \
             mock.patch.object(harness, 'socket_inodes', return_value={'111'}):
            self.assertEqual(1234, harness.assert_loopback_bind(self.root))

    def test_network_xml_rejects_unsafe_bind(self):
        self.assertTrue(harness.validate_network_config(harness.NETWORK_XML))
        for unsafe in (
            harness.NETWORK_XML.replace('127.0.0.1', '0.0.0.0'),
            harness.NETWORK_XML.replace('<InternalHttpPort>18096</InternalHttpPort>', '<InternalHttpPort>8096</InternalHttpPort>'),
            harness.NETWORK_XML.replace('<EnableRemoteAccess>false</EnableRemoteAccess>', '<EnableRemoteAccess>true</EnableRemoteAccess>'),
        ):
            with self.assertRaises(SystemExit):
                harness.validate_network_config(unsafe)

    def test_write_private_is_0600_at_creation_and_refuses_symlink(self):
        path = Path(self.temporary.name) / 'private.txt'
        previous = os.umask(0)
        try:
            harness.write_private(path, 'secret')
        finally:
            os.umask(previous)
        self.assertEqual(0o600, stat.S_IMODE(path.stat().st_mode))
        target = Path(self.temporary.name) / 'target.txt'
        target.write_text('keep')
        link = Path(self.temporary.name) / 'link.txt'
        link.symlink_to(target)
        with self.assertRaises(SystemExit):
            harness.write_private(link, 'changed')
        self.assertEqual('keep', target.read_text())

    def test_ensure_canary_image_rejects_different_existing_and_reencode(self):
        metadata = self.root / 'data/metadata/library/aa/poster.png'
        metadata.parent.mkdir(parents=True)
        metadata.write_bytes(b'different')
        with mock.patch.object(harness, 'request', return_value=(200, [{'ImageType': 'Primary', 'Path': str(metadata)}])) as request:
            with self.assertRaisesRegex(SystemExit, 'not the harness synthetic'):
                harness.ensure_canary_image(self.root, 'token', {'Id': 'movie'})
            self.assertEqual(1, request.call_count)

        self.root.joinpath('native-png').mkdir()
        metadata.write_bytes(b'reencoded')
        responses = [(200, []), (204, b''), (200, [{'ImageType': 'Primary', 'Path': str(metadata)}])]
        with mock.patch.object(harness, 'request', side_effect=responses) as request:
            with self.assertRaisesRegex(SystemExit, 'changed the uploaded synthetic'):
                harness.ensure_canary_image(self.root, 'token', {'Id': 'movie'})
            self.assertEqual(3, request.call_count)

    def test_restore_is_not_reentrant_after_consumed_backup_or_evidence(self):
        journal = self.root / 'data/plugins/Jellyfin.Plugin.JellyP/migration/journal/record.json'
        original = self.root / 'data/plugins/Jellyfin.Plugin.JellyP/migration/originals/record.original'
        journal.parent.mkdir(parents=True)
        original.parent.mkdir(parents=True)
        original.write_bytes(b'original')
        digest = hashlib.sha256(b'original').hexdigest().upper()
        snapshot = {
            'files': {
                'journal': {'path': str(journal.relative_to(self.root)), 'content': {'Id': 'record'}},
                'original': {'path': str(original.relative_to(self.root)), 'sha256': digest},
            },
        }
        journal.write_text(json.dumps({'Id': 'record', 'State': 'RolledBack', 'BackupDeleted': True, 'SourceHash': digest}))
        with self.assertRaisesRegex(SystemExit, 'not reentrant'):
            harness.assert_restore_allowed(self.root, snapshot)

        journal.write_text(json.dumps({'Id': 'record', 'State': 'Committed', 'BackupDeleted': False, 'SourceHash': digest}))
        (self.root / 'upgrade-verify.json').write_text('{}')
        with self.assertRaisesRegex(SystemExit, 'already exists'):
            harness.assert_restore_allowed(self.root, snapshot)

    def test_restored_journal_contract(self):
        journal = self.root / 'data/plugins/Jellyfin.Plugin.JellyP/migration/journal/record.json'
        original = self.root / 'data/plugins/Jellyfin.Plugin.JellyP/migration/originals/record.original'
        journal.parent.mkdir(parents=True)
        original.parent.mkdir(parents=True)
        digest = hashlib.sha256(b'original').hexdigest().upper()
        snapshot = {
            'files': {
                'journal': {'path': str(journal.relative_to(self.root)), 'content': {'Id': 'record'}},
                'original': {'path': str(original.relative_to(self.root)), 'sha256': digest},
            },
        }
        journal.write_text(json.dumps({'Id': 'record', 'State': 'RolledBack', 'BackupDeleted': True, 'SourceHash': digest}))
        harness.assert_restored_journal(self.root, snapshot)
        journal.write_text(json.dumps({'Id': 'record', 'State': 'Committed', 'BackupDeleted': True, 'SourceHash': digest}))
        with self.assertRaisesRegex(SystemExit, 'expected RolledBack'):
            harness.assert_restored_journal(self.root, snapshot)
        journal.write_text(json.dumps({'Id': 'record', 'State': 'RolledBack', 'BackupDeleted': True, 'SourceHash': digest}))
        original.write_bytes(b'original')
        with self.assertRaisesRegex(SystemExit, 'still has a backup'):
            harness.assert_restored_journal(self.root, snapshot)


if __name__ == '__main__':
    unittest.main()
