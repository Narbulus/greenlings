"""
Minimal Tripo API client (https://developers.tripo3d.ai, API v3) for generating creature meshes.

    python tools/tripo/tripo.py balance
    python tools/tripo/tripo.py multiview <out_dir> front=<png> left=<png> back=<png> right=<png> [face_limit=N] [low_poly=1]
    python tools/tripo/tripo.py fetch <task_id> <out_dir>      # re-download a finished task (URLs expire after 5 min)

"left"/"right" are the creature's OWN left/right side (Tripo convention).
The API key comes from $TRIPO_API_KEY or the first KEY=value line of $TRIPO_SECRETS (default ../greenlings/secrets.txt).
Every task costs credits (1 credit = $0.01): multiview + texture = 30, smart low poly +10.
"""
import json
import os
import sys
import time

import requests

BASE = "https://openapi.tripo3d.ai/v3"
MODEL = "v3.1-20260211"


def _key():
    if os.environ.get("TRIPO_API_KEY"):
        return os.environ["TRIPO_API_KEY"].strip()
    path = os.environ.get("TRIPO_SECRETS", os.path.expanduser("~/Projects/greenlings/secrets.txt"))
    with open(path, encoding="utf-8") as f:
        for line in f:
            if "=" in line:
                return line.split("=", 1)[1].strip()
    raise SystemExit("no API key found")


def _h():
    return {"Authorization": "Bearer " + _key()}


def _check(r):
    try:
        j = r.json()
    except ValueError:
        raise SystemExit(f"HTTP {r.status_code}: {r.text[:300]}")
    if r.status_code != 200 or j.get("code") != 0:
        raise SystemExit(f"HTTP {r.status_code}: {json.dumps(j)[:500]}")
    return j["data"]


def balance():
    return _check(requests.get(BASE + "/account/balance", headers=_h(), timeout=30))


def upload(path):
    with open(path, "rb") as f:
        return _check(requests.post(BASE + "/files", headers=_h(), files={"file": f}, timeout=120))["file_token"]


def create_multiview(views, face_limit=None, low_poly=False):
    body = {"inputs": [{k: upload(p)} for k, p in views.items()], "model": MODEL, "texture": True, "pbr": False}
    if low_poly:
        body["smart_low_poly"] = True
    if face_limit:
        body["face_limit"] = int(face_limit)
    return _check(requests.post(BASE + "/generation/multiview-to-model", headers=_h(), json=body, timeout=120))["task_id"]


def wait(task_id, timeout=900):
    t0 = time.time()
    while True:
        d = _check(requests.get(f"{BASE}/tasks/{task_id}", headers=_h(), timeout=30))
        if d["status"] in ("success", "failed", "cancelled", "banned", "expired"):
            return d
        if time.time() - t0 > timeout:
            raise SystemExit(f"timed out waiting for {task_id} (status {d['status']}, {d.get('progress')}%)")
        time.sleep(3)


def download(task, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    out = task.get("output") or {}
    saved = {}
    for key, url in out.items():
        if isinstance(url, str) and url.startswith("http"):
            ext = os.path.splitext(url.split("?")[0])[1] or ".bin"
            path = os.path.join(out_dir, key + ext)
            with open(path, "wb") as f:
                f.write(requests.get(url, timeout=300).content)
            saved[key] = path
    with open(os.path.join(out_dir, "task.json"), "w", encoding="utf-8") as f:
        json.dump({k: v for k, v in task.items() if k != "output"}, f, indent=1)
    return saved


def main(argv):
    cmd = argv[0] if argv else "balance"
    if cmd == "balance":
        print(balance())
    elif cmd == "multiview":
        out_dir, kv = argv[1], dict(a.split("=", 1) for a in argv[2:])
        views = {k: kv.pop(k) for k in ("front", "left", "back", "right") if k in kv}
        before = balance()
        tid = create_multiview(views, kv.get("face_limit"), kv.get("low_poly") == "1")
        print("task", tid, flush=True)
        task = wait(tid)
        print("status", task["status"])
        if task["status"] == "success":
            print(download(task, out_dir))
        print("balance before", before, "after", balance())
    elif cmd == "fetch":
        task = wait(argv[1])
        print(task["status"], download(task, argv[2]))
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
