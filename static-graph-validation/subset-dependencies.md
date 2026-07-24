# Static Graph Validation Subset Dependencies

This file records the prerequisites discovered while isolating the subsets supported by
`validate-static-graph.sh`. Artifact dependencies are not represented by project graph edges
and must be produced before the consuming subset runs.

| Validation subset | Required artifacts outside its graph | How the normal dynamic build satisfies them |
| --- | --- | --- |
| `clr` | None | The subset contains its required runtime projects and ordering. |
| `libs.native` | None | The native libraries project is independently buildable. |
| `libs.sfx` | None | `sfx.proj` explicitly references `sfx-src.proj`, `sfx-gen.proj`, and `sfx-finish.proj`. |
| `libs.oob` | Completed shared-framework and reference-pack outputs; `System.IO.Ports.Native` | The default libraries expansion orders `libs.native`, `libs.sfx`, then `libs.oob`. `@(ProjectToBuild)` defaults to `BuildInParallel=false`, so the native and SFX producers complete before OOB. Validation builds `sfx-finish.proj` and `build-native.proj` before each isolated phase. |
| `libs.pretest` | CoreCLR runtime and CoreLib; native host; completed shared-framework and reference-pack outputs | Full and bootstrap builds include `clr.runtime`, `clr.corelib`, `host.native`, and `libs.sfx`. Their `@(ProjectToBuild)` declaration order and `BuildInParallel=false` place all producers before `pretest.proj`. Validation builds `host.native+clr.runtime+clr.corelib+libs.sfx` before each isolated phase. |
| `libs.tests` | Completed testhost from `libs.pretest` and its CoreCLR, host, and SFX producers | The default libraries build places `libs.tests` after `libs.pretest` when tests are enabled. Validation builds `host.native+clr.runtime+clr.corelib+libs.sfx+libs.pretest` before the full test subset. |
| `host.pretest` | CoreCLR runtime, CoreLib, and Crossgen2; native host; native and shared-framework libraries; completed library pretest assets | The normal build completes the CoreCLR and libraries subsets before host pretest. Validation builds `host.native+clr.runtime+clr.corelib+clr.tools+libs.native+libs.sfx+libs.pretest` before each isolated phase. |
| `host.tests` | Native host test prerequisites; the shared-framework layout and managed test assets produced by `host.pretest` | The default host expansion places `host.tests` after `host.pretest`. Validation builds `host.native+clr.runtime+clr.corelib+clr.tools+libs.native+libs.sfx+libs.pretest+host.pretest` before each isolated phase. |

## Subset Expansion Notes

- The default `libs` expansion is `libs.native+libs.sfx+libs.oob+libs.pretest`.
- `libs.pretest` alone does not infer its artifact producers and fails on a clean tree.
- A `libs`-only build supplies the library artifacts in order, but expects the CoreCLR and host
  artifacts to exist already. Full and bootstrap builds include those additional producers.
- `libs.pretest` does not require OOB outputs in the validated default configuration.
- The current `libs.tests` validation uses the normal full test-project selection.
- `host.pretest` publishes a shared-framework layout through `PublishToDisk`. Static graph
  execution discovers the same `hostpretestpublish.proj` coordinator and explicit
  bundle-component references used by the normal build.
- `host.tests` represents the managed test projects' runtime `ReturnProductVersion` query through
  `hosttestversion.proj`. Project-reference replay restricts outer builds to the target frameworks
  selected by normal negotiation, avoiding unused inner builds such as the .NET Framework
  `Microsoft.NET.HostModel` configuration.
