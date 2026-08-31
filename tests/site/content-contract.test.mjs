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
  assert.match(workflow, /peaceiris\/actions-gh-pages@v4/);
  assert.match(workflow, /destination_dir:\s*\.\/docs/);
  assert.match(workflow, /force_orphan:\s*true/);
});
