# Writing the wiki

This site is [MkDocs Material](https://squidfunk.github.io/mkdocs-material/), built from `docs/` and `mkdocs.yml`
at the repo root.

## Preview locally

```powershell
python -m venv .venv-docs
.venv-docs/Scripts/pip install -r docs/requirements.txt
python scripts/gen-docs.py
.venv-docs/Scripts/mkdocs serve        # http://127.0.0.1:8000, reloads on save
```

## Generated pages

Two pages mirror the code and are rewritten by `scripts/gen-docs.py`. Don't edit them by hand:

- `guide/config.md` comes from the `Config.Bind` calls in `Plugin.cs`.
- `dev/tests.md` comes from the comment at the top of each `tests/*.txt`.

The deploy workflow runs the script before building, so a new setting or test shows up without anyone remembering to update the wiki.

## Keeping it current

- New feature: add it to [Implementation status](../project/status.md) with its test, and to the player guide.
- Finished roadmap item: tick it on the [roadmap](../project/roadmap.md) and note it in the [changelog](../project/changelog.md).
- Status pills: `<span class="st ok">tested</span>`, `st part`, `st built`, `st plan`.

## Deploying

`.github/workflows/docs.yml` builds the site and publishes it to **GitHub Pages** on every push to `main` that
touches the docs, the config or the tests. One-time setup once the repo is on GitHub:

1. In the repo, go to **Settings → Pages → Build and deployment → Source** and choose **GitHub Actions**.
2. Uncomment `site_url` and `repo_url` in `mkdocs.yml`.

The build runs with `strict: true`, so a broken link fails the deploy instead of shipping.
