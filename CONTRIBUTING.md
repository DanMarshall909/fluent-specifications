# Contributing

Changes should preserve the library's narrow purpose: readable Boolean domain
rules, structured explanations, and infrastructure translation without query
leakage.

Before opening a pull request:

1. Restore dependencies with `dotnet restore FluentSpecifications.slnx` and
   `npm ci`.
2. Add or update executable examples before changing implementation behavior.
3. Regenerate documentation extracts with `npm run snippets:sync`.
4. Run the .NET Release suite and `npm test`.
5. Confirm that a focused provider test is not being presented as broad
   production-provider proof.

Documentation examples must be real C# declarations from the repository. Put
the declaration's canonical Roslyn documentation ID in the code fence's
`symbol` metadata; the generator owns the fence body. Run the documentation
tool in `list` mode when the exact ID is unclear.

The GitHub Pages workflow validates pull requests. Deployments occur only from
`main` and publish the generated `docs/` directory beneath `gh-pages:/docs`.
