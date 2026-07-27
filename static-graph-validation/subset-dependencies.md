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
| `host.native` | None | `corehost.proj` contains the native host product build and is independently buildable. |
| `host.tools` | None | The subset contains the independently buildable `Microsoft.NET.HostModel` managed project. |
| `host.pkg` | The native host binaries, including `singlefilehost` produced by the CoreCLR native build | The normal build orders `clr.runtime` and `host.native` before the host packaging projects. `corehost.proj` copies `singlefilehost` out of the CoreCLR artifacts, and the pkgproj consumes the copied binaries by path. Validation builds `clr.runtime+host.native` before each isolated phase. |
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
- `host.pkg` packs `Microsoft.NETCore.DotNetAppHost.pkgproj` twice: once RID-agnostic and once with
  `PackageTargetRuntime=$(TargetRid)`. The RID-agnostic configuration additionally queries
  `GetPackageIdentity` on one configuration of itself per supported RID through
  `CreateRuntimeDependencyItems`. Those self-invocations are not graph nodes, but MSBuild allows a
  project to call itself in isolated mode, and the isolated build executes them identically to the
  normal build. The comparison reports them as dynamic-only because the graph build reuses cached
  project instances instead of emitting separate evaluations for them.
