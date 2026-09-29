# Branch reconciliation — 2026-09-28

Maintained BookshelfNG work is consolidated on `main`. The fork's `main` is
the shipping line; obsolete topic refs and the local upstream remote are
removed after the completed push.

## Maintained branch outcomes

| Branch | Outcome |
| --- | --- |
| `origin/packaging/catalog-submissions` | Merged into `main`. Its Co-op Cloud wishlist issue and recipe remain drafts; the branch explicitly leaves external issue submission for a human, so no issue was submitted as part of this reconciliation. |
| `codex/yunohost-release-note` | Its YunoHost package release-note fragment is merged into `main`. The branch contained no separate application change. |

The old `azure-pipelines.yml` release pipeline was removed from `main` in
`102a87e0f`. The maintained GitHub workflows own BookshelfNG publication.

The release workflows were also narrowed to the maintained `main` line:
development image triggers and `-develop` tags were removed, the standalone
manual image publisher was retired, and release-tag validation no longer falls
back to `master`. The `main-*` tag workflow is the sole stable image publisher;
it checks tags against `origin/main` and assembles the release before
distribution.

## Upstream tracking refs

The `upstream` remote pointed to `pennydreadful/bookshelf`; its `develop`,
`eslint`, `rids`, and `tests` refs belonged to that source repository. They
were not BookshelfNG fork work and were not merged into the fork's release
line. The upstream remote is removed locally so those unrelated refs no longer
appear among maintained BookshelfNG branches. Historical code attribution and
the product's documented ancestry remain in the project documentation.
