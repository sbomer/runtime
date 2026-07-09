# runtime-caching-allcommits

This branch is an archive branch. Its purpose is to keep reachable pointers to
experimental commits from work on enabling caching via static graph isolated
builds and BuildXL.

It is not intended to be a clean integration branch, a PR branch, or a branch
that necessarily builds. Some merged histories represent dead ends, abandoned
approaches, or partially completed investigations.

## What to preserve

The branch intentionally uses merge commits to keep experiment lines visible.
Do not squash, rebase, or otherwise rewrite this branch unless explicitly asked.

The main experiment lines merged here are:

- `runtime-buildxl`
- `runtime-incremental`
- `runtime-staticgraph`
- `staticgraph-small-subset`
- `runtime-bxl2`

Branches that were not merged separately because they were already covered by
one of the above include:

- `runtime-staticgraph-clr`
- `runtime-staticgraph3`
- `runtime-bxl`
- `runtime-1`
- `runtime-2`

## If continuing this archive later

1. Check for related branches with names or commit messages mentioning static
   graph, BuildXL, BXL, caching, or incremental builds.
2. Prefer `git merge --no-ff` with an explicit archive-style merge message so
   each experiment line remains visible.
3. Before merging a branch, check whether it is already reachable from this
   branch with `git merge-base --is-ancestor <branch> HEAD`.
4. If conflicts appear, resolve them only enough to complete the archival merge;
   do not spend time trying to make the aggregate branch production-ready unless
   explicitly asked.
5. Check `git stash list` for related WIP stashes. Stashes are not preserved by
   branch merges, so convert important ones to branches or commits if they need
   long-term retention.

