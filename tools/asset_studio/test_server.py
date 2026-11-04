import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import server


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        (server.ROOT / '.work').mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=server.ROOT / '.work')
        self.data_patch = patch.object(server, 'DATA', Path(self.temp.name))
        self.data_patch.start()
        self.rid = 'a' * 32
        directory = server.folder(self.rid)
        directory.mkdir()
        (directory / 'chosen.png').write_bytes(b'approved')
        self.run = dict(id=self.rid, selected='chosen.png',
                        approved_hash=server.digest(directory / 'chosen.png'),
                        multiview_task='existing-generation', retopo_task='existing-retopo', status='building')
        server.save(self.run)

    def tearDown(self):
        self.data_patch.stop()
        self.temp.cleanup()

    def test_resume_fetches_existing_tasks_without_chargeable_submission(self):
        client = Mock()
        client.wait.return_value = {'status': 'success'}
        client.download.return_value = {}
        with patch.object(server, 'module', return_value=client):
            server.build_worker(self.rid, resume=True)
        client.create_multiview.assert_not_called()
        client.retopo.assert_not_called()
        self.assertEqual([c.args[0] for c in client.wait.call_args_list],
                         ['existing-generation', 'existing-retopo'])
        self.assertEqual(server.read(self.rid)['status'], 'mesh_ready')

    def test_changed_approval_prevents_provider_calls(self):
        (server.folder(self.rid) / 'chosen.png').write_bytes(b'changed')
        client = Mock()
        with patch.object(server, 'module', return_value=client):
            server.build_worker(self.rid)
        client.create_multiview.assert_not_called()
        client.wait.assert_not_called()
        self.assertEqual(server.read(self.rid)['status'], 'error')

    def test_ambiguous_retopology_is_not_resubmitted(self):
        del self.run['retopo_task']
        self.run['submission'] = 'retopo'
        server.save(self.run)
        client = Mock()
        client.wait.return_value = {'status': 'success'}
        with patch.object(server, 'module', return_value=client):
            server.build_worker(self.rid, resume=True)
        client.retopo.assert_not_called()
        self.assertIn('unknown', server.read(self.rid)['progress'])

    def test_rejects_run_path_traversal(self):
        with self.assertRaises(ValueError):
            server.folder('../other')


if __name__ == '__main__':
    unittest.main()
