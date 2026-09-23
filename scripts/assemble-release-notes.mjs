#!/usr/bin/env node

import fs from 'node:fs';
import process from 'node:process';

import {
  changedReleaseNoteFiles,
  formatCuratedNotes,
  injectCuratedNotes,
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

const previous = args.get('--previous-tag');
const head = args.get('--head');
const input = args.get('--changelog');
const output = args.get('--output');

if (!previous || !head || !input || !output) {
  console.error(
    'Usage: assemble-release-notes.mjs --previous-tag <sha> --head <sha> --changelog <file> --output <file>'
  );
  process.exit(2);
}

const entries = changedReleaseNoteFiles(previous, head);
const modified = entries.filter((entry) => entry.status !== 'A');
if (modified.length > 0) {
  console.error(
    'Release-note fragments are append-only; these existing fragments were modified:'
  );
  for (const entry of modified) {
    console.error(`- ${entry.file}`);
  }
  process.exit(1);
}

const { notes, errors } = readReleaseNotes(entries);
if (errors.length > 0) {
  console.error('Release-note validation failed:');
  for (const error of errors) {
    console.error(`- ${error}`);
  }
  process.exit(1);
}

const changelog = fs.readFileSync(input, 'utf8');
const assembled = injectCuratedNotes(changelog, formatCuratedNotes(notes));
fs.writeFileSync(output, assembled);

console.log(
  notes.length > 0
    ? `Added ${notes.length} curated release-note fragment(s) to ${output}.`
    : `No new curated release-note fragments found; retained ${output}.`
);
