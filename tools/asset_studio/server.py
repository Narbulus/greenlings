"""Local asset experiment studio. Run: python tools/asset_studio/server.py"""
import argparse
import hashlib
import importlib.util
import json
import mimetypes
import shutil
import threading
import uuid
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / '.work' / 'asset-studio'
LOCK = threading.RLock()


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def stamp():
    return datetime.now(timezone.utc).isoformat()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def folder(run_id):
    if not isinstance(run_id, str) or len(run_id) != 32 or any(c not in '0123456789abcdef' for c in run_id):
        raise ValueError('Invalid run ID')
    return DATA / run_id


def read(run_id):
    return json.loads((folder(run_id) / 'run.json').read_text(encoding='utf-8'))


def save(run):
    path = folder(run['id']) / 'run.json'
    run['updated'] = stamp()
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(run, indent=2), encoding='utf-8')
    temp.replace(path)


def change(run_id, **values):
    with LOCK:
        run = read(run_id)
        run.update(values)
        save(run)


def image_worker(run_id):
    try:
        client = module('studio_images', 'tools/images/openrouter_image.py')
        run = read(run_id)
        for index in range(run['count']):
            with LOCK:
                run = read(run_id)
                run['progress'] = f'Generating reference {index + 1} of {run["count"]}'
                run['attempts'].append({'index': index, 'status': 'submitted', 'started': stamp()})
                save(run)
            images, message, usage = client.generate(run['prompt'], [folder(run_id) / 'source.png'], run['model'], '3:2')
            if not images:
                raise ValueError('Provider returned no image: ' + str(message)[:300])
            with LOCK:
                run = read(run_id)
                attempt = run['attempts'][-1]
                attempt.update(status='complete', usage=usage, finished=stamp())
                for n, data in enumerate(images):
                    name = f'candidate-{index}-{n}.png'
                    path = folder(run_id) / name
                    path.write_bytes(data)
                    run['candidates'].append({'file': name, 'hash': digest(path), 'attempt': index})
                save(run)
        change(run_id, status='review', progress='Choose a reference to build')
    except BaseException as exc:
        change(run_id, status='error', progress=str(exc)[:600])


def build_worker(run_id, resume=False):
    try:
        tripo = module('studio_tripo', 'tools/tripo/tripo.py')
        board = module('studio_board', 'tools/tripo/board.py')
        run = read(run_id)
        directory = folder(run_id)
        if digest(directory / run['selected']) != run['approved_hash']:
            raise ValueError('Approved reference changed; choose it again.')
        views_dir = directory / 'views'
        if not run.get('multiview_task'):
            if resume:
                raise ValueError('Submission outcome is unknown. Reconcile the provider task before trying again.')
            board.grid(str(directory / run['selected']), str(views_dir), 'body')
            change(run_id, submission='multiview', progress='Submitting 3D generation')
            tid = tripo.create_multiview({v: str(views_dir / f'body_{v}.png') for v in ('front', 'left', 'right', 'back')})
            change(run_id, multiview_task=tid, submission=None)
        run = read(run_id)
        change(run_id, progress='Generating 3D model')
        task = tripo.wait(run['multiview_task'])
        if task['status'] != 'success':
            raise ValueError('3D generation: ' + task['status'])
        tripo.download(task, str(directory / 'generated'))
        run = read(run_id)
        if not run.get('retopo_task'):
            if run.get('submission') == 'retopo':
                raise ValueError('Retopology submission outcome unknown; reconcile provider history.')
            change(run_id, submission='retopo', progress='Submitting retopology')
            tid = tripo.retopo(run['multiview_task'], face_limit=1500)
            change(run_id, retopo_task=tid, submission=None)
        run = read(run_id)
        change(run_id, progress='Retopologizing and baking textures')
        task = tripo.wait(run['retopo_task'])
        if task['status'] != 'success':
            raise ValueError('Retopology: ' + task['status'])
        outputs = tripo.download(task, str(directory / 'retopo'))
        change(run_id, status='mesh_ready', outputs=outputs,
               progress='Mesh ready for Blender rigging and pose review')
    except BaseException as exc:
        change(run_id, status='error', progress=str(exc)[:600])


def launch(target, *args):
    threading.Thread(target=target, args=args, daemon=True).start()


class Handler(BaseHTTPRequestHandler):
    def reply(self, data, status=200):
        body = json.dumps(data).encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = urlparse(self.path).path
        if path == '/api/runs':
            with LOCK:
                runs = [json.loads(p.read_text(encoding='utf-8')) for p in DATA.glob('*/run.json')]
            return self.reply(sorted(runs, key=lambda r: r['created'], reverse=True))
        if path == '/api/sources':
            sources = [p for p in (ROOT / '.work/refs').glob('in_*grid.png')]
            return self.reply([str(p.relative_to(ROOT)).replace('\\', '/') for p in sources])
        if path.startswith('/files/'):
            file = (DATA / path[len('/files/'):]).resolve()
            if not file.is_relative_to(DATA.resolve()) or file.suffix.lower() not in ('.png', '.jpg', '.webp', '.glb', '.json'):
                return self.reply({'error': 'Invalid file'}, 400)
        elif path == '/':
            file = Path(__file__).with_name('index.html')
        else:
            return self.reply({'error': 'Not found'}, 404)
        if not file.is_file():
            return self.reply({'error': 'Not found'}, 404)
        body = file.read_bytes()
        self.send_response(200)
        self.send_header('Content-Type', mimetypes.guess_type(str(file))[0] or 'application/octet-stream')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        # Only the local interface may initiate paid work; reject cross-origin requests.
        if self.headers.get('Origin') != f'http://{self.headers.get("Host")}':
            return self.reply({'error': 'Use the local Studio interface'}, 403)
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if length > 65536:
                raise ValueError('Request too large')
            body = json.loads(self.rfile.read(length))
            with LOCK:
                if self.path == '/api/create':
                    source = (ROOT / body['source']).resolve()
                    if not source.is_relative_to(ROOT / '.work/refs') or not source.is_file():
                        raise ValueError('Choose an existing reference board')
                    count = int(body.get('count', 2))
                    if count not in (1, 2, 4):
                        raise ValueError('Choose 1, 2 or 4 references')
                    prompt, model = body['prompt'].strip(), body['model'].strip()
                    if not prompt or not model:
                        raise ValueError('Describe the change and choose a model')
                    run_id = uuid.uuid4().hex
                    folder(run_id).mkdir(parents=True)
                    shutil.copyfile(source, folder(run_id) / 'source.png')
                    run = dict(id=run_id, title=body.get('title', 'New asset')[:100], created=stamp(),
                               status='generating', progress='Starting references', prompt=prompt, model=model,
                               count=count, source=str(source.relative_to(ROOT)), source_hash=digest(source),
                               candidates=[], attempts=[])
                    save(run)
                    launch(image_worker, run_id)
                    return self.reply(run)
                run_id = body['id']
                run = read(run_id)
                if self.path == '/api/build':
                    if run['status'] != 'review':
                        raise ValueError('This experiment is not awaiting reference review')
                    selected = next((c for c in run['candidates'] if c['file'] == body['file']), None)
                    if not selected:
                        raise ValueError('Choose a generated reference')
                    run.update(selected=selected['file'], approved_hash=selected['hash'], status='building',
                               approved_at=stamp(), credit_estimate=65, progress='Preparing approved reference')
                    save(run)
                    launch(build_worker, run_id)
                elif self.path == '/api/resume':
                    if run['status'] not in ('error', 'interrupted') or not run.get('multiview_task'):
                        raise ValueError('No known 3D task to resume')
                    run['status'] = 'building'
                    save(run)
                    launch(build_worker, run_id, True)
                else:
                    raise ValueError('Unknown action')
                self.reply(read(run_id))
        except (ValueError, KeyError, OSError) as exc:
            self.reply({'error': str(exc)}, 400)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args()
    DATA.mkdir(parents=True, exist_ok=True)
    for path in DATA.glob('*/run.json'):
        run = json.loads(path.read_text(encoding='utf-8'))
        if run['status'] in ('generating', 'building'):
            run.update(status='interrupted', progress='Studio restarted. Known 3D tasks can be resumed; reference requests are not retried automatically.')
            save(run)
    print(f'Asset Studio: http://127.0.0.1:{args.port}', flush=True)
    ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()


if __name__ == '__main__':
    main()
