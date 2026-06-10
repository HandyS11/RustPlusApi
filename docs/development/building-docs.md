# Building the Docs

The documentation site is built with [DocFX](https://dotnet.github.io/docfx/) from the XML doc
comments in `src/` plus the conceptual pages in `docs/`. It is deployed automatically by
[`.github/workflows/Documentation.yml`](https://github.com/HandyS11/RustPlusApi/blob/main/.github/workflows/Documentation.yml)
on push to `main`.

## Prerequisites

- The .NET 10 SDK (DocFX builds the projects to extract API metadata).
- DocFX as a global tool:

  ```bash
  dotnet tool install --global docfx
  ```

  Make sure `~/.dotnet/tools` is on your `PATH`.

## Build and preview

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

> Note: regenerate after changing public APIs or XML doc comments — DocFX rebuilds the `docs/api/`
> metadata each run.

## Layout

| Path | Purpose |
| --- | --- |
| `docfx.json` | DocFX configuration (metadata source = `../src`, pinned to `net10.0`). |
| `index.md` | Landing page. |
| `toc.yml` | Top navigation. |
| `articles/` | Conceptual guides (get started, guides, resources). |
| `development/` | This section — contributor and build documentation. |
| `template/` | Custom Rust theme layer (`public/main.css`, `public/main.js`). |
| `images/` | Static image assets. |
| `api/` | Generated API metadata (gitignored). |
| `_site/` | Generated static site (gitignored). |
