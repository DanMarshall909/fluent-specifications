import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';

const root = process.cwd();
const contentRoot = join(root, 'src', 'content', 'docs');
const requiredPages = [
  'getting-started.md',
  'defining-rules.md',
  'composition.md',
  'diagnostics.md',
  'ef-core.md',
  'testing.md',
  'reference.md',
  'prior-art.md',
];

function read(relativePath) {
  return readFileSync(join(root, relativePath), 'utf8');
}

test('the documentation is authored as a complete Markdown collection', () => {
  for (const page of requiredPages) {
    const markdown = readFileSync(join(contentRoot, page), 'utf8');

    assert.match(markdown, /^---\r?\n[\s\S]*?\r?\n---\r?\n/);
    assert.match(markdown, /^title:\s+.+$/m);
    assert.match(markdown, /^description:\s+.{70,}$/m);
    assert.match(markdown, /^order:\s+\d+$/m);
    assert.match(markdown, /^section:\s+.+$/m);
  }
});

test('every required documentation source is tracked by Git', () => {
  const trackedFiles = new Set(
    execFileSync('git', ['ls-files'], { cwd: root, encoding: 'utf8' })
      .split(/\r?\n/)
      .filter(Boolean)
      .map(path => path.replaceAll('\\', '/')),
  );

  const requiredTrackedFiles = [
    ...requiredPages.map(page => `src/content/docs/${page}`),
    'src/pages/docs/[...slug].astro',
  ];

  for (const path of requiredTrackedFiles) {
    assert.ok(
      trackedFiles.has(path),
      `${path} must be committed so CI can build the site`,
    );
  }
});

test('the testing guide points to representative executable contracts', () => {
  const guide = readFileSync(join(contentRoot, 'testing.md'), 'utf8');

  for (const testName of [
    'Zero_argument_rules_compose_without_parentheses',
    'Matches_short_circuits_and_from_left_to_right',
    'Unsupported_filter_fails_before_any_select_is_executed',
    'Public_ef_adapter_api_never_returns_or_accepts_iqueryable',
  ]) {
    assert.match(guide, new RegExp(`\\b${testName}\\b`));
  }
});

test('every authored C# fence is bound to a Roslyn symbol in its metadata', () => {
  const authoredMarkdown = [
    join(root, 'README.md'),
    ...readdirSync(contentRoot).map(page => join(contentRoot, page)),
  ];

  for (const path of authoredMarkdown) {
    const markdown = readFileSync(path, 'utf8');
    for (const match of markdown.matchAll(/^```csharp(?<metadata>[^\r\n]*)$/gm)) {
      assert.match(
        match.groups.metadata,
        /\bsymbol="[MPTFE]:[^"\r\n]+"/,
        `${path} contains a C# fence without Roslyn symbol metadata`,
      );
    }
  }
});

test('custom-domain and deployment configuration follow the blog convention', () => {
  assert.equal(read('CNAME').trim(), 'fluent-spec.danmarshall.dev');
  assert.equal(read('public/CNAME').trim(), 'fluent-spec.danmarshall.dev');

  const workflow = read('.github/workflows/publish.yml');
  assert.match(workflow, /dotnet test/);
  assert.match(workflow, /npm ci/);
  assert.match(workflow, /npm test/);
  assert.match(workflow, /dotnet pack/);
  assert.match(workflow, /actions\/upload-artifact@v4/);
  assert.match(workflow, /peaceiris\/actions-gh-pages@v4/);
  assert.match(workflow, /destination_dir:\s*\.\/docs/);
  assert.match(workflow, /force_orphan:\s*true/);
});

test('NuGet publication starts at 1.0.0, increments patches, and is credential-gated', () => {
  const workflow = read('.github/workflows/publish-nuget.yml');

  assert.match(workflow, /push:\s*\r?\n\s+branches:\s*\[main\]/);
  assert.match(workflow, /workflow_dispatch:/);
  assert.match(workflow, /if:\s*vars\.NUGET_PUBLISH_ENABLED == 'true'/);
  assert.match(workflow, /NUGET_API_KEY:\s*\$\{\{ secrets\.NUGET_API_KEY \}\}/);
  assert.match(workflow, /VERSION_PREFIX:\s*'1\.0'/);
  assert.match(workflow, /BASE_COMMIT_COUNT:\s*3/);
  assert.match(workflow, /git rev-list --first-parent --count HEAD/);
  assert.match(workflow, /-p:PackageVersion=\$\{\{ steps\.package-version\.outputs\.version \}\}/);
  assert.match(workflow, /https:\/\/api\.nuget\.org\/v3\/index\.json/);
  assert.match(workflow, /--skip-duplicate/);
  assert.doesNotMatch(workflow, /--api-key/);
});

test('the homepage and documentation shell have deliberate responsive fallbacks', () => {
  const css = read('src/styles/global.css');

  assert.match(css, /body\s*{[^}]*min-width:\s*0;/s);
  assert.match(
    css,
    /@media \(max-width:\s*62rem\)[\s\S]*?\.hero-grid,[\s\S]*?\.boundary-grid\s*{\s*grid-template-columns:\s*1fr;/,
  );
  assert.match(
    css,
    /@media \(max-width:\s*48rem\)[\s\S]*?\.docs-sidebar\s*{[^}]*overflow-x:\s*auto;/,
  );
  assert.match(css, /\.doc-article,[\s\S]*?\.prose\s*{\s*min-width:\s*0;/);
});
