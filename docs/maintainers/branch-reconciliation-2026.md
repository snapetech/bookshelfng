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

## Upstream tracking refs

The `upstream` remote pointed to `pennydreadful/bookshelf`; its `develop`,
`eslint`, `rids`, and `tests` refs belonged to that source repository. They
were not BookshelfNG fork work and were not merged into the fork's release
line. The upstream remote is removed locally so those unrelated refs no longer
appear among maintained BookshelfNG branches. Historical code attribution and
the product's documented ancestry remain in the project documentation.
