config({
    resolvers: [
        {
            kind: "MsBuild",
            root: d`.`,
            moduleName: "RuntimeLibs",
            msBuildRuntime: "DotNetCore",
            // Graph construction uses MSBuild 9.0 (default, compatible with ProjectGraphBuilder net9.0).
            // Pip execution uses MSBuild from .NET 11 SDK (needed for net11.0 target framework).
            msBuildRuntimeLocation: f`/home/sven/.dotnet/sdk/11.0.100-preview.3.26172.108/MSBuild.dll`,
            // MSBuild writes temp files to /tmp/MSBuild* — untrack to avoid DFA violations.
            untrackedDirectoryScopes: [d`/tmp`],
            // Configuration=Debug is set explicitly so that direct ProjectReferences to generator
            // projects (which inherit no Configuration) and eng/generators.targets references
            // (which set SetConfiguration="Configuration=$(LibrariesConfiguration)" = Debug) 
            // resolve to the same MSBuild graph node, avoiding duplicate pips that share obj/ dirs.
            // Skip AsnXml code generation — the .xml.cs files are already checked in and
            // regeneration/touching would cause DFA violations between concurrent inner builds.
            globalProperties: Map.empty<string, string>()
                .add("Configuration", "Debug")
                .add("SkipAsnXmlGeneration", "true")
                // Replace .NET Framework TFM with netstandard2.0 — net462 can't build on Linux
                // (no .NET Framework reference assemblies). Setting to empty string creates
                // empty TFM segments (;;) that break MSBuild's static graph inner builds.
                // Setting to netstandard2.0 instead: all projects already have netstandard2.0
                // in their TargetFrameworks, so the duplicate gets removed by Distinct().
                .add("NetFrameworkMinimum", "netstandard2.0")
                // Disable local targeting pack resolution — OOB multi-TFM projects default to
                // UseLocalTargetingRuntimePack=true which resolves framework refs from
                // artifacts/bin/microsoft.netcore.app.ref/ (a build output directory). This causes
                // DFA violations and shared opaque scrubbing deletes the ref assemblies.
                // Setting false makes them use the SDK's targeting pack instead.
                .add("UseLocalTargetingRuntimePack", "false"),
            fileNameEntryPoints: [
                // === Tier 0: CoreLib-only facades (single-target) ===
                r`src/libraries/System.Text.Encoding.Extensions/src/System.Text.Encoding.Extensions.csproj`,
                r`src/libraries/System.Numerics.Vectors/src/System.Numerics.Vectors.csproj`,
                r`src/libraries/System.Reflection.Primitives/src/System.Reflection.Primitives.csproj`,
                r`src/libraries/System.Threading.Overlapped/src/System.Threading.Overlapped.csproj`,
                r`src/libraries/System.Diagnostics.Tracing/src/System.Diagnostics.Tracing.csproj`,
                r`src/libraries/System.Diagnostics.Contracts/src/System.Diagnostics.Contracts.csproj`,
                r`src/libraries/System.Threading.Thread/src/System.Threading.Thread.csproj`,
                r`src/libraries/System.Threading.ThreadPool/src/System.Threading.ThreadPool.csproj`,
                r`src/libraries/System.Runtime.Loader/src/System.Runtime.Loader.csproj`,
                r`src/libraries/System.Reflection.Emit.ILGeneration/src/System.Reflection.Emit.ILGeneration.csproj`,
                r`src/libraries/System.Reflection.Emit.Lightweight/src/System.Reflection.Emit.Lightweight.csproj`,
                r`src/libraries/Microsoft.Win32.Primitives/src/Microsoft.Win32.Primitives.csproj`,
                // === Tier 0: CoreLib-only real code (single-target) ===
                r`src/libraries/System.Reflection.TypeExtensions/src/System.Reflection.TypeExtensions.csproj`,
                r`src/libraries/System.Runtime.Intrinsics/src/System.Runtime.Intrinsics.csproj`,
                r`src/libraries/System.Threading/src/System.Threading.csproj`,
                r`src/libraries/System.Memory/src/System.Memory.csproj`,
                r`src/libraries/System.Collections/src/System.Collections.csproj`,
                r`src/libraries/System.Private.Uri/src/System.Private.Uri.csproj`,
                // === Tier 1: depends on Tier 0 libraries ===
                r`src/libraries/System.Runtime/src/System.Runtime.csproj`,
                r`src/libraries/System.Collections.Concurrent/src/System.Collections.Concurrent.csproj`,
                r`src/libraries/System.Collections.NonGeneric/src/System.Collections.NonGeneric.csproj`,
                r`src/libraries/System.ComponentModel/src/System.ComponentModel.csproj`,
                r`src/libraries/System.ObjectModel/src/System.ObjectModel.csproj`,
                r`src/libraries/System.Resources.Writer/src/System.Resources.Writer.csproj`,
                r`src/libraries/System.Runtime.CompilerServices.VisualC/src/System.Runtime.CompilerServices.VisualC.csproj`,
                r`src/libraries/System.Runtime.InteropServices/src/System.Runtime.InteropServices.csproj`,
                r`src/libraries/System.Runtime.Numerics/src/System.Runtime.Numerics.csproj`,
                r`src/libraries/System.Runtime.Serialization.Primitives/src/System.Runtime.Serialization.Primitives.csproj`,
                r`src/libraries/System.Security.Claims/src/System.Security.Claims.csproj`,
                r`src/libraries/System.Threading.Tasks.Parallel/src/System.Threading.Tasks.Parallel.csproj`,
                // === Tier 2: depends on Tier 1 libraries ===
                r`src/libraries/System.Linq/src/System.Linq.csproj`,
                r`src/libraries/System.Collections.Specialized/src/System.Collections.Specialized.csproj`,
                r`src/libraries/System.ComponentModel.Primitives/src/System.ComponentModel.Primitives.csproj`,
                r`src/libraries/System.ComponentModel.EventBasedAsync/src/System.ComponentModel.EventBasedAsync.csproj`,
                r`src/libraries/System.Diagnostics.TraceSource/src/System.Diagnostics.TraceSource.csproj`,
                r`src/libraries/System.Net.WebHeaderCollection/src/System.Net.WebHeaderCollection.csproj`,
                r`src/libraries/System.Web.HttpUtility/src/System.Web.HttpUtility.csproj`,
                // === Multi-TFM projects (unlocking blocked single-target deps) ===
                r`src/libraries/System.Collections.Immutable/src/System.Collections.Immutable.csproj`,
                r`src/libraries/System.Reflection.Metadata/src/System.Reflection.Metadata.csproj`,
                r`src/libraries/System.Runtime.Serialization.Formatters/src/System.Runtime.Serialization.Formatters.csproj`,
                r`src/libraries/System.Drawing.Primitives/src/System.Drawing.Primitives.csproj`,
                // === Previously-blocked single-target projects (unblocked by multi-TFM deps above) ===
                r`src/libraries/System.Reflection.Emit/src/System.Reflection.Emit.csproj`,
                r`src/libraries/System.Diagnostics.StackTrace/src/System.Diagnostics.StackTrace.csproj`,
                r`src/libraries/System.Text.RegularExpressions/src/System.Text.RegularExpressions.csproj`,
                r`src/libraries/System.Linq.Expressions/src/System.Linq.Expressions.csproj`,
                r`src/libraries/System.Linq.Queryable/src/System.Linq.Queryable.csproj`,
                r`src/libraries/System.Reflection.DispatchProxy/src/System.Reflection.DispatchProxy.csproj`,
                r`src/libraries/System.Linq.Parallel/src/System.Linq.Parallel.csproj`,
                // === Crypto dependency chain ===
                r`src/libraries/System.Formats.Asn1/src/System.Formats.Asn1.csproj`,
                r`src/libraries/System.IO.MemoryMappedFiles/src/System.IO.MemoryMappedFiles.csproj`,
                r`src/libraries/System.Net.Primitives/src/System.Net.Primitives.csproj`,
                r`src/libraries/System.Console/src/System.Console.csproj`,
                r`src/libraries/System.Diagnostics.DiagnosticSource/src/System.Diagnostics.DiagnosticSource.csproj`,
                r`src/libraries/System.Security.Principal.Windows/src/System.Security.Principal.Windows.csproj`,
                r`src/libraries/System.Net.NameResolution/src/System.Net.NameResolution.csproj`,
                r`src/libraries/System.Net.Sockets/src/System.Net.Sockets.csproj`,
                r`src/libraries/System.Security.Cryptography/src/System.Security.Cryptography.csproj`,
                // === Higher-level libraries ===
                r`src/libraries/System.ComponentModel.TypeConverter/src/System.ComponentModel.TypeConverter.csproj`,
                r`src/libraries/System.ComponentModel.Annotations/src/System.ComponentModel.Annotations.csproj`,
                // === Batch 2: leaf/near-leaf multi-TFM projects ===
                r`src/libraries/Microsoft.Bcl.AsyncInterfaces/src/Microsoft.Bcl.AsyncInterfaces.csproj`,
                r`src/libraries/Microsoft.Bcl.Memory/src/Microsoft.Bcl.Memory.csproj`,
                r`src/libraries/Microsoft.Bcl.Numerics/src/Microsoft.Bcl.Numerics.csproj`,
                r`src/libraries/Microsoft.Extensions.Primitives/src/Microsoft.Extensions.Primitives.csproj`,
                r`src/libraries/Microsoft.Extensions.FileSystemGlobbing/src/Microsoft.Extensions.FileSystemGlobbing.csproj`,
                r`src/libraries/Microsoft.Win32.SystemEvents/src/Microsoft.Win32.SystemEvents.csproj`,
                r`src/libraries/System.CodeDom/src/System.CodeDom.csproj`,
                r`src/libraries/System.ComponentModel.Composition/src/System.ComponentModel.Composition.csproj`,
                r`src/libraries/System.Composition.AttributedModel/src/System.Composition.AttributedModel.csproj`,
                r`src/libraries/System.Composition.Runtime/src/System.Composition.Runtime.csproj`,
                r`src/libraries/System.Formats.Cbor/src/System.Formats.Cbor.csproj`,
                r`src/libraries/System.IO.Hashing/src/System.IO.Hashing.csproj`,
                r`src/libraries/System.Reflection.Context/src/System.Reflection.Context.csproj`,
                r`src/libraries/System.Security.Cryptography.ProtectedData/src/System.Security.Cryptography.ProtectedData.csproj`,
                r`src/libraries/Microsoft.CSharp/src/Microsoft.CSharp.csproj`,
                // === Batch 2: Tier 1 (deps already built or in Tier 0 above) ===
                r`src/libraries/System.Text.Encodings.Web/src/System.Text.Encodings.Web.csproj`,
                r`src/libraries/System.Text.Encoding.CodePages/src/System.Text.Encoding.CodePages.csproj`,
                r`src/libraries/System.IO.Compression/src/System.IO.Compression.csproj`,
                r`src/libraries/System.IO.Pipelines/src/System.IO.Pipelines.csproj`,
                r`src/libraries/System.IO.FileSystem.DriveInfo/src/System.IO.FileSystem.DriveInfo.csproj`,
                r`src/libraries/System.Formats.Tar/src/System.Formats.Tar.csproj`,
                r`src/libraries/System.Diagnostics.FileVersionInfo/src/System.Diagnostics.FileVersionInfo.csproj`,
                r`src/libraries/System.Formats.Nrbf/src/System.Formats.Nrbf.csproj`,
                r`src/libraries/System.Security.AccessControl/src/System.Security.AccessControl.csproj`,
                r`src/libraries/System.Threading.Channels/src/System.Threading.Channels.csproj`,
                r`src/libraries/System.Threading.Tasks.Dataflow/src/System.Threading.Tasks.Dataflow.csproj`,
                r`src/libraries/System.Numerics.Tensors/src/System.Numerics.Tensors.csproj`,
                r`src/libraries/Microsoft.Bcl.Cryptography/src/Microsoft.Bcl.Cryptography.csproj`,
                r`src/libraries/System.Reflection.MetadataLoadContext/src/System.Reflection.MetadataLoadContext.csproj`,
                r`src/libraries/System.Composition.Convention/src/System.Composition.Convention.csproj`,
                r`src/libraries/System.Composition.Hosting/src/System.Composition.Hosting.csproj`,
                // === Batch 2: Tier 2 ===
                r`src/libraries/System.IO.Compression.Brotli/src/System.IO.Compression.Brotli.csproj`,
                r`src/libraries/System.IO.Compression.ZipFile/src/System.IO.Compression.ZipFile.csproj`,
                r`src/libraries/System.IO.Pipes/src/System.IO.Pipes.csproj`,
                r`src/libraries/System.IO.FileSystem.AccessControl/src/System.IO.FileSystem.AccessControl.csproj`,
                r`src/libraries/Microsoft.Win32.Registry/src/Microsoft.Win32.Registry.csproj`,
                r`src/libraries/System.Net.NetworkInformation/src/System.Net.NetworkInformation.csproj`,
                r`src/libraries/System.Net.Security/src/System.Net.Security.csproj`,
                r`src/libraries/System.Net.WebSockets/src/System.Net.WebSockets.csproj`,
                r`src/libraries/System.Net.WebProxy/src/System.Net.WebProxy.csproj`,
                r`src/libraries/System.Composition.TypedParts/src/System.Composition.TypedParts.csproj`,
                r`src/libraries/System.Security.Cryptography.Pkcs/src/System.Security.Cryptography.Pkcs.csproj`,
                r`src/libraries/System.IO.FileSystem.Watcher/src/System.IO.FileSystem.Watcher.csproj`,
                r`src/libraries/System.Linq.AsyncEnumerable/src/System.Linq.AsyncEnumerable.csproj`,
                r`src/libraries/System.Resources.Extensions/src/System.Resources.Extensions.csproj`,
            ],
        }
    ]
});
