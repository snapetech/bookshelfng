---
category: changed
audience: operators
area: release-pipeline
action: none
breaking: false
---
RPM builds on COPR now start through a package-scoped webhook after a verified release is published. This avoids reliance on expiring COPR API credentials and limits each trigger to the BookshelfNG package.
