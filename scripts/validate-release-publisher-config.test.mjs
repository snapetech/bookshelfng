import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(
  new URL('./validate-release-publisher-config.sh', import.meta.url)
);

function createTestCredentials(directory) {
  const sshKeyPath = path.join(directory, 'aur-test-key');
  execFileSync('ssh-keygen', [
    '-q',
    '-t',
    'ed25519',
    '-N',
    '',
    '-f',
    sshKeyPath,
  ]);

  const gpgHome = path.join(directory, 'gpg-test-home');
  fs.mkdirSync(gpgHome, { mode: 0o700 });
  const gpgEnv = { ...process.env, GNUPGHOME: gpgHome };
  execFileSync(
    'gpg',
    [
      '--batch',
      '--passphrase',
      '',
      '--quick-generate-key',
      'BookshelfNG workflow test <workflow-test@example.invalid>',
      'ed25519',
      'sign',
      '0',
    ],
    { env: gpgEnv, stdio: 'ignore' }
  );
  const gpgKey = execFileSync(
    'gpg',
    ['--batch', '--armor', '--export-secret-keys', 'workflow-test@example.invalid'],
    { env: gpgEnv, encoding: 'utf8' }
  );

  return {
    aurKey: fs.readFileSync(sshKeyPath, 'utf8'),
    gpgKey,
  };
}

function runValidator({ mode = 'publishers', env = {} } = {}) {
  const directory = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bookshelfng-publisher-check-')
  );
  const outputPath = path.join(directory, 'output');
  const summaryPath = path.join(directory, 'summary');
  fs.writeFileSync(outputPath, '');
  fs.writeFileSync(summaryPath, '');

  const result = spawnSync('bash', [script, mode], {
    encoding: 'utf8',
    env: {
      ...process.env,
      GITHUB_OUTPUT: outputPath,
      GITHUB_STEP_SUMMARY: summaryPath,
      ...env,
    },
  });

  return {
    ...result,
    directory,
    output: fs.readFileSync(outputPath, 'utf8'),
    summary: fs.readFileSync(summaryPath, 'utf8'),
  };
}

test('valid publisher settings pass preflight without contacting COPR', (t) => {
  const directory = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bookshelfng-publisher-keys-')
  );
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const { aurKey, gpgKey } = createTestCredentials(directory);
  const fakeBin = path.join(directory, 'bin');
  fs.mkdirSync(fakeBin);
  const curlPath = path.join(fakeBin, 'curl');
  fs.writeFileSync(curlPath, '#!/usr/bin/env bash\nexit 0\n', { mode: 0o755 });

  const result = runValidator({
    env: {
      PATH: `${fakeBin}${path.delimiter}${process.env.PATH}`,
      AUR_SSH_KEY: aurKey,
      COPR_WEBHOOK_URL:
        'https://copr.fedorainfracloud.org/webhooks/custom/1234/0123456789abcdef/bookshelfng/',
      GPG_PRIVATE_KEY: gpgKey,
      LAUNCHPAD_PPA: 'ppa:slskdn/bookshelfng',
      CHOCOLATEY_API_KEY: 'valid-test-key',
    },
  });

  t.after(() => fs.rmSync(result.directory, { recursive: true, force: true }));
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.output, /aur=true/);
  assert.match(result.output, /copr=true/);
  assert.match(result.output, /ppa=true/);
  assert.match(result.output, /chocolatey=true/);
  assert.equal(result.summary, '');
});

test('a malformed COPR secret is explicitly skipped instead of failing the release', (t) => {
  const result = runValidator({
    env: { COPR_WEBHOOK_URL: 'copr-login-token-without-webhook-url' },
  });
  t.after(() => fs.rmSync(result.directory, { recursive: true, force: true }));

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.output, /copr=false/);
  assert.match(result.summary, /COPR_WEBHOOK_URL is not a package-scoped/);
  assert.doesNotMatch(result.summary, /copr-login-token-without-webhook-url/);
});

test('COPR webhook preflight rejects wrong host, endpoint, and package', (t) => {
  const invalidUrls = [
    'http://copr.fedorainfracloud.org/webhooks/custom/1/token/bookshelfng/',
    'https://example.invalid/webhooks/custom/1/token/bookshelfng/',
    'https://copr.fedorainfracloud.org/webhooks/custom/1/token/other/',
    'https://copr.fedorainfracloud.org/webhooks/custom/1/token/bookshelfng/ extra',
  ];

  for (const url of invalidUrls) {
    const result = runValidator({ env: { COPR_WEBHOOK_URL: url } });
    t.after(() => fs.rmSync(result.directory, { recursive: true, force: true }));
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.output, /copr=false/);
    assert.match(result.summary, /package-scoped COPR custom webhook/);
    assert.doesNotMatch(result.summary, /example\.invalid|\/token\//);
  }
});

test('malformed Launchpad and Chocolatey settings are skipped before publication', (t) => {
  const badPpaTarget = runValidator({
    env: {
      GPG_PRIVATE_KEY: 'not a private key',
      LAUNCHPAD_PPA: 'not-a-ppa',
      CHOCOLATEY_API_KEY: 'contains whitespace',
    },
  });
  const badGpgKey = runValidator({
    env: {
      GPG_PRIVATE_KEY: 'not a private key',
      LAUNCHPAD_PPA: 'ppa:slskdn/bookshelfng',
    },
  });
  t.after(() => {
    fs.rmSync(badPpaTarget.directory, { recursive: true, force: true });
    fs.rmSync(badGpgKey.directory, { recursive: true, force: true });
  });

  assert.equal(badPpaTarget.status, 0, badPpaTarget.stderr);
  assert.match(badPpaTarget.output, /ppa=false/);
  assert.match(badPpaTarget.output, /chocolatey=false/);
  assert.match(badPpaTarget.summary, /LAUNCHPAD_PPA must use the form/);
  assert.match(badPpaTarget.summary, /CHOCOLATEY_API_KEY contains whitespace/);
  assert.equal(badGpgKey.status, 0, badGpgKey.stderr);
  assert.match(badGpgKey.output, /ppa=false/);
  assert.match(badGpgKey.summary, /GPG_PRIVATE_KEY is not an importable/);
});

test('required Discord webhook must have the expected endpoint shape', (t) => {
  const valid = runValidator({
    mode: 'discord',
    env: {
      DISCORD_RELEASE_WEBHOOK:
        'https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789_-',
    },
  });
  const invalid = runValidator({
    mode: 'discord',
    env: { DISCORD_RELEASE_WEBHOOK: 'https://example.invalid/webhook/token' },
  });
  const whitespace = runValidator({
    mode: 'discord',
    env: {
      DISCORD_RELEASE_WEBHOOK:
        ' https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789_-',
    },
  });
  t.after(() => {
    fs.rmSync(valid.directory, { recursive: true, force: true });
    fs.rmSync(invalid.directory, { recursive: true, force: true });
    fs.rmSync(whitespace.directory, { recursive: true, force: true });
  });

  assert.equal(valid.status, 0, valid.stderr);
  assert.match(valid.stdout, /format is valid/);
  assert.equal(invalid.status, 1);
  assert.match(invalid.stderr, /valid HTTPS Discord webhook URL/);
  assert.equal(whitespace.status, 1);
});
