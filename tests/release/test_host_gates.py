import contextlib
import importlib.util
import io
import json
import os
import signal
import stat
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('smoke_host_gates', ROOT / 'scripts/smoke-host-gates.py')
gates = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gates)


class HostGateHelperTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'fixture'
        self.root.mkdir()
        (self.root / '.jellyp-smoke-root').write_text('')

    def test_parse_stat_starttime_uses_field_22_after_comm(self):
        stat = '123 (comm with space) ' + ' '.join(['S'] + [str(value) for value in range(1, 19)] + ['8952892'])
        self.assertEqual('8952892', gates.parse_stat_starttime(stat))

    def test_select_ordered_pair_requires_target_before_barrier(self):
        target = {'Id': '11111111222233334444555566667777'}
        barrier = {'Id': '888888889999aaaabbbbccccddddeeee'}
        self.assertEqual((target, barrier), gates.select_ordered_pair([target], [barrier]))
        with self.assertRaises(gates.GateFailure):
            gates.select_ordered_pair([barrier], [target])

    def test_state_hash_normalizes_guid_dashes(self):
        state = {'hashes': {'34e0745c6b2bf962a5797b70e0194388': 'ABC'}}
        self.assertEqual('ABC', gates.state_hash(state, '34e0745c-6b2b-f962-a579-7b70e0194388'))

    def test_journal_for_item_normalizes_source_item_id(self):
        journal = self.root / 'data/plugins/Jellyfin.Plugin.JellyP/migration/journal'
        journal.mkdir(parents=True)
        record = {'Id': 'record', 'Source': {'ItemId': '34e0745c-6b2b-f962-a579-7b70e0194388'}, 'State': 'Prepared'}
        (journal / 'record.json').write_text(json.dumps(record))
        matches = gates.journal_for_item(self.root, '34e0745c6b2bf962a5797b70e0194388')
        self.assertEqual(1, len(matches))
        self.assertEqual(record, matches[0]['record'])

    def test_committed_output_requires_state_hashes_and_webp(self):
        journal = self.root / 'data/plugins/Jellyfin.Plugin.JellyP/migration/journal'
        journal.mkdir(parents=True)
        record = {
            'Id': 'record',
            'Source': {'ItemId': '34e0745c-6b2b-f962-a579-7b70e0194388'},
            'State': 'Committed',
            'SourceHash': 'A' * 64,
            'OutputHash': 'B' * 64,
            'OutputPath': '/tmp/output.webp',
            'BackupDeleted': False,
        }
        (journal / 'record.json').write_text(json.dumps(record))
        self.assertEqual(record, gates.committed_output(
            self.root, '34e0745c6b2bf962a5797b70e0194388', 'A' * 64, 'B' * 64))
        with self.assertRaises(gates.GateFailure):
            gates.committed_output(self.root, '34e0745c6b2bf962a5797b70e0194388', 'A' * 64, 'C' * 64)
        record['OutputPath'] = '/tmp/output.png'
        (journal / 'record.json').write_text(json.dumps(record))
        with self.assertRaises(gates.GateFailure):
            gates.committed_output(self.root, '34e0745c6b2bf962a5797b70e0194388', 'A' * 64, 'B' * 64)

    def test_set_slow_delay_is_private_and_bounded(self):
        gates.set_slow_delay(self.root, 90)
        path = self.root / gates.SLOW_CONTROL_NAME
        self.assertEqual('90\n', path.read_text())
        self.assertEqual(0o600, stat.S_IMODE(path.stat().st_mode))
        gates.set_slow_delay(self.root, 0)
        self.assertEqual('0\n', path.read_text())
        with self.assertRaises(gates.GateFailure):
            gates.set_slow_delay(self.root, 601)

    def test_inotify_chunk_keeps_second_valid_line_from_one_read(self):
        rest, names = gates.inotify_event_names(b'', b'first.tmp\nsecond.json\n')
        self.assertEqual(b'', rest)
        self.assertEqual(['second.json'], names)
        rest, names = gates.inotify_event_names(b'par', b'tial.json\nnext')
        self.assertEqual(b'next', rest)
        self.assertEqual(['partial.json'], names)

    def test_v15_prekill_proof_rejects_missing_output_committed_journal_and_hash_mismatch(self):
        record = {
            'Id': 'record',
            'State': 'Prepared',
            'SourceHash': 'A' * 64,
            'OutputHash': 'B' * 64,
            'OutputPath': str(self.root / 'poster.webp'),
            'BackupDeleted': False,
        }
        output = {'path': str(self.root / 'poster.webp'), 'exists': True, 'hash': 'B' * 64,
                  'bytes': 1, 'isSymlink': False}
        source = {'path': str(self.root / 'poster.png'), 'exists': True, 'hash': 'A' * 64,
                  'bytes': 1, 'isSymlink': False}
        backup = {'path': str(self.root / 'record.original'), 'exists': True, 'hash': 'A' * 64,
                  'bytes': 1, 'isSymlink': False}
        proof = dict(gates.v15_prekill_proof(record, output, source, backup, 'A' * 64))
        self.assertTrue(all(proof.values()))
        missing = dict(gates.v15_prekill_proof(
            record, {'path': output['path'], 'exists': False, 'hash': None}, source, backup, 'A' * 64))
        self.assertFalse(missing['V15 output exists before SIGKILL'])
        self.assertFalse(missing['V15 output hash matches the Prepared journal OutputHash before SIGKILL'])
        committed = dict(gates.v15_prekill_proof(
            {**record, 'State': 'Committed'}, output, source, backup, 'A' * 64))
        self.assertFalse(committed['V15 journal was Prepared before SIGKILL'])
        wrong_output = dict(gates.v15_prekill_proof(
            {**record, 'OutputHash': 'C' * 64}, output, source, backup, 'A' * 64))
        self.assertFalse(wrong_output['V15 output hash matches the Prepared journal OutputHash before SIGKILL'])
        wrong_backup = dict(gates.v15_prekill_proof(
            record, output, source, {**backup, 'hash': 'D' * 64}, 'A' * 64))
        self.assertFalse(wrong_backup['V15 retained original has the exact synthetic source hash before kill'])

    def test_arm_image_barrier_writes_private_folders_and_clears_stale_markers(self):
        source = self.root / 'data/metadata/library/aa/item/poster.png'
        source.parent.mkdir(parents=True)
        source.write_bytes(b'png')
        barrier_path = self.root / gates.IMAGE_BARRIER_NAME
        barrier_path.write_text('stale')
        reached = self.root / (gates.IMAGE_BARRIER_NAME + '.reached')
        release = self.root / (gates.IMAGE_BARRIER_NAME + '.release')
        reached.write_text('stale')
        release.write_text('stale')
        barrier, reached, release, folders = gates.arm_image_barrier(self.root, [str(source)])
        self.assertEqual([str(source.parent.resolve())], folders)
        self.assertEqual(str(source.parent.resolve()) + '\n', barrier.read_text())
        self.assertEqual(0o600, stat.S_IMODE(barrier.stat().st_mode))
        self.assertFalse(reached.exists())
        self.assertFalse(release.exists())
        reached.write_text('evidence')
        gates.release_image_barrier(release)
        self.assertTrue(release.is_file())
        self.assertEqual(0o600, stat.S_IMODE(release.stat().st_mode))
        gates.release_image_barrier(release)
        self.assertEqual('release\n', release.read_text())
        gates.disarm_image_barrier(self.root)
        self.assertFalse(barrier.exists())
        self.assertTrue(reached.is_file())

    def test_arm_image_barrier_rejects_empty_source_list(self):
        with self.assertRaises(gates.GateFailure):
            gates.arm_image_barrier(self.root, [])

    def test_pidfd_guard_failure_sends_no_stop(self):
        control = gates.PidfdControl(self.root, 123, '1', 7)
        with mock.patch.object(gates, 'assert_process_guard', side_effect=gates.GateFailure('changed')), \
             mock.patch.object(gates.signal, 'pidfd_send_signal') as send:
            with self.assertRaises(gates.GateFailure):
                control.stop()
            send.assert_not_called()
            self.assertFalse(control.stop_sent)

    def test_pidfd_stop_kill_and_close_never_cont_after_kill(self):
        control = gates.PidfdControl(self.root, 123, '1', 7)
        with mock.patch.object(gates, 'assert_process_guard'), \
             mock.patch.object(gates.signal, 'pidfd_send_signal') as send, \
             mock.patch.object(gates.os, 'close') as close:
            control.stop()
            control.kill()
            control.close()
            self.assertEqual([mock.call(7, signal.SIGSTOP), mock.call(7, signal.SIGKILL)], send.call_args_list)
            close.assert_called_once_with(7)

    def test_pidfd_close_cont_after_stop(self):
        control = gates.PidfdControl(self.root, 123, '1', 7)
        with mock.patch.object(gates, 'assert_process_guard'), \
             mock.patch.object(gates.signal, 'pidfd_send_signal') as send, \
             mock.patch.object(gates.os, 'close'):
            control.stop()
            control.close()
            self.assertEqual([mock.call(7, signal.SIGSTOP), mock.call(7, signal.SIGCONT)], send.call_args_list)

    def test_pidfd_kill_failure_auto_cont(self):
        control = gates.PidfdControl(self.root, 123, '1', 7)
        with mock.patch.object(gates, 'assert_process_guard'), \
             mock.patch.object(gates.signal, 'pidfd_send_signal',
                               side_effect=[None, gates.GateFailure('kill failed'), None]) as send, \
             mock.patch.object(gates.os, 'close'):
            control.stop()
            with self.assertRaises(gates.GateFailure):
                control.kill()
            control.close()
            self.assertEqual([
                mock.call(7, signal.SIGSTOP),
                mock.call(7, signal.SIGKILL),
                mock.call(7, signal.SIGCONT),
            ], send.call_args_list)

    def test_restart_calls_verified_stop_before_start_and_checks_new_identity(self):
        expected = 'A' * 64
        order = []
        with mock.patch.object(gates.h, 'stop_host', side_effect=lambda root: order.append('stop')), \
             mock.patch.object(gates.h, 'start_host', side_effect=lambda root: order.append('start')), \
             mock.patch.object(gates.h, 'port_open', return_value=False), \
             mock.patch.object(gates, 'assert_process_guard', return_value=(222, 'b')), \
             mock.patch.object(gates, 'login_verified', return_value=('token', {'Version': '12.1.0'})), \
             mock.patch.object(gates.h, 'wait_plugin', return_value={'Status': 'Active'}), \
             mock.patch.object(gates.h, 'assert_single_plugin_install', return_value=Path('/tmp/plugin.dll')), \
             mock.patch.object(gates.h, 'sha256', return_value=expected), \
             mock.patch.object(gates.h, 'assert_loaded_assembly'), \
             mock.patch.object(gates.h, 'assert_candidate_page'), \
             mock.patch.object(gates, 'test_plugin_status', return_value={'Status': 'Active'}):
            result = gates.restart_same_candidate(
                self.root, expected, previous_identity=(111, 'a'), require_test_plugin=True)
            self.assertEqual(['stop', 'start'], order)
            self.assertEqual((222, 'b'), (result['pid'], result['starttime']))

    def test_restart_rejects_same_identity_and_inactive_test_plugin(self):
        expected = 'A' * 64
        with mock.patch.object(gates.h, 'stop_host'), mock.patch.object(gates.h, 'start_host'), \
             mock.patch.object(gates.h, 'port_open', return_value=False), \
             mock.patch.object(gates, 'assert_process_guard', return_value=(111, 'a')):
            with self.assertRaises(gates.GateFailure):
                gates.restart_same_candidate(self.root, expected, previous_identity=(111, 'a'))
        with mock.patch.object(gates.h, 'stop_host'), mock.patch.object(gates.h, 'start_host'), \
             mock.patch.object(gates.h, 'port_open', return_value=False), \
             mock.patch.object(gates, 'assert_process_guard', return_value=(222, 'b')), \
             mock.patch.object(gates, 'login_verified', return_value=('token', {'Version': '12.1.0'})), \
             mock.patch.object(gates.h, 'wait_plugin', return_value={'Status': 'Active'}), \
             mock.patch.object(gates.h, 'assert_single_plugin_install', return_value=Path('/tmp/plugin.dll')), \
             mock.patch.object(gates.h, 'sha256', return_value=expected), \
             mock.patch.object(gates.h, 'assert_loaded_assembly'), \
             mock.patch.object(gates.h, 'assert_candidate_page'), \
             mock.patch.object(gates, 'test_plugin_status', return_value={'Status': 'Malfunctioned'}):
            with self.assertRaises(gates.GateFailure):
                gates.restart_same_candidate(self.root, expected, require_test_plugin=True)

    def test_generate_movie_refuses_symlink_and_outside_path(self):
        outside = Path(self.temporary.name) / 'outside'
        outside.mkdir()
        (self.root / 'host-gates-media').symlink_to(outside, target_is_directory=True)
        with self.assertRaises(SystemExit):
            gates.generate_movie(self.root, 'host-gates-media/movie.mp4')
        self.assertEqual([], list(outside.iterdir()))
        with self.assertRaises(SystemExit):
            gates.generate_movie(self.root, '../outside/movie.mp4')

    def test_private_bytes_refuses_symlink_and_leaves_outside_untouched(self):
        outside = Path(self.temporary.name) / 'outside.bin'
        outside.write_bytes(b'original')
        (self.root / 'evil').symlink_to(outside)
        with self.assertRaises(gates.GateFailure):
            gates.write_private_bytes(self.root / 'evil', b'changed')
        self.assertEqual(b'original', outside.read_bytes())
        gates.write_private_bytes(self.root / 'private.bin', b'data')
        self.assertEqual(0o600, stat.S_IMODE((self.root / 'private.bin').stat().st_mode))
        self.assertEqual({'exists': False, 'isSymlink': True, 'path': str(self.root / 'evil'), 'hash': None, 'bytes': 0},
                         gates.file_probe(self.root / 'evil'))

    def test_reset_safe_automation_reauths_on_401_and_verifies_readback(self):
        apply_settings = {
            'Automation': {'Enabled': True, 'DryRun': False, 'PreserveOriginals': True,
                           'ScopeMode': 'items', 'ItemIds': ['id'], 'LibraryIds': []},
        }
        safe_settings = {
            'Automation': {'Enabled': False, 'DryRun': True, 'PreserveOriginals': True,
                           'ScopeMode': 'legacy', 'ItemIds': [], 'LibraryIds': []},
        }
        calls = {'settings': 0, 'tokens': []}

        def request(path, body=None, method=None, token=None, **kwargs):
            calls['tokens'].append(token)
            if path == 'JellyP/Settings' and method is None:
                calls['settings'] += 1
                if calls['settings'] == 1 and token == 'old':
                    raise RuntimeError('JellyP/Settings: HTTP 401 unauthorized')
                return 200, safe_settings if calls['settings'] > 1 else apply_settings
            if path == 'JellyP/Settings/Automation':
                return 200, {}
            if path == 'JellyP/Status':
                return 200, {'PendingRecovery': 0}
            raise AssertionError(path)

        with mock.patch.object(gates.h, 'request', side_effect=request), \
             mock.patch.object(gates, 'login_verified', return_value=('new', {'Version': '12.1.0'})), \
             mock.patch.object(gates, 'set_task_triggers') as set_triggers, \
             mock.patch.object(gates, 'task_info', return_value={'Triggers': []}):
            result = gates.reset_safe_automation(self.root, 'old')
            self.assertFalse(result['enabled'])
            self.assertTrue(result['dryRun'])
            self.assertEqual([], result['triggers'])
            self.assertIn('new', calls['tokens'])
            set_triggers.assert_called_once_with('new', 'JellyPOptimizeArtwork', [])

    def test_reset_safe_automation_reports_bad_readback_and_pending_recovery(self):
        safe_settings = {
            'Automation': {'Enabled': True, 'DryRun': True, 'PreserveOriginals': True,
                           'ScopeMode': 'legacy', 'ItemIds': [], 'LibraryIds': []},
        }

        def request(path, body=None, method=None, token=None, **kwargs):
            if path == 'JellyP/Settings':
                return 200, safe_settings
            if path == 'JellyP/Settings/Automation':
                return 200, {}
            if path == 'JellyP/Status':
                return 200, {'PendingRecovery': 1}
            raise AssertionError(path)

        with mock.patch.object(gates.h, 'request', side_effect=request), \
             mock.patch.object(gates, 'set_task_triggers'), \
             mock.patch.object(gates, 'task_info', return_value={'Triggers': []}):
            with self.assertRaises(gates.GateFailure):
                gates.reset_safe_automation(self.root, 'token')

    def test_setup_failure_stops_owned_fixture_and_writes_final(self):
        candidate = Path(self.temporary.name) / 'candidate.zip'
        candidate.write_bytes(b'zip')
        test_plugin = Path(self.temporary.name) / 'test.dll'
        test_plugin.write_bytes(b'dll')
        with mock.patch.object(gates, 'preflight'), \
             mock.patch.object(gates.h, 'read_plugin_archive', return_value=({}, 'A' * 64)), \
             mock.patch.object(gates.h, 'stop_host') as stop, \
             mock.patch.object(gates.h, 'install_plugin') as install, \
             mock.patch.object(gates, 'ensure_fixture_state', side_effect=gates.GateFailure('setup failed')), \
             mock.patch.object(gates, 'stop_and_verify', return_value={'stopped': True, 'portFree': True}) as verify, \
             mock.patch.object(gates, 'write_final_evidence') as write:
            with self.assertRaisesRegex(gates.GateFailure, 'setup failed'):
                gates.main(['--root', str(self.root), '--phase', 'all', '--candidate', str(candidate),
                            '--expected-dll-sha256', 'A' * 64, '--test-plugin-dll', str(test_plugin)])
            stop.assert_called()
            install.assert_called_once()
            verify.assert_called_once()
            write.assert_called_once()
            self.assertTrue(write.call_args.args[1]['stopped'])

    def test_stop_failure_is_nonzero_after_successful_run(self):
        candidate = Path(self.temporary.name) / 'candidate.zip'
        candidate.write_bytes(b'zip')
        test_plugin = Path(self.temporary.name) / 'test.dll'
        test_plugin.write_bytes(b'dll')
        with mock.patch.object(gates, 'preflight'), \
             mock.patch.object(gates.h, 'read_plugin_archive', return_value=({}, 'A' * 64)), \
             mock.patch.object(gates.h, 'stop_host'), \
             mock.patch.object(gates.h, 'install_plugin'), \
             mock.patch.object(gates, 'ensure_fixture_state'), \
             mock.patch.object(gates, 'load_state', return_value={}), \
             mock.patch.object(gates, 'run_gates'), \
             mock.patch.object(gates, 'stop_and_verify', side_effect=gates.GateFailure('stop failed')), \
             mock.patch.object(gates, 'write_final_evidence') as write:
            with contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(gates.GateFailure, 'stop failed'):
                    gates.main(['--root', str(self.root), '--phase', 'all', '--candidate', str(candidate),
                                '--expected-dll-sha256', 'A' * 64, '--test-plugin-dll', str(test_plugin)])
            write.assert_called_once()
            self.assertFalse(write.call_args.args[1]['stopped'])

    def test_stop_phase_rejects_unmarked_root_before_any_process_action(self):
        unmarked = Path(self.temporary.name) / 'unmarked'
        unmarked.mkdir()
        with mock.patch.object(gates.h, 'stop_host') as stop:
            with self.assertRaises(SystemExit):
                gates.main(['--root', str(unmarked), '--phase', 'stop'])
            stop.assert_not_called()

    def test_install_test_plugin_writes_active_manifest_and_dll(self):
        source = Path(self.temporary.name) / 'JellyP.TestSlowMetadata.dll'
        source.write_bytes(b'test-only-dll')
        result = gates.install_test_plugin(self.root, source)
        folder = self.root / 'data/plugins/JellyPTestSlowMetadata'
        self.assertTrue((folder / 'JellyP.TestSlowMetadata.dll').is_file())
        meta = json.loads((folder / 'meta.json').read_text())
        self.assertEqual(gates.TEST_PLUGIN_GUID, meta['guid'])
        self.assertEqual(gates.TEST_PLUGIN_VERSION, meta['version'])
        self.assertEqual([gates.TEST_PLUGIN_DLL], meta['assemblies'])
        self.assertEqual(gates.h.sha256(source), result['sha256'])


if __name__ == '__main__':
    unittest.main()
