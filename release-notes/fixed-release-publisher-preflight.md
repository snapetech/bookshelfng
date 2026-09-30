---
category: fixed
audience: operators
area: release-pipeline
action: none
breaking: false
---
Release automation now validates publisher URLs and signing keys before making a GitHub Release public. Invalid optional AUR, COPR, Launchpad, or Chocolatey settings are reported and skipped so they do not leave a release partially published. The Unraid guidance now identifies its moving Hardcover image tag.
