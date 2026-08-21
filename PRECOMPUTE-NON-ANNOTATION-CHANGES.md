# Non-annotation precompute changes

This file tracks changes made during precompute enablement that do more than add
`PrecomputeInputs`, `PrecomputeOutputs`, `PrecomputeMerge`,
`PrecomputationMode`, or deferred read/write annotations. Update it whenever a
new behavioral, path, graph, bootstrap, or workflow change is introduced.

## Runtime repository

| Area | Change | Reason | Commits |
| --- | --- | --- | --- |
| Build driver | Added the reproducible restore/meta/product driver, package overlays, prerequisite restores, SDK override prebuilds, and bootstrap-target patching. | The product graph needs deterministic restored inputs and must reapply changes to generated SDK/package files. | `2c31589f83c`, `3d2ed6864fb`, `0a06753c54d`, `da941d09fc9`, `f4d0a463d07` |
| Bootstrap metadata | Seed the local targeting-pack `FrameworkList.xml` from the highest repo-local SDK pack before graph construction. | A stale .NET 10 manifest resolved local references under the wrong target framework. | `9f3cb6e293a` |
| Targeting-pack paths | Rewrite local targeting-pack references to the isolated `unflattened/<assembly>` producer paths. | Flattened paths have multiple potential writers and do not identify the actual BuildXL producer. | `9f3cb6e293a` |
| Unicode test outputs | Keep restore state under the Unicode project path but redirect generated intermediate and output files to `HelloWorld_Unicode`. | The Linux BuildXL sandbox did not authorize writes below the Unicode artifact directory. | `1c36495e985` |
| Tool output isolation | Put shared runtime tools, corehost versions, native library versions, Mono versions, and tool-variant PDBs in producer-specific locations. | Removes multiple writers and hidden last-writer-wins behavior. | `692961fe902`, `a0cb58c8f7c`, `5a413d3491a`, `a6fcfaaf254`, `3727d428512` |
| Symbol staging | Merge symbol staging with the output-copy operation and keep tool variants in separate staging directories. | Avoids a second writer and collisions between identically named PDBs. | `a0cb58c8f7c`, `f2552d339d8` |
| Project graph properties | Remove native partition state from nested managed/CDAC builds, qualify CDAC references, and normalize transitive CDAC configurations. | Prevents duplicate managed project configurations and output collisions. | `bd135582d28`, `51680962be2`, `7a686b3ca40`, `4077a8ada1c` |
| Corehost graph | Unify product and test corehost builds where they otherwise produce the same files. | Avoids duplicate producers for equivalent corehost outputs. | `3accb81a230` |
| Generated state | Preserve or relocate generated ILLink XML, assembly attributes, xUnit entry points, local artifact paths, runtime artifact paths, CoreLib replacement state, and package overrides across deferred target boundaries. | A deferral file does not recreate transient target state automatically. | `d7c073147be`, `9d75d73d473`, `ca880f13764`, `8b1226fc3f0`, `17d99d71bf3`, `35d8782ed7d`, `bd46478820b` |
| Packaging execution | Generate package inputs/reports deterministically and patch bootstrap NuGet pack targets to consume project assets and each inner framework output directory. | Later SDK imports shadow package overlays, and deferred pack items do not create producer edges by themselves. | `c4157ac3cec`, `6c9feda405e`, `da941d09fc9`, `f4d0a463d07` |
| Runtime ordering | Add a CoreCLR completion marker used by local runtime-file resolution. | Directory discovery alone does not establish a cross-project producer dependency. | `11d90cbb59d` |
| Determinism | Use deterministic gzip during precompute. | Removes timestamps and other host-dependent archive output. | `11651d91f6` |
| External processes | Disable the compiler server, source-control queries, and untracked source discovery during precompute. | These processes or probes escape graph accounting or introduce unstable host state. | `f4dd6809cec`, `aebfbc1a7d2`, `f513a0499cf` |
| Redundant generation | Skip redundant host pretest restore, generated runtime platform attributes, and ASN source regeneration during precompute. | Their required state is already restored/generated elsewhere and rerunning creates duplicate or untracked work. | `b3282aff2ab`, `9e44f3eea0f`, `ba7d74843bb` |
| Empty batches | Keep empty Arcade/package placeholder batches executable where they carry required graph state. | MSBuild would otherwise omit nodes that downstream deferred work expects. | `9bdb177245f`, `9921558a0ca` |

## MSBuild and TaskLauncher repository

| Area | Change | Reason | Commits |
| --- | --- | --- | --- |
| Linux bootstrap | Compose the Linux precompute bootstrap and use platform path separators and stable mounts/output roots. | The original assumptions were Windows-specific. | `935c0b21e`, `26546ccfd`, `4b96526f0` |
| Output model | Represent graph output directories as shared opaque directories, avoid claiming source/shared roots as exclusive outputs, and allow conservative optional outputs. | BuildXL requires different semantics for directory members and predicted files that may not be produced. | `337a498c9`, `c6dda1b89`, `76705e492` |
| Filesystem hierarchy | Correct parent/child dependency handling and preserve mapped direct file inputs. | Directory dependencies are not substitutes for explicit produced-file dependencies. | `74a60151c`, `05f51b3cb` |
| Task conversion | Preserve primitive task-item values and resolve dynamically loaded task dependencies beside TaskLauncher. | Deferred execution must reconstruct task parameters and task assemblies accurately. | `6acedd0d2`, `c9e087b26`, `57aec8a9c` |
| Restore and package prediction | Model in-place restore assets, restored project-assets inputs, bootstrap package-validation references, and NuGet pack output paths. | Restore and pack tasks read/write files that default prediction did not expose. | `7eacfdfec`, `322642042`, `019859303`, `f6288fd81` |
| RAR behavior | Limit candidate suppression/filtering to RAR, avoid modeling speculative candidates, and attach exact project-reference inputs. | RAR probes many nonexistent candidates; only resolved references should create graph edges. | `305e5909d`, `18f496771`, `77add4622`, `cb8e857cc` |
| Common outputs | Respect common output directories during graph conversion. | Multiple project configurations intentionally contribute to these directories. | `4710e04d8` |

## Known temporary workarounds

- `build-precompute.sh` patches the generated bootstrap
  `NuGet.Build.Tasks.Pack.targets`. This is reproducible but should eventually
  move into an upstream SDK/precompute contract.
- `HelloWorld_中文.csproj` uses ASCII artifact paths only during precompute.
  Remove this when the Linux BuildXL sandbox correctly handles Unicode output
  directories.
- Local targeting-pack references use isolated `unflattened` paths during
  precompute. Normal builds retain the standard flattened layout.
