"""
Image generation/editing through OpenRouter (reference boards for Tripo).

    python tools/images/openrouter_image.py <out.png> --prompt-file p.txt [--image in.png ...] [--model id] [--aspect 3:1]

Sends the prompt plus any input images to a chat model with image output and saves the first
image it returns (more as out_2.png, ...). Prints the cost OpenRouter reports.
The key comes from $OPENROUTER_API_KEY or the OPENROUTER_API_KEY= line of
~/Projects/greenlings/secrets.txt.
"""
import argparse
import base64
import json
import os
import sys

import requests

URL = "https://openrouter.ai/api/v1/chat/completions"
DEFAULT_MODEL = "google/gemini-3.1-flash-image"


def _key():
    if os.environ.get("OPENROUTER_API_KEY"):
        return os.environ["OPENROUTER_API_KEY"].strip()
    path = os.environ.get("GREENLINGS_SECRETS", os.path.expanduser("~/Projects/greenlings/secrets.txt"))
    with open(path, encoding="utf-8") as f:
        for line in f:
            name, _, value = line.strip().partition("=")
            if name == "OPENROUTER_API_KEY" and value:
                return value.strip()
    raise SystemExit("no OPENROUTER_API_KEY in " + path)


def _data_uri(path):
    ext = os.path.splitext(path)[1].lower().lstrip(".") or "png"
    mime = "image/jpeg" if ext in ("jpg", "jpeg") else "image/" + ext
    with open(path, "rb") as f:
        return f"data:{mime};base64," + base64.b64encode(f.read()).decode()


def generate(prompt, images=(), model=DEFAULT_MODEL, aspect=None):
    content = [{"type": "text", "text": prompt}] + [{"type": "image_url", "image_url": {"url": _data_uri(p)}} for p in images]
    body = {"model": model, "messages": [{"role": "user", "content": content}], "modalities": ["image", "text"], "usage": {"include": True}}
    if aspect:
        body["image_config"] = {"aspect_ratio": aspect}
    r = requests.post(URL, headers={"Authorization": "Bearer " + _key()}, json=body, timeout=600)
    j = r.json()
    if r.status_code != 200 or "choices" not in j:
        raise SystemExit(f"HTTP {r.status_code}: {json.dumps(j)[:600]}")
    msg = j["choices"][0]["message"]
    out = [base64.b64decode(im["image_url"]["url"].split(",", 1)[1]) for im in msg.get("images") or []]
    return out, (msg.get("content") or ""), j.get("usage", {})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--prompt-file", required=True)
    ap.add_argument("--image", action="append", default=[])
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--aspect")
    a = ap.parse_args()
    prompt = open(a.prompt_file, encoding="utf-8").read()
    imgs, text, usage = generate(prompt, a.image, a.model, a.aspect)
    if not imgs:
        raise SystemExit("no image returned; model said: " + text[:500])
    base, ext = os.path.splitext(a.out)
    for i, data in enumerate(imgs):
        path = a.out if i == 0 else f"{base}_{i + 1}{ext}"
        with open(path, "wb") as f:
            f.write(data)
        print("saved", path)
    if text.strip():
        print("model:", text.strip()[:300])
    print("cost $", usage.get("cost"), "| tokens", usage.get("prompt_tokens"), "->", usage.get("completion_tokens"))


if __name__ == "__main__":
    main()
