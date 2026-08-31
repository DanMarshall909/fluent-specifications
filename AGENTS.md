# AGENTS.md — fluent-spec.danmarshall.dev

## Project notes

- The library targets C# 14 and .NET 10.
- The documentation site is an Astro static site.
- Documentation source lives in `src/content/docs/*.md`.
- Astro builds the production site into `docs/`.
- Preview locally with `npm run dev`; validate everything with `npm test`.

## Code snippets

- Every authored C# fence in the documentation and README must include a
  canonical Roslyn documentation ID in `symbol` fence metadata.
- Never copy C# into a documentation fence manually.
- Run `npm run snippets:sync` after changing a referenced declaration.
- Run `npm run snippets:check` to prove the checked-in extracts are current.
- Use the documentation tool's `list` mode to discover canonical IDs when a
  declaration is overloaded or contains generic types.

## GitHub Pages deployment

- The custom domain is `fluent-spec.danmarshall.dev`.
- GitHub Pages must serve the `gh-pages` branch from `/docs`.
- The published branch must contain `docs/index.html`, `docs/CNAME`,
  `docs/.nojekyll`, and the rest of the generated site beneath `docs/`.
- The publish workflow follows the blog repository's orphaned
  `gh-pages:/docs` convention.
- Do not publish the generated site at the root of `gh-pages`.
