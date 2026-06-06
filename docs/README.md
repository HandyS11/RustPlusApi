# Documentation (DocFX)

The API documentation site is built with [DocFX](https://dotnet.github.io/docfx/) from the XML
doc comments in `src/` plus the conceptual pages in this folder. It is published to GitHub Pages
by [`.github/workflows/docs.yml`](../.github/workflows/docs.yml) on push to `develop`.

## Prerequisites

- The .NET 10 SDK (DocFX builds the projects to extract API metadata).
- DocFX as a global tool:

  ```bash
  dotnet tool install --global docfx
  ```

  Make sure `~/.dotnet/tools` is on your `PATH`.

## Build and preview locally

From the repository root:

```bash
# Build the API metadata + site and serve it with live preview
docfx docs/docfx.json --serve
```

Then open <http://localhost:8080>.

To just build (output goes to `docs/_site/`, which is gitignored):

```bash
docfx docs/docfx.json
```

If you change public APIs or XML doc comments, re-run the command above to regenerate the API
reference (DocFX rebuilds the `docs/api/` metadata each run).

## Layout

| Path | Purpose |
| --- | --- |
| `docfx.json` | DocFX configuration (metadata source = `../src`, pinned to `net10.0`). |
| `index.md` | Landing page. |
| `toc.yml` | Top navigation. |
| `api/` | Generated API metadata (gitignored). |
| `_site/` | Generated static site (gitignored). |
