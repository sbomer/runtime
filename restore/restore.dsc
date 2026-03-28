// NuGet restore pip — runs dotnet restore for OOB library projects.
// BuildXL's content-based caching tracks all source file reads (csproj, props, targets,
// NuGet.config, global.json, etc.) so the pip only re-executes when inputs actually change.
// Cache hit: ~0s vs ~18s for dotnet restore every time.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const homeDir = Context.getMount("UserProfile").path;
const repoRoot = d`..`;

const bash : Transformer.ToolDefinition = {
    exe: f`/bin/bash`,
    dependsOnCurrentHostOSDirectories: true,
    untrackedDirectoryScopes: [
        // NuGet package cache — restored packages don't need tracking
        d`${homeDir}/.nuget`,
        // .NET SDK installation
        d`${homeDir}/.dotnet`,
        // .NET local data (NuGet HTTP cache, plugins)
        d`${homeDir}/.local`,
        // Temp files
        d`/tmp`,
        // System directories
        d`/proc`,
        d`/sys`,
        d`/etc`,
        d`/dev`,
    ],
};

@@public
export const restore = Transformer.execute({
    tool: bash,
    arguments: [
        Cmd.argument(Artifact.input(f`../build.sh`)),
        Cmd.rawArgument("--restore"),
        Cmd.rawArgument("--subset"),
        Cmd.rawArgument("libs.oob"),
    ],
    workingDirectory: repoRoot,
    outputs: [
        // project.assets.json files and other restore outputs go under artifacts/
        { kind: "shared", directory: d`../artifacts` },
    ],
    // Allow undeclared source reads — dotnet restore reads many csproj/props/targets files
    // across the repo tree. BuildXL will dynamically track these for caching.
    allowUndeclaredSourceReads: true,
    unsafe: {
        // Environment variables not included in pip fingerprint
        passThroughEnvironmentVariables: ["HOME", "USER", "LANG", "PATH", "TERM"],
        // Arcade SDK does chmod +x on eng/common/dotnet-install.sh (already executable,
        // but the sandbox blocks the write). Untrack eng/common/ to avoid this.
        // Untrack artifacts/log and artifacts/tmp — ephemeral build logs that pre-exist
        // on disk from prior runs. Writing to pre-existing files in a shared opaque is
        // treated as a "rewrite" DFA. Logs don't affect caching correctness.
        untrackedScopes: [
            d`../eng/common`,
            d`../artifacts/log`,
            d`../artifacts/tmp`,
        ],
    },
    environmentVariables: [
        { name: "DOTNET_NOLOGO", value: "1" },
        { name: "DOTNET_CLI_TELEMETRY_OPTOUT", value: "1" },
        // Disable targeting pack caching (as runtime's build.sh does)
        { name: "DOTNETSDK_ALLOW_TARGETING_PACK_CACHING", value: "0" },
    ],
    // Restore can be slow on first run
    tempDirectory: Context.getTempDirectory("restore"),
});
