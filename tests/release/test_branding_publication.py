import email.message
import importlib.util
import sys
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import MagicMock, patch

SCRIPTS = Path(__file__).resolve().parents[2] / 'scripts'
sys.path.insert(0, str(SCRIPTS))
spec = importlib.util.spec_from_file_location('publish_catalog', SCRIPTS / 'publish-catalog.py')
publication = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publication)


class BrandingPublicationTests(unittest.TestCase):
    def response(self, content_type, body):
        response = MagicMock()
        response.__enter__.return_value = response
        response.headers = email.message.Message()
        response.headers['Content-Type'] = content_type
        response.read.return_value = body
        return response

    def test_public_exact_png_is_required(self):
        source = (publication.package.ROOT / 'assets/branding/jellyp-catalog.png').read_bytes()
        with patch.object(publication.urllib.request, 'urlopen', return_value=self.response('image/png', source)) as fetch:
            publication.verify_catalog_image('https://example.invalid/tagged.png')
            fetch.assert_called_once_with('https://example.invalid/tagged.png', timeout=30)

    def test_wrong_mime_or_bytes_is_rejected(self):
        for content_type, body in [('text/html', b'not a PNG'), ('image/png', b'different artwork')]:
            with self.subTest(content_type=content_type), patch.object(publication.urllib.request, 'urlopen', return_value=self.response(content_type, body)):
                with self.assertRaisesRegex(ValueError, 'catalog unchanged'):
                    publication.verify_catalog_image('https://example.invalid/tagged.png')

    def test_unpublished_thumbnail_is_rejected(self):
        with patch.object(publication.urllib.request, 'urlopen', side_effect=urllib.error.HTTPError('https://example.invalid/tagged.png', 404, 'Not found', {}, None)):
            with self.assertRaises(urllib.error.HTTPError):
                publication.verify_catalog_image('https://example.invalid/tagged.png')
