#!/usr/bin/env bash
# Restore test projects that BuildXL will build.
# The Arcade subset-based restore (libs.oob+libs.tests) doesn't produce
# project.assets.json for individual test projects. This script fills that gap
# by running explicit dotnet restore with the same global properties BuildXL uses.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOTNET="$HOME/.dotnet/dotnet"

# Global properties matching config.dsc
PROPS="/p:Configuration=Debug"
PROPS="$PROPS /p:NetFrameworkMinimum=netstandard2.0"
PROPS="$PROPS /p:NetFrameworkCurrent=net10.0"
PROPS="$PROPS /p:UseLocalTargetingRuntimePack=false"
PROPS="$PROPS /p:SkipNativePackaging=true"
PROPS="$PROPS /p:SkipAsnXmlGeneration=true"

# Test projects to restore (must match fileNameEntryPoints test entries in config.dsc)
TEST_PROJECTS=(
    src/libraries/System.Net.WebHeaderCollection/tests/System.Net.WebHeaderCollection.Tests.csproj
    src/libraries/System.Web.HttpUtility/tests/System.Web.HttpUtility.Tests.csproj
    src/libraries/System.Net.WebProxy/tests/System.Net.WebProxy.Tests.csproj
    src/libraries/System.Resources.Writer/tests/System.Resources.Writer.Tests.csproj
    src/libraries/System.ComponentModel/tests/System.ComponentModel.Tests.csproj
    src/libraries/System.Collections/tests/System.Collections.Tests.csproj
    src/libraries/System.Collections.Concurrent/tests/System.Collections.Concurrent.Tests.csproj
    src/libraries/System.Collections.NonGeneric/tests/System.Collections.NonGeneric.Tests.csproj
    src/libraries/System.Collections.Specialized/tests/System.Collections.Specialized.Tests.csproj
    src/libraries/System.Collections.Immutable/tests/System.Collections.Immutable.Tests.csproj
    src/libraries/System.ComponentModel.Annotations/tests/System.ComponentModel.Annotations.Tests.csproj
    src/libraries/System.ComponentModel.EventBasedAsync/tests/System.ComponentModel.EventBasedAsync.Tests.csproj
    src/libraries/System.ComponentModel.Primitives/tests/System.ComponentModel.Primitives.Tests.csproj
    src/libraries/System.ComponentModel.TypeConverter/tests/System.ComponentModel.TypeConverter.Tests.csproj
    src/libraries/System.ComponentModel.Composition/tests/System.ComponentModel.Composition.Tests.csproj
    src/libraries/System.ComponentModel.Composition.Registration/tests/System.ComponentModel.Composition.Registration.Tests.csproj
    src/libraries/System.Composition.AttributedModel/tests/System.Composition.AttributeModel.Tests.csproj
    src/libraries/System.Composition.Convention/tests/System.Composition.Convention.Tests.csproj
    src/libraries/System.Composition.Hosting/tests/System.Composition.Hosting.Tests.csproj
    src/libraries/System.Composition.Runtime/tests/System.Composition.Runtime.Tests.csproj
    src/libraries/System.Composition/tests/System.Composition.Tests.csproj
    src/libraries/System.Composition.TypedParts/tests/System.Composition.TypedParts.Tests.csproj
    src/libraries/System.Console/tests/System.Console.Tests.csproj
    src/libraries/System.Diagnostics.Contracts/tests/System.Diagnostics.Contracts.Tests.csproj
    src/libraries/System.Diagnostics.DiagnosticSource/tests/System.Diagnostics.DiagnosticSource.Tests.csproj
    src/libraries/System.Diagnostics.FileVersionInfo/tests/System.Diagnostics.FileVersionInfo.Tests/System.Diagnostics.FileVersionInfo.Tests.csproj
    src/libraries/System.Diagnostics.StackTrace/tests/System.Diagnostics.StackTrace.Tests.csproj
    src/libraries/System.Diagnostics.TextWriterTraceListener/tests/System.Diagnostics.TextWriterTraceListener.Tests.csproj
    src/libraries/System.Diagnostics.TraceSource/tests/System.Diagnostics.TraceSource.Tests/System.Diagnostics.TraceSource.Tests.csproj
    src/libraries/System.Diagnostics.Tracing/tests/System.Diagnostics.Tracing.Tests.csproj
    src/libraries/System.Drawing.Primitives/tests/System.Drawing.Primitives.Tests.csproj
    src/libraries/System.Formats.Asn1/tests/System.Formats.Asn1.Tests.csproj
    src/libraries/System.Formats.Cbor/tests/System.Formats.Cbor.Tests.csproj
    src/libraries/System.Formats.Nrbf/tests/System.Formats.Nrbf.Tests.csproj
    src/libraries/System.Formats.Tar/tests/System.Formats.Tar.Tests.csproj
    src/libraries/System.IO.Compression/tests/System.IO.Compression.Tests.csproj
    src/libraries/System.IO.Compression.Brotli/tests/System.IO.Compression.Brotli.Tests.csproj
    src/libraries/System.IO.Compression.ZipFile/tests/System.IO.Compression.ZipFile.Tests.csproj
    src/libraries/System.IO.FileSystem.AccessControl/tests/System.IO.FileSystem.AccessControl.Tests.csproj
    src/libraries/System.IO.FileSystem.DriveInfo/tests/System.IO.FileSystem.DriveInfo.Tests.csproj
    src/libraries/System.IO.FileSystem.Watcher/tests/System.IO.FileSystem.Watcher.Tests.csproj
    src/libraries/System.IO.Hashing/tests/System.IO.Hashing.Tests.csproj
    src/libraries/System.IO.IsolatedStorage/tests/System.IO.IsolatedStorage.Tests.csproj
    src/libraries/System.IO.MemoryMappedFiles/tests/System.IO.MemoryMappedFiles.Tests.csproj
    src/libraries/System.IO.Packaging/tests/System.IO.Packaging.Tests.csproj
    # Skipped: System.IO.Pipelines.Tests — StreamConformanceTests dep not compatible with net10.0
    src/libraries/System.IO.Pipes/tests/System.IO.Pipes.Tests.csproj
    src/libraries/System.IO.Pipes.AccessControl/tests/System.IO.Pipes.AccessControl.Tests.csproj
    src/libraries/System.IO.Ports/tests/System.IO.Ports.Tests.csproj
    src/libraries/System.Linq/tests/System.Linq.Tests.csproj
    src/libraries/System.Linq.AsyncEnumerable/tests/System.Linq.AsyncEnumerable.Tests.csproj
    src/libraries/System.Linq.Expressions/tests/System.Linq.Expressions.Tests.csproj
    src/libraries/System.Linq.Parallel/tests/System.Linq.Parallel.Tests.csproj
    src/libraries/System.Linq.Queryable/tests/System.Linq.Queryable.Tests.csproj
    src/libraries/System.Memory/tests/System.Memory.Tests.csproj
    src/libraries/System.Memory.Data/tests/System.Memory.Data.Tests.csproj
    src/libraries/System.Net.Http/tests/UnitTests/System.Net.Http.Unit.Tests.csproj
    src/libraries/System.Net.Http.Json/tests/UnitTests/System.Net.Http.Json.Unit.Tests.csproj
    src/libraries/System.Net.HttpListener/tests/System.Net.HttpListener.Tests.csproj
    src/libraries/System.Net.Mail/tests/Unit/System.Net.Mail.Unit.Tests.csproj
    src/libraries/System.Net.NameResolution/tests/FunctionalTests/System.Net.NameResolution.Functional.Tests.csproj
    src/libraries/System.Net.NetworkInformation/tests/FunctionalTests/System.Net.NetworkInformation.Functional.Tests.csproj
    src/libraries/System.Net.Ping/tests/FunctionalTests/System.Net.Ping.Functional.Tests.csproj
    src/libraries/System.Net.Primitives/tests/FunctionalTests/System.Net.Primitives.Functional.Tests.csproj
    src/libraries/System.Net.Requests/tests/System.Net.Requests.Tests.csproj
    src/libraries/System.Net.Security/tests/FunctionalTests/System.Net.Security.Tests.csproj
    src/libraries/System.Net.ServerSentEvents/tests/System.Net.ServerSentEvents.Tests.csproj
    src/libraries/System.Net.Sockets/tests/FunctionalTests/System.Net.Sockets.Tests.csproj
    src/libraries/System.Net.WebClient/tests/System.Net.WebClient.Tests.csproj
    src/libraries/System.Net.WebSockets/tests/System.Net.WebSockets.Tests.csproj
    src/libraries/System.Net.WebSockets.Client/tests/System.Net.WebSockets.Client.Tests.csproj
    src/libraries/System.Numerics.Tensors/tests/System.Numerics.Tensors.Tests.csproj
    src/libraries/System.Numerics.Vectors/tests/System.Numerics.Vectors.Tests.csproj
    src/libraries/System.ObjectModel/tests/System.ObjectModel.Tests.csproj
    src/libraries/System.Private.Uri/tests/FunctionalTests/System.Private.Uri.Functional.Tests.csproj
    src/libraries/System.Private.Xml/tests/System.Private.Xml.Tests.csproj
    src/libraries/System.Reflection.Context/tests/System.Reflection.Context.Tests.csproj
    src/libraries/System.Reflection.DispatchProxy/tests/System.Reflection.DispatchProxy.Tests.csproj
    src/libraries/System.Reflection.Emit/tests/System.Reflection.Emit.Tests.csproj
    src/libraries/System.Reflection.Emit.ILGeneration/tests/System.Reflection.Emit.ILGeneration.Tests.csproj
    src/libraries/System.Reflection.Emit.Lightweight/tests/System.Reflection.Emit.Lightweight.Tests.csproj
    src/libraries/System.Reflection.Metadata/tests/System.Reflection.Metadata.Tests.csproj
    src/libraries/System.Reflection.MetadataLoadContext/tests/System.Reflection.MetadataLoadContext.Tests.csproj
    src/libraries/System.Reflection.TypeExtensions/tests/System.Reflection.TypeExtensions.Tests.csproj
    src/libraries/System.Resources.Extensions/tests/System.Resources.Extensions.Tests.csproj
    src/libraries/System.Runtime.CompilerServices.VisualC/tests/System.Runtime.CompilerServices.VisualC.Tests.csproj
    src/libraries/System.Runtime.InteropServices/tests/System.Runtime.InteropServices.UnitTests/System.Runtime.InteropServices.Tests.csproj
    src/libraries/System.Runtime.Intrinsics/tests/System.Runtime.Intrinsics.Tests.csproj
    src/libraries/System.Runtime.Numerics/tests/System.Runtime.Numerics.Tests.csproj
    src/libraries/System.Runtime.Serialization.Formatters/tests/System.Runtime.Serialization.Formatters.Tests.csproj
    src/libraries/System.Runtime.Serialization.Json/tests/System.Runtime.Serialization.Json.Tests.csproj
    src/libraries/System.Runtime.Serialization.Primitives/tests/System.Runtime.Serialization.Primitives.Tests.csproj
    src/libraries/System.Runtime.Serialization.Xml/tests/System.Runtime.Serialization.Xml.Tests.csproj
    src/libraries/System.Security.AccessControl/tests/System.Security.AccessControl.Tests.csproj
    src/libraries/System.Security.Claims/tests/System.Security.Claims.Tests.csproj
    src/libraries/System.Security.Cryptography/tests/System.Security.Cryptography.Tests.csproj
    src/libraries/System.Security.Cryptography.Cose/tests/System.Security.Cryptography.Cose.Tests.csproj
    src/libraries/System.Security.Cryptography.Pkcs/tests/System.Security.Cryptography.Pkcs.Tests.csproj
    src/libraries/System.Security.Cryptography.ProtectedData/tests/System.Security.Cryptography.ProtectedData.Tests.csproj
    src/libraries/System.Security.Cryptography.Xml/tests/System.Security.Cryptography.Xml.Tests.csproj
    src/libraries/System.Security.Principal.Windows/tests/System.Security.Principal.Windows.Tests.csproj
    src/libraries/System.ServiceModel.Syndication/tests/System.ServiceModel.Syndication.Tests.csproj
    src/libraries/System.Text.Encoding.CodePages/tests/System.Text.Encoding.CodePages.Tests.csproj
    src/libraries/System.Text.Encoding.Extensions/tests/System.Text.Encoding.Extensions.Tests.csproj
    src/libraries/System.Text.Encodings.Web/tests/System.Text.Encodings.Web.Tests.csproj
    src/libraries/System.Text.Json/tests/System.Text.Json.Tests/System.Text.Json.Tests.csproj
    src/libraries/System.Text.RegularExpressions/tests/FunctionalTests/System.Text.RegularExpressions.Tests.csproj
    src/libraries/System.Threading/tests/System.Threading.Tests.csproj
    src/libraries/System.Threading.AccessControl/tests/System.Threading.AccessControl.Tests.csproj
    src/libraries/System.Threading.Channels/tests/System.Threading.Channels.Tests.csproj
    src/libraries/System.Threading.Overlapped/tests/System.Threading.Overlapped.Tests.csproj
    src/libraries/System.Threading.RateLimiting/tests/System.Threading.RateLimiting.Tests.csproj
    src/libraries/System.Threading.Tasks.Dataflow/tests/System.Threading.Tasks.Dataflow.Tests.csproj
    src/libraries/System.Threading.Tasks.Parallel/tests/System.Threading.Tasks.Parallel.Tests.csproj
    src/libraries/System.Threading.Thread/tests/System.Threading.Thread.Tests.csproj
    src/libraries/System.Threading.ThreadPool/tests/System.Threading.ThreadPool.Tests.csproj
    src/libraries/System.Transactions.Local/tests/System.Transactions.Local.Tests.csproj
    src/libraries/System.Data.Common/tests/System.Data.Common.Tests.csproj
    src/libraries/System.Private.Xml.Linq/tests/misc/System.Xml.Linq.Misc.Tests.csproj
    src/libraries/Microsoft.Bcl.AsyncInterfaces/tests/Microsoft.Bcl.AsyncInterfaces.Tests.csproj
    src/libraries/Microsoft.Bcl.Cryptography/tests/Microsoft.Bcl.Cryptography.Tests.csproj
    src/libraries/Microsoft.Bcl.Memory/tests/Microsoft.Bcl.Memory.Tests.csproj
    src/libraries/Microsoft.Bcl.Numerics/tests/Microsoft.Bcl.Numerics.Tests.csproj
    src/libraries/Microsoft.Bcl.TimeProvider/tests/Microsoft.Bcl.TimeProvider.Tests.csproj
    src/libraries/Microsoft.CSharp/tests/Microsoft.CSharp.Tests.csproj
    src/libraries/Microsoft.Extensions.Caching.Memory/tests/Microsoft.Extensions.Caching.Memory.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration/tests/Microsoft.Extensions.Configuration.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.Binder/tests/UnitTests/Microsoft.Extensions.Configuration.Binder.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.CommandLine/tests/Microsoft.Extensions.Configuration.CommandLine.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.EnvironmentVariables/tests/Microsoft.Extensions.Configuration.EnvironmentVariables.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.FileExtensions/tests/Microsoft.Extensions.Configuration.FileExtensions.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.Ini/tests/Microsoft.Extensions.Configuration.Ini.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.Json/tests/Microsoft.Extensions.Configuration.Json.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.UserSecrets/tests/Microsoft.Extensions.Configuration.UserSecrets.Tests.csproj
    src/libraries/Microsoft.Extensions.Configuration.Xml/tests/Microsoft.Extensions.Configuration.Xml.Tests.csproj
    src/libraries/Microsoft.Extensions.DependencyInjection/tests/DI.Tests/Microsoft.Extensions.DependencyInjection.Tests.csproj
    src/libraries/Microsoft.Extensions.DependencyModel/tests/Microsoft.Extensions.DependencyModel.Tests.csproj
    src/libraries/Microsoft.Extensions.Diagnostics.Abstractions/tests/Microsoft.Extensions.Diagnostics.Abstractions.Tests.csproj
    src/libraries/Microsoft.Extensions.Diagnostics/tests/Microsoft.Extensions.Diagnostics.Tests.csproj
    src/libraries/Microsoft.Extensions.FileProviders.Composite/tests/Microsoft.Extensions.FileProviders.Composite.Tests.csproj
    src/libraries/Microsoft.Extensions.FileProviders.Physical/tests/Microsoft.Extensions.FileProviders.Physical.Tests.csproj
    src/libraries/Microsoft.Extensions.FileSystemGlobbing/tests/Microsoft.Extensions.FileSystemGlobbing.Tests.csproj
    src/libraries/Microsoft.Extensions.Hosting.Abstractions/tests/Microsoft.Extensions.Hosting.Abstractions.Tests.csproj
    src/libraries/Microsoft.Extensions.Hosting/tests/UnitTests/Microsoft.Extensions.Hosting.Unit.Tests.csproj
    src/libraries/Microsoft.Extensions.Hosting.Systemd/tests/Microsoft.Extensions.Hosting.Systemd.Tests.csproj
    src/libraries/Microsoft.Extensions.Http/tests/Microsoft.Extensions.Http.Tests/Microsoft.Extensions.Http.Tests.csproj
    src/libraries/Microsoft.Extensions.Logging/tests/Common/Microsoft.Extensions.Logging.Tests.csproj
    src/libraries/Microsoft.Extensions.Logging.Abstractions/tests/Microsoft.Extensions.Logging.Generators.Tests/Microsoft.Extensions.Logging.Generators.Roslyn4.8.Tests.csproj
    src/libraries/Microsoft.Extensions.Logging.Console/tests/Microsoft.Extensions.Logging.Console.Tests/Microsoft.Extensions.Logging.Console.Tests.csproj
    src/libraries/Microsoft.Extensions.Logging.EventSource/tests/Microsoft.Extensions.Logging.EventSource.Tests.csproj
    src/libraries/Microsoft.Extensions.Options/tests/Microsoft.Extensions.Options.Tests/Microsoft.Extensions.Options.Tests.csproj
    src/libraries/Microsoft.Extensions.Primitives/tests/Microsoft.Extensions.Primitives.Tests.csproj
    src/libraries/Microsoft.Win32.Primitives/tests/Microsoft.Win32.Primitives.Tests.csproj
    src/libraries/Microsoft.Win32.Registry/tests/Microsoft.Win32.Registry.Tests.csproj
    src/libraries/Microsoft.Win32.Registry.AccessControl/tests/Microsoft.Win32.Registry.AccessControl.Tests.csproj
    src/libraries/Microsoft.Win32.SystemEvents/tests/Microsoft.Win32.SystemEvents.Tests.csproj
    src/libraries/System.CodeDom/tests/System.CodeDom.Tests.csproj
    src/libraries/System.Configuration.ConfigurationManager/tests/System.Configuration.ConfigurationManager.Tests.csproj
    src/libraries/System.Data.Odbc/tests/System.Data.Odbc.Tests.csproj
    src/libraries/System.Data.OleDb/tests/System.Data.OleDb.Tests.csproj
    src/libraries/System.Diagnostics.EventLog/tests/System.Diagnostics.EventLog.Tests.csproj
    src/libraries/System.Diagnostics.PerformanceCounter/tests/System.Diagnostics.PerformanceCounter.Tests.csproj
    src/libraries/System.Diagnostics.Process/tests/System.Diagnostics.Process.Tests.csproj
    src/libraries/System.DirectoryServices/tests/System.DirectoryServices.Tests.csproj
    src/libraries/System.DirectoryServices.AccountManagement/tests/System.DirectoryServices.AccountManagement.Tests.csproj
    src/libraries/System.DirectoryServices.Protocols/tests/System.DirectoryServices.Protocols.Tests.csproj
    src/libraries/System.Management/tests/System.Management.Tests.csproj
    src/libraries/System.Net.Quic/tests/FunctionalTests/System.Net.Quic.Functional.Tests.csproj
    src/libraries/System.Runtime.Caching/tests/System.Runtime.Caching.Tests.csproj
    src/libraries/System.Runtime.Serialization.Schema/tests/System.Runtime.Serialization.Schema.Tests.csproj
    src/libraries/System.Security.Cryptography.OpenSsl/tests/System.Security.Cryptography.OpenSsl.Tests.csproj
    src/libraries/System.Security.Permissions/tests/System.Security.Permissions.Tests.csproj
    src/libraries/System.ServiceProcess.ServiceController/tests/System.ServiceProcess.ServiceController.Tests.csproj
    src/libraries/System.Speech/tests/System.Speech.Tests.csproj
    src/libraries/System.Windows.Extensions/tests/System.Windows.Extensions.Tests.csproj
)

echo "Restoring ${#TEST_PROJECTS[@]} test projects (parallel)..."

# Restore in parallel using xargs. Each dotnet restore is independent.
FAIL_DIR=$(mktemp -d)
restore_one() {
    local proj="$1"
    local repo_root="$2"
    local dotnet="$3"
    local props="$4"
    local fail_dir="$5"
    if [[ ! -f "$repo_root/$proj" ]]; then
        echo "WARNING: $proj does not exist, skipping"
        return 0
    fi
    # shellcheck disable=SC2086
    if ! "$dotnet" restore "$repo_root/$proj" $props --verbosity quiet 2>/dev/null; then
        echo "$proj" > "$fail_dir/$(echo "$proj" | tr / _)"
    fi
}
export -f restore_one

printf '%s\n' "${TEST_PROJECTS[@]}" | \
    xargs -P 16 -I {} bash -c 'restore_one "$@"' _ {} "$REPO_ROOT" "$DOTNET" "$PROPS" "$FAIL_DIR"

# Collect failures
FAILED=()
for f in "$FAIL_DIR"/*; do
    [[ -f "$f" ]] && FAILED+=("$(cat "$f")")
done
rm -rf "$FAIL_DIR"

if [[ ${#FAILED[@]} -gt 0 ]]; then
    echo "WARNING: ${#FAILED[@]} test projects failed to restore:"
    for f in "${FAILED[@]}"; do
        echo "  $f"
    done
    # Don't fail the build — failed restores just mean those test projects can't be added as entry points yet
fi

echo "Test project restore complete."
