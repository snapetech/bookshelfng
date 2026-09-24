#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import process from 'node:process';

import {
  changedReleaseNoteFiles,
  formatCaptureMetadata,
  formatCuratedNotes,
  hasExplicitNoReleaseNote,
  readReleaseNotes,
} from './release-notes.mjs';

const args = new Map();
for (let index = 2; index < process.argv.length; index += 1) {
  const argument = process.argv[index];
  if (!argument.startsWith('--')) {
    continue;
  }
  args.set(argument, process.argv[index + 1]);
  index += 1;
}

const base = args.get('--base');
const head = args.get('--head');
const bodyFile = args.get('--pr-body');
const summaryFile = args.get('--summary-file');

if (!base || !head || !bodyFile) {
  console.error(
    'Usage: check-release-notes.mjs --base <sha> --head <sha> --pr-body <file> [--summary-file <file>]'
  );
  process.exit(2);
}

const entries = changedReleaseNoteFiles(base, head);
const modified = entries.filter((entry) => entry.status !== 'A');
const tagNames = execFileSync('git', ['tag', '--list', 'main-v*'], {
  encoding: 'utf8',
})
  .split('\n')
  .filter(Boolean);
const restoredToTaggedVersion = (entry) => {
  const current = fs.readFileSync(entry.file, 'utf8');
  return tagNames.some((tag) => {
    try {
      return (
        execFileSync('git', ['show', `${tag}:${entry.file}`], {
          encoding: 'utf8',
          stdio: ['ignore', 'pipe', 'ignore'],
        }) === current
      );
    } catch {
      return false;
    }
  });
};
const untaggedModifications = modified.filter(
  (entry) => !restoredToTaggedVersion(entry)
);
const { notes, errors } = readReleaseNotes(
  entries.filter((entry) => entry.status === 'A')
);
const body = fs.readFileSync(bodyFile, 'utf8');
const explicitNoReleaseNote = hasExplicitNoReleaseNote(body);
const issues = [...errors];

for (const entry of untaggedModifications) {
  issues.push(
    `${entry.file}: release-note fragments are append-only; add a new fragment instead of modifying an old one`
  );
}

if (modified.length > untaggedModifications.length && !explicitNoReleaseNote) {
  issues.push(
    'restoring a tagged release-note fragment is internal-only; explicitly mark the change `release-note: none`'
  );
}

if (notes.length === 0 && !explicitNoReleaseNote) {
  issues.push(
    'add a validated file under release-notes/ or explicitly mark the PR `release-note: none` for internal-only work'
  );
}

if (notes.length > 0 && explicitNoReleaseNote) {
  issues.push(
    'choose either a release-note fragment or `release-note: none`; do not select both'
  );
}

if (issues.length > 0) {
  console.error('Release-note validation failed:');
  for (const issue of issues) {
    console.error(`- ${issue}`);
  }
  process.exit(1);
}

if (summaryFile) {
  const summary =
    notes.length > 0
      ? [
          '## Release-note preview',
          '',
          formatCuratedNotes(notes),
          '',
          formatCaptureMetadata(notes),
        ].join('\n')
      : '## Release-note preview\n\nInternal-only change; no release note will be published.';
  fs.appendFileSync(summaryFile, `${summary}\n`);
}

console.log(
  notes.length > 0
    ? `Validated ${notes.length} user-facing release-note fragment(s).`
    : 'Explicitly marked as internal-only; no release note required.'
);
