import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const root = process.cwd();
const versionGate = join(root, 'eng', 'Assert-NextPackageVersion.ps1');

function runGate(candidateVersion, publishedVersions, extraArguments = []) {
  return spawnSync(
    'pwsh',
    [
      '-NoLogo',
      '-NoProfile',
      '-File',
      versionGate,
      '-PackageId',
      'DanMarshall.FluentSpecifications',
      '-CandidateVersion',
      candidateVersion,
      '-PublishedVersionsJson',
      JSON.stringify(publishedVersions),
      ...extraArguments,
    ],
    { cwd: root, encoding: 'utf8' },
  );
}

test('the version gate accepts each immediate semantic-version transition', () => {
  for (const example of [
    { published: [], candidate: '1.0.0' },
    { published: ['1.0.0'], candidate: '1.0.1' },
    { published: ['1.0.0'], candidate: '1.1.0' },
    { published: ['1.0.0'], candidate: '2.0.0' },
    { published: ['1.0.0', '1.1.0'], candidate: '1.1.1' },
  ]) {
    const result = runGate(example.candidate, example.published);

    assert.equal(
      result.status,
      0,
      `${example.candidate} should be accepted:\n${result.stdout}${result.stderr}`,
    );
  }
});

test('the version gate supports coordinated first releases and idempotent retries', () => {
  const coordinatedFirst = runGate('1.2.0', [], [
    '-FirstStableVersion',
    '1.2.0',
  ]);
  const retry = runGate('1.2.0', ['1.2.0'], ['-AllowAlreadyPublished']);

  assert.equal(
    coordinatedFirst.status,
    0,
    `a new extension should join the current suite:\n${coordinatedFirst.stdout}${coordinatedFirst.stderr}`,
  );
  assert.equal(
    retry.status,
    0,
    `a partial suite release should be retryable:\n${retry.stdout}${retry.stderr}`,
  );
});

test('the version gate rejects duplicates, gaps, and non-release versions', () => {
  for (const example of [
    { published: ['1.0.0'], candidate: '1.0.0' },
    { published: ['1.0.0'], candidate: '1.0.2' },
    { published: ['1.0.0'], candidate: '1.2.0' },
    { published: ['1.0.0'], candidate: '2.1.0' },
    { published: ['1.0.0'], candidate: '1.0.1-beta.1' },
  ]) {
    const result = runGate(example.candidate, example.published);

    assert.notEqual(result.status, 0, `${example.candidate} should be rejected`);
  }
});

test('network failures retain their original error instead of assuming an HTTP response', () => {
  const result = spawnSync(
    'pwsh',
    [
      '-NoLogo',
      '-NoProfile',
      '-File',
      versionGate,
      '-PackageId',
      'DanMarshall.FluentSpecifications',
      '-CandidateVersion',
      '1.1.0',
      '-PackageIndexUri',
      'http://127.0.0.1:1/index.json',
    ],
    { cwd: root, encoding: 'utf8' },
  );
  const output = `${result.stdout}${result.stderr}`;

  assert.notEqual(result.status, 0, 'an unavailable package index must fail closed');
  assert.doesNotMatch(output, /property 'Response' cannot be found/i);
  assert.match(output, /connect|refused|request/i);
});

test('NuGet publication reads, verifies, and packs one coordinated suite before authentication', () => {
  const workflow = readFileSync(
    join(root, '.github', 'workflows', 'publish-nuget.yml'),
    'utf8',
  );
  const selectionStep = workflow.indexOf('Read the selected package version');
  const gateStep = workflow.indexOf('Verify every package version is publishable');
  const packStep = workflow.indexOf(
    'Pack package suite ${{ steps.package-version.outputs.version }}',
  );
  const authenticationStep = workflow.indexOf(
    'Authenticate to NuGet.org with trusted publishing',
  );

  assert.ok(selectionStep >= 0, 'the NuGet workflow must read the project version');
  assert.ok(gateStep >= 0, 'the NuGet workflow must run the semantic-version gate');
  assert.ok(selectionStep < gateStep, 'version selection must precede verification');
  assert.ok(gateStep < packStep, 'version verification must precede packing');
  assert.ok(gateStep < authenticationStep, 'version verification must precede OIDC');
  assert.match(workflow, /-getProperty:PackageVersion/);
  assert.match(workflow, /Assert-NextPackageVersion\.ps1/);
  assert.match(workflow, /release_version:/);
  assert.match(workflow, /Pack-PackageSuite\.ps1/);
  assert.doesNotMatch(workflow, /\n\s+push:/);
  assert.doesNotMatch(workflow, /VERSION_PREFIX|BASE_COMMIT_COUNT|git rev-list/);
});
