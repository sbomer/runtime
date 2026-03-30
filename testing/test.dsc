// Test runner module — creates DScript pips for running xunit tests via `dotnet test`.
// Each test pip depends on the MSBuild build outputs (via cross-resolver import from "RuntimeLibs")
// and is independently cacheable.
//
// AUTO-GENERATED from config.dsc test entry points.

// Qualifier declaration: must include all keys from the MSBuild resolver's globalProperties
// so that the qualifier flows correctly through importFrom("RuntimeLibs") to
// GetProjectOutputsAsync, which matches qualifier keys against MSBuild project global properties.
// Without this, V2 DScript modules coerce the qualifier to {} (empty), causing DX11426.
export declare const qualifier: {
    Configuration: "Debug" | "Release",
    SkipAsnXmlGeneration: "true",
    NetFrameworkMinimum: "netstandard2.0",
    NetFrameworkCurrent: "net10.0",
    UseLocalTargetingRuntimePack: "false",
    SkipNativePackaging: "true",
};

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

// Import MSBuild build outputs for test projects.
// The identifier is the underscore-flattened relative path of the csproj (minus extension).
import {
    src_libraries_System_Net_WebHeaderCollection_tests_System_Net_WebHeaderCollection_Tests,
    src_libraries_System_Web_HttpUtility_tests_System_Web_HttpUtility_Tests,
    src_libraries_System_Net_WebProxy_tests_System_Net_WebProxy_Tests,
    src_libraries_System_Resources_Writer_tests_System_Resources_Writer_Tests,
    src_libraries_System_ComponentModel_tests_System_ComponentModel_Tests,
    src_libraries_System_Collections_tests_System_Collections_Tests,
    src_libraries_System_Collections_Concurrent_tests_System_Collections_Concurrent_Tests,
    src_libraries_System_Collections_NonGeneric_tests_System_Collections_NonGeneric_Tests,
    src_libraries_System_Collections_Specialized_tests_System_Collections_Specialized_Tests,
    src_libraries_System_Collections_Immutable_tests_System_Collections_Immutable_Tests,
    src_libraries_System_ComponentModel_Annotations_tests_System_ComponentModel_Annotations_Tests,
    src_libraries_System_ComponentModel_EventBasedAsync_tests_System_ComponentModel_EventBasedAsync_Tests,
    src_libraries_System_ComponentModel_Primitives_tests_System_ComponentModel_Primitives_Tests,
    src_libraries_System_ComponentModel_TypeConverter_tests_System_ComponentModel_TypeConverter_Tests,
    src_libraries_System_ComponentModel_Composition_tests_System_ComponentModel_Composition_Tests,
    src_libraries_System_ComponentModel_Composition_Registration_tests_System_ComponentModel_Composition_Registration_Tests,
    src_libraries_System_Composition_AttributedModel_tests_System_Composition_AttributeModel_Tests,
    src_libraries_System_Composition_Convention_tests_System_Composition_Convention_Tests,
    src_libraries_System_Composition_Hosting_tests_System_Composition_Hosting_Tests,
    src_libraries_System_Composition_Runtime_tests_System_Composition_Runtime_Tests,
    src_libraries_System_Composition_tests_System_Composition_Tests,
    src_libraries_System_Composition_TypedParts_tests_System_Composition_TypedParts_Tests,
    src_libraries_System_Console_tests_System_Console_Tests,
    src_libraries_System_Diagnostics_Contracts_tests_System_Diagnostics_Contracts_Tests,
    src_libraries_System_Diagnostics_DiagnosticSource_tests_System_Diagnostics_DiagnosticSource_Tests,
    src_libraries_System_Diagnostics_FileVersionInfo_tests_System_Diagnostics_FileVersionInfo_Tests_System_Diagnostics_FileVersionInfo_Tests,
    src_libraries_System_Diagnostics_StackTrace_tests_System_Diagnostics_StackTrace_Tests,
    src_libraries_System_Diagnostics_TextWriterTraceListener_tests_System_Diagnostics_TextWriterTraceListener_Tests,
    src_libraries_System_Diagnostics_TraceSource_tests_System_Diagnostics_TraceSource_Tests_System_Diagnostics_TraceSource_Tests,
    src_libraries_System_Diagnostics_Tracing_tests_System_Diagnostics_Tracing_Tests,
    src_libraries_System_Drawing_Primitives_tests_System_Drawing_Primitives_Tests,
    src_libraries_System_Formats_Cbor_tests_System_Formats_Cbor_Tests,
    src_libraries_System_Formats_Nrbf_tests_System_Formats_Nrbf_Tests,
    src_libraries_System_Formats_Tar_tests_System_Formats_Tar_Tests,
    src_libraries_System_IO_Compression_tests_System_IO_Compression_Tests,
    src_libraries_System_IO_Compression_Brotli_tests_System_IO_Compression_Brotli_Tests,
    src_libraries_System_IO_Compression_ZipFile_tests_System_IO_Compression_ZipFile_Tests,
    src_libraries_System_IO_FileSystem_DriveInfo_tests_System_IO_FileSystem_DriveInfo_Tests,
    src_libraries_System_IO_FileSystem_Watcher_tests_System_IO_FileSystem_Watcher_Tests,
    src_libraries_System_IO_Hashing_tests_System_IO_Hashing_Tests,
    src_libraries_System_IO_IsolatedStorage_tests_System_IO_IsolatedStorage_Tests,
    src_libraries_System_IO_MemoryMappedFiles_tests_System_IO_MemoryMappedFiles_Tests,
    src_libraries_System_IO_Packaging_tests_System_IO_Packaging_Tests,
    src_libraries_System_IO_Pipes_tests_System_IO_Pipes_Tests,
    src_libraries_System_IO_Ports_tests_System_IO_Ports_Tests,
    src_libraries_System_Linq_tests_System_Linq_Tests,
    src_libraries_System_Linq_AsyncEnumerable_tests_System_Linq_AsyncEnumerable_Tests,
    src_libraries_System_Linq_Expressions_tests_System_Linq_Expressions_Tests,
    src_libraries_System_Linq_Parallel_tests_System_Linq_Parallel_Tests,
    src_libraries_System_Linq_Queryable_tests_System_Linq_Queryable_Tests,
    src_libraries_System_Memory_tests_System_Memory_Tests,
    src_libraries_System_Memory_Data_tests_System_Memory_Data_Tests,
    src_libraries_System_Net_Http_tests_UnitTests_System_Net_Http_Unit_Tests,
    src_libraries_System_Net_Http_Json_tests_UnitTests_System_Net_Http_Json_Unit_Tests,
    src_libraries_System_Net_HttpListener_tests_System_Net_HttpListener_Tests,
    src_libraries_System_Net_Mail_tests_Unit_System_Net_Mail_Unit_Tests,
    src_libraries_System_Net_NameResolution_tests_FunctionalTests_System_Net_NameResolution_Functional_Tests,
    src_libraries_System_Net_NetworkInformation_tests_FunctionalTests_System_Net_NetworkInformation_Functional_Tests,
    src_libraries_System_Net_Ping_tests_FunctionalTests_System_Net_Ping_Functional_Tests,
    src_libraries_System_Net_Primitives_tests_FunctionalTests_System_Net_Primitives_Functional_Tests,
    src_libraries_System_Net_Requests_tests_System_Net_Requests_Tests,
    src_libraries_System_Net_Security_tests_FunctionalTests_System_Net_Security_Tests,
    src_libraries_System_Net_ServerSentEvents_tests_System_Net_ServerSentEvents_Tests,
    src_libraries_System_Net_Sockets_tests_FunctionalTests_System_Net_Sockets_Tests,
    src_libraries_System_Net_WebClient_tests_System_Net_WebClient_Tests,
    src_libraries_System_Net_WebSockets_tests_System_Net_WebSockets_Tests,
    src_libraries_System_Net_WebSockets_Client_tests_System_Net_WebSockets_Client_Tests,
    src_libraries_System_Numerics_Tensors_tests_System_Numerics_Tensors_Tests,
    src_libraries_System_Numerics_Vectors_tests_System_Numerics_Vectors_Tests,
    src_libraries_System_ObjectModel_tests_System_ObjectModel_Tests,
    src_libraries_System_Private_Uri_tests_FunctionalTests_System_Private_Uri_Functional_Tests,
    src_libraries_System_Private_Xml_tests_System_Private_Xml_Tests,
    src_libraries_System_Reflection_Context_tests_System_Reflection_Context_Tests,
    src_libraries_System_Reflection_DispatchProxy_tests_System_Reflection_DispatchProxy_Tests,
    src_libraries_System_Reflection_Emit_tests_System_Reflection_Emit_Tests,
    src_libraries_System_Reflection_Emit_ILGeneration_tests_System_Reflection_Emit_ILGeneration_Tests,
    src_libraries_System_Reflection_Emit_Lightweight_tests_System_Reflection_Emit_Lightweight_Tests,
    src_libraries_System_Reflection_Metadata_tests_System_Reflection_Metadata_Tests,
    src_libraries_System_Reflection_MetadataLoadContext_tests_System_Reflection_MetadataLoadContext_Tests,
    src_libraries_System_Reflection_TypeExtensions_tests_System_Reflection_TypeExtensions_Tests,
    src_libraries_System_Resources_Extensions_tests_System_Resources_Extensions_Tests,
    src_libraries_System_Runtime_CompilerServices_VisualC_tests_System_Runtime_CompilerServices_VisualC_Tests,
    src_libraries_System_Runtime_InteropServices_tests_System_Runtime_InteropServices_UnitTests_System_Runtime_InteropServices_Tests,
    src_libraries_System_Runtime_Intrinsics_tests_System_Runtime_Intrinsics_Tests,
    src_libraries_System_Runtime_Numerics_tests_System_Runtime_Numerics_Tests,
    src_libraries_System_Runtime_Serialization_Formatters_tests_System_Runtime_Serialization_Formatters_Tests,
    src_libraries_System_Runtime_Serialization_Json_tests_System_Runtime_Serialization_Json_Tests,
    src_libraries_System_Runtime_Serialization_Primitives_tests_System_Runtime_Serialization_Primitives_Tests,
    src_libraries_System_Runtime_Serialization_Xml_tests_System_Runtime_Serialization_Xml_Tests,
    src_libraries_System_Security_Claims_tests_System_Security_Claims_Tests,
    src_libraries_System_Security_Cryptography_tests_System_Security_Cryptography_Tests,
    src_libraries_System_Security_Cryptography_Cose_tests_System_Security_Cryptography_Cose_Tests,
    src_libraries_System_Security_Cryptography_Pkcs_tests_System_Security_Cryptography_Pkcs_Tests,
    src_libraries_System_Security_Cryptography_ProtectedData_tests_System_Security_Cryptography_ProtectedData_Tests,
    src_libraries_System_Security_Cryptography_Xml_tests_System_Security_Cryptography_Xml_Tests,
    src_libraries_System_ServiceModel_Syndication_tests_System_ServiceModel_Syndication_Tests,
    src_libraries_System_Text_Encoding_CodePages_tests_System_Text_Encoding_CodePages_Tests,
    src_libraries_System_Text_Encoding_Extensions_tests_System_Text_Encoding_Extensions_Tests,
    src_libraries_System_Text_Encodings_Web_tests_System_Text_Encodings_Web_Tests,
    src_libraries_System_Threading_tests_System_Threading_Tests,
    src_libraries_System_Threading_AccessControl_tests_System_Threading_AccessControl_Tests,
    src_libraries_System_Threading_Channels_tests_System_Threading_Channels_Tests,
    src_libraries_System_Threading_Overlapped_tests_System_Threading_Overlapped_Tests,
    src_libraries_System_Threading_RateLimiting_tests_System_Threading_RateLimiting_Tests,
    src_libraries_System_Threading_Tasks_Dataflow_tests_System_Threading_Tasks_Dataflow_Tests,
    src_libraries_System_Threading_Tasks_Parallel_tests_System_Threading_Tasks_Parallel_Tests,
    src_libraries_System_Threading_Thread_tests_System_Threading_Thread_Tests,
    src_libraries_System_Threading_ThreadPool_tests_System_Threading_ThreadPool_Tests,
    src_libraries_System_Transactions_Local_tests_System_Transactions_Local_Tests,
    src_libraries_System_Data_Common_tests_System_Data_Common_Tests,
    src_libraries_System_Private_Xml_Linq_tests_misc_System_Xml_Linq_Misc_Tests,
    src_libraries_Microsoft_Bcl_AsyncInterfaces_tests_Microsoft_Bcl_AsyncInterfaces_Tests,
    src_libraries_Microsoft_Bcl_Cryptography_tests_Microsoft_Bcl_Cryptography_Tests,
    src_libraries_Microsoft_Bcl_Memory_tests_Microsoft_Bcl_Memory_Tests,
    src_libraries_Microsoft_Bcl_Numerics_tests_Microsoft_Bcl_Numerics_Tests,
    src_libraries_Microsoft_Bcl_TimeProvider_tests_Microsoft_Bcl_TimeProvider_Tests,
    src_libraries_Microsoft_CSharp_tests_Microsoft_CSharp_Tests,
    src_libraries_Microsoft_Extensions_Caching_Memory_tests_Microsoft_Extensions_Caching_Memory_Tests,
    src_libraries_Microsoft_Extensions_Configuration_tests_Microsoft_Extensions_Configuration_Tests,
    src_libraries_Microsoft_Extensions_Configuration_Binder_tests_UnitTests_Microsoft_Extensions_Configuration_Binder_Tests,
    src_libraries_Microsoft_Extensions_Configuration_CommandLine_tests_Microsoft_Extensions_Configuration_CommandLine_Tests,
    src_libraries_Microsoft_Extensions_Configuration_EnvironmentVariables_tests_Microsoft_Extensions_Configuration_EnvironmentVariables_Tests,
    src_libraries_Microsoft_Extensions_Configuration_FileExtensions_tests_Microsoft_Extensions_Configuration_FileExtensions_Tests,
    src_libraries_Microsoft_Extensions_Configuration_Ini_tests_Microsoft_Extensions_Configuration_Ini_Tests,
    src_libraries_Microsoft_Extensions_Configuration_Json_tests_Microsoft_Extensions_Configuration_Json_Tests,
    src_libraries_Microsoft_Extensions_Configuration_UserSecrets_tests_Microsoft_Extensions_Configuration_UserSecrets_Tests,
    src_libraries_Microsoft_Extensions_Configuration_Xml_tests_Microsoft_Extensions_Configuration_Xml_Tests,
    src_libraries_Microsoft_Extensions_DependencyInjection_tests_DI_Tests_Microsoft_Extensions_DependencyInjection_Tests,
    src_libraries_Microsoft_Extensions_DependencyModel_tests_Microsoft_Extensions_DependencyModel_Tests,
    src_libraries_Microsoft_Extensions_Diagnostics_Abstractions_tests_Microsoft_Extensions_Diagnostics_Abstractions_Tests,
    src_libraries_Microsoft_Extensions_Diagnostics_tests_Microsoft_Extensions_Diagnostics_Tests,
    src_libraries_Microsoft_Extensions_FileProviders_Composite_tests_Microsoft_Extensions_FileProviders_Composite_Tests,
    src_libraries_Microsoft_Extensions_FileProviders_Physical_tests_Microsoft_Extensions_FileProviders_Physical_Tests,
    src_libraries_Microsoft_Extensions_FileSystemGlobbing_tests_Microsoft_Extensions_FileSystemGlobbing_Tests,
    src_libraries_Microsoft_Extensions_Hosting_Abstractions_tests_Microsoft_Extensions_Hosting_Abstractions_Tests,
    src_libraries_Microsoft_Extensions_Hosting_tests_UnitTests_Microsoft_Extensions_Hosting_Unit_Tests,
    src_libraries_Microsoft_Extensions_Hosting_Systemd_tests_Microsoft_Extensions_Hosting_Systemd_Tests,
    src_libraries_Microsoft_Extensions_Http_tests_Microsoft_Extensions_Http_Tests_Microsoft_Extensions_Http_Tests,
    src_libraries_Microsoft_Extensions_Logging_tests_Common_Microsoft_Extensions_Logging_Tests,
    src_libraries_Microsoft_Extensions_Logging_Abstractions_tests_Microsoft_Extensions_Logging_Generators_Tests_Microsoft_Extensions_Logging_Generators_Roslyn4_8_Tests,
    src_libraries_Microsoft_Extensions_Logging_Console_tests_Microsoft_Extensions_Logging_Console_Tests_Microsoft_Extensions_Logging_Console_Tests,
    src_libraries_Microsoft_Extensions_Logging_EventSource_tests_Microsoft_Extensions_Logging_EventSource_Tests,
    src_libraries_Microsoft_Extensions_Options_tests_Microsoft_Extensions_Options_Tests_Microsoft_Extensions_Options_Tests,
    src_libraries_Microsoft_Extensions_Primitives_tests_Microsoft_Extensions_Primitives_Tests,
    src_libraries_Microsoft_Win32_Primitives_tests_Microsoft_Win32_Primitives_Tests,
    src_libraries_Microsoft_Win32_Registry_AccessControl_tests_Microsoft_Win32_Registry_AccessControl_Tests,
    src_libraries_Microsoft_Win32_SystemEvents_tests_Microsoft_Win32_SystemEvents_Tests,
    src_libraries_System_CodeDom_tests_System_CodeDom_Tests,
    src_libraries_System_Configuration_ConfigurationManager_tests_System_Configuration_ConfigurationManager_Tests,
    src_libraries_System_Data_OleDb_tests_System_Data_OleDb_Tests,
    src_libraries_System_Diagnostics_EventLog_tests_System_Diagnostics_EventLog_Tests,
    src_libraries_System_Diagnostics_Process_tests_System_Diagnostics_Process_Tests,
    src_libraries_System_DirectoryServices_tests_System_DirectoryServices_Tests,
    src_libraries_System_DirectoryServices_AccountManagement_tests_System_DirectoryServices_AccountManagement_Tests,
    src_libraries_System_DirectoryServices_Protocols_tests_System_DirectoryServices_Protocols_Tests,
    src_libraries_System_Management_tests_System_Management_Tests,
    src_libraries_System_Net_Quic_tests_FunctionalTests_System_Net_Quic_Functional_Tests,
    src_libraries_System_Runtime_Caching_tests_System_Runtime_Caching_Tests,
    src_libraries_System_Runtime_Serialization_Schema_tests_System_Runtime_Serialization_Schema_Tests,
    src_libraries_System_Security_Cryptography_OpenSsl_tests_System_Security_Cryptography_OpenSsl_Tests,
    src_libraries_System_ServiceProcess_ServiceController_tests_System_ServiceProcess_ServiceController_Tests,
} from "RuntimeLibs";

const dotnetTool: Transformer.ToolDefinition = {
    exe: f`/home/sven/.dotnet/dotnet`,
    dependsOnCurrentHostOSDirectories: true,
    runtimeDependencies: [],
    untrackedDirectoryScopes: [
        d`/home/sven/.dotnet`,
        d`/tmp`,
        d`/proc`,
        d`/sys`,
        d`/etc`,
    ],
};

const repoRoot = d`${Context.getMount("SourceRoot").path}`;

interface TestProject {
    name: string;
    testDll: string;
    buildOutputs: SharedOpaqueDirectory[];
    tfm?: string;  // Target framework moniker, defaults to "net11.0"
}

function runTest(project: TestProject): Transformer.ExecuteResult {
    // The test DLL is at: artifacts/bin/<name>/Debug/<tfm>/<name>.dll
    // Platform-specific tests use net11.0-unix, net11.0-linux, or net10.0 instead of net11.0
    const tfm = project.tfm || "net11.0";
    const testBinDir = d`${repoRoot}/artifacts/bin/${project.name}/Debug/${tfm}`;
    const resultDir = d`${repoRoot}/artifacts/TestResults/${project.name}`;

    return Transformer.execute({
        tool: dotnetTool,
        workingDirectory: testBinDir,
        arguments: [
            Cmd.argument("test"),
            Cmd.argument("--no-build"),
            Cmd.argument("--no-restore"),
            Cmd.argument(Artifact.none(f`${testBinDir}/${project.testDll}`)),
            Cmd.argument("--results-directory"),
            Cmd.argument(Artifact.none(resultDir)),
            Cmd.argument("--logger"),
            Cmd.argument("trx"),
        ],
        outputs: [
            // Test results as shared opaque — dotnet test writes .trx files with dynamic names
            { kind: "shared", directory: resultDir },
        ],
        dependencies: [
            // Depend on the MSBuild build outputs for this test project
            ...project.buildOutputs,
        ],
        environmentVariables: [
            { name: "DOTNET_HOST_PATH", value: "/home/sven/.dotnet/dotnet" },
            { name: "DOTNET_CLI_HOME", value: "/tmp/dotnet-cli-home" },
        ],
        unsafe: {
            untrackedScopes: [
                // NuGet package cache (read by dotnet test for deps resolution)
                d`/home/sven/.nuget`,
                // dotnet SDK
                d`/home/sven/.dotnet`,
                // System directories
                d`/usr`,
                d`/tmp`,
                d`/proc`,
                d`/sys`,
                d`/etc`,
                // BuildXL's redirected temp directory — .NET CLR creates debug pipes
                // (clr-debug-pipe-*-in/out) here for debugger communication
                d`/home/sven/Microsoft/BuildXL/RestrictedTemp`,
            ],
            passThroughEnvironmentVariables: [
                "HOME",
                "PATH",
                "LANG",
                "TERM",
            ],
        },
        allowUndeclaredSourceReads: true,
    });
}

// === Test execution pips ===

@@public
export const systemNetWebHeaderCollectionTests = runTest({
    name: "System.Net.WebHeaderCollection.Tests",
    testDll: "System.Net.WebHeaderCollection.Tests.dll",
    buildOutputs: src_libraries_System_Net_WebHeaderCollection_tests_System_Net_WebHeaderCollection_Tests,
});

@@public
export const systemWebHttpUtilityTests = runTest({
    name: "System.Web.HttpUtility.Tests",
    testDll: "System.Web.HttpUtility.Tests.dll",
    buildOutputs: src_libraries_System_Web_HttpUtility_tests_System_Web_HttpUtility_Tests,
});

@@public
export const systemNetWebProxyTests = runTest({
    name: "System.Net.WebProxy.Tests",
    testDll: "System.Net.WebProxy.Tests.dll",
    buildOutputs: src_libraries_System_Net_WebProxy_tests_System_Net_WebProxy_Tests,
});

@@public
export const systemResourcesWriterTests = runTest({
    name: "System.Resources.Writer.Tests",
    testDll: "System.Resources.Writer.Tests.dll",
    buildOutputs: src_libraries_System_Resources_Writer_tests_System_Resources_Writer_Tests,
});

@@public
export const systemComponentModelTests = runTest({
    name: "System.ComponentModel.Tests",
    testDll: "System.ComponentModel.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_tests_System_ComponentModel_Tests,
});

@@public
export const systemCollectionsTests = runTest({
    name: "System.Collections.Tests",
    testDll: "System.Collections.Tests.dll",
    buildOutputs: src_libraries_System_Collections_tests_System_Collections_Tests,
});

@@public
export const systemCollectionsConcurrentTests = runTest({
    name: "System.Collections.Concurrent.Tests",
    testDll: "System.Collections.Concurrent.Tests.dll",
    buildOutputs: src_libraries_System_Collections_Concurrent_tests_System_Collections_Concurrent_Tests,
});

@@public
export const systemCollectionsNonGenericTests = runTest({
    name: "System.Collections.NonGeneric.Tests",
    testDll: "System.Collections.NonGeneric.Tests.dll",
    buildOutputs: src_libraries_System_Collections_NonGeneric_tests_System_Collections_NonGeneric_Tests,
});

@@public
export const systemCollectionsSpecializedTests = runTest({
    name: "System.Collections.Specialized.Tests",
    testDll: "System.Collections.Specialized.Tests.dll",
    buildOutputs: src_libraries_System_Collections_Specialized_tests_System_Collections_Specialized_Tests,
});

@@public
export const systemCollectionsImmutableTests = runTest({
    name: "System.Collections.Immutable.Tests",
    testDll: "System.Collections.Immutable.Tests.dll",
    buildOutputs: src_libraries_System_Collections_Immutable_tests_System_Collections_Immutable_Tests,
});

@@public
export const systemComponentModelAnnotationsTests = runTest({
    name: "System.ComponentModel.Annotations.Tests",
    testDll: "System.ComponentModel.Annotations.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_Annotations_tests_System_ComponentModel_Annotations_Tests,
});

@@public
export const systemComponentModelEventBasedAsyncTests = runTest({
    name: "System.ComponentModel.EventBasedAsync.Tests",
    testDll: "System.ComponentModel.EventBasedAsync.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_EventBasedAsync_tests_System_ComponentModel_EventBasedAsync_Tests,
});

@@public
export const systemComponentModelPrimitivesTests = runTest({
    name: "System.ComponentModel.Primitives.Tests",
    testDll: "System.ComponentModel.Primitives.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_Primitives_tests_System_ComponentModel_Primitives_Tests,
});

@@public
export const systemComponentModelTypeConverterTests = runTest({
    name: "System.ComponentModel.TypeConverter.Tests",
    testDll: "System.ComponentModel.TypeConverter.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_TypeConverter_tests_System_ComponentModel_TypeConverter_Tests,
});

@@public
export const systemComponentModelCompositionTests = runTest({
    name: "System.ComponentModel.Composition.Tests",
    testDll: "System.ComponentModel.Composition.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_Composition_tests_System_ComponentModel_Composition_Tests,
});

@@public
export const systemComponentModelCompositionRegistrationTests = runTest({
    name: "System.ComponentModel.Composition.Registration.Tests",
    testDll: "System.ComponentModel.Composition.Registration.Tests.dll",
    buildOutputs: src_libraries_System_ComponentModel_Composition_Registration_tests_System_ComponentModel_Composition_Registration_Tests,
});

@@public
export const systemCompositionAttributeModelTests = runTest({
    name: "System.Composition.AttributeModel.Tests",
    testDll: "System.Composition.AttributeModel.Tests.dll",
    buildOutputs: src_libraries_System_Composition_AttributedModel_tests_System_Composition_AttributeModel_Tests,
});

@@public
export const systemCompositionConventionTests = runTest({
    name: "System.Composition.Convention.Tests",
    testDll: "System.Composition.Convention.Tests.dll",
    buildOutputs: src_libraries_System_Composition_Convention_tests_System_Composition_Convention_Tests,
});

@@public
export const systemCompositionHostingTests = runTest({
    name: "System.Composition.Hosting.Tests",
    testDll: "System.Composition.Hosting.Tests.dll",
    buildOutputs: src_libraries_System_Composition_Hosting_tests_System_Composition_Hosting_Tests,
});

@@public
export const systemCompositionRuntimeTests = runTest({
    name: "System.Composition.Runtime.Tests",
    testDll: "System.Composition.Runtime.Tests.dll",
    buildOutputs: src_libraries_System_Composition_Runtime_tests_System_Composition_Runtime_Tests,
});

@@public
export const systemCompositionTests = runTest({
    name: "System.Composition.Tests",
    testDll: "System.Composition.Tests.dll",
    buildOutputs: src_libraries_System_Composition_tests_System_Composition_Tests,
});

@@public
export const systemCompositionTypedPartsTests = runTest({
    name: "System.Composition.TypedParts.Tests",
    testDll: "System.Composition.TypedParts.Tests.dll",
    buildOutputs: src_libraries_System_Composition_TypedParts_tests_System_Composition_TypedParts_Tests,
});

@@public
export const systemConsoleTests = runTest({
    name: "System.Console.Tests",
    testDll: "System.Console.Tests.dll",
    buildOutputs: src_libraries_System_Console_tests_System_Console_Tests,
});

@@public
export const systemDiagnosticsContractsTests = runTest({
    name: "System.Diagnostics.Contracts.Tests",
    testDll: "System.Diagnostics.Contracts.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_Contracts_tests_System_Diagnostics_Contracts_Tests,
});

@@public
export const systemDiagnosticsDiagnosticSourceTests = runTest({
    name: "System.Diagnostics.DiagnosticSource.Tests",
    testDll: "System.Diagnostics.DiagnosticSource.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_DiagnosticSource_tests_System_Diagnostics_DiagnosticSource_Tests,
});

@@public
export const systemDiagnosticsFileVersionInfoTests = runTest({
    name: "System.Diagnostics.FileVersionInfo.Tests",
    testDll: "System.Diagnostics.FileVersionInfo.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_FileVersionInfo_tests_System_Diagnostics_FileVersionInfo_Tests_System_Diagnostics_FileVersionInfo_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemDiagnosticsStackTraceTests = runTest({
    name: "System.Diagnostics.StackTrace.Tests",
    testDll: "System.Diagnostics.StackTrace.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_StackTrace_tests_System_Diagnostics_StackTrace_Tests,
});

@@public
export const systemDiagnosticsTextWriterTraceListenerTests = runTest({
    name: "System.Diagnostics.TextWriterTraceListener.Tests",
    testDll: "System.Diagnostics.TextWriterTraceListener.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_TextWriterTraceListener_tests_System_Diagnostics_TextWriterTraceListener_Tests,
});

@@public
export const systemDiagnosticsTraceSourceTests = runTest({
    name: "System.Diagnostics.TraceSource.Tests",
    testDll: "System.Diagnostics.TraceSource.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_TraceSource_tests_System_Diagnostics_TraceSource_Tests_System_Diagnostics_TraceSource_Tests,
});

@@public
export const systemDiagnosticsTracingTests = runTest({
    name: "System.Diagnostics.Tracing.Tests",
    testDll: "System.Diagnostics.Tracing.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_Tracing_tests_System_Diagnostics_Tracing_Tests,
});

@@public
export const systemDrawingPrimitivesTests = runTest({
    name: "System.Drawing.Primitives.Tests",
    testDll: "System.Drawing.Primitives.Tests.dll",
    buildOutputs: src_libraries_System_Drawing_Primitives_tests_System_Drawing_Primitives_Tests,
});

@@public
export const systemFormatsCborTests = runTest({
    name: "System.Formats.Cbor.Tests",
    testDll: "System.Formats.Cbor.Tests.dll",
    buildOutputs: src_libraries_System_Formats_Cbor_tests_System_Formats_Cbor_Tests,
});

@@public
export const systemFormatsNrbfTests = runTest({
    name: "System.Formats.Nrbf.Tests",
    testDll: "System.Formats.Nrbf.Tests.dll",
    buildOutputs: src_libraries_System_Formats_Nrbf_tests_System_Formats_Nrbf_Tests,
});

@@public
export const systemFormatsTarTests = runTest({
    name: "System.Formats.Tar.Tests",
    testDll: "System.Formats.Tar.Tests.dll",
    buildOutputs: src_libraries_System_Formats_Tar_tests_System_Formats_Tar_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemIOCompressionTests = runTest({
    name: "System.IO.Compression.Tests",
    testDll: "System.IO.Compression.Tests.dll",
    buildOutputs: src_libraries_System_IO_Compression_tests_System_IO_Compression_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemIOCompressionBrotliTests = runTest({
    name: "System.IO.Compression.Brotli.Tests",
    testDll: "System.IO.Compression.Brotli.Tests.dll",
    buildOutputs: src_libraries_System_IO_Compression_Brotli_tests_System_IO_Compression_Brotli_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemIOCompressionZipFileTests = runTest({
    name: "System.IO.Compression.ZipFile.Tests",
    testDll: "System.IO.Compression.ZipFile.Tests.dll",
    buildOutputs: src_libraries_System_IO_Compression_ZipFile_tests_System_IO_Compression_ZipFile_Tests,
    tfm: "net11.0-unix",
});

// System.IO.FileSystem.AccessControl.Tests — Windows-only (net11.0-windows), skipped on Linux

@@public
export const systemIOFileSystemDriveInfoTests = runTest({
    name: "System.IO.FileSystem.DriveInfo.Tests",
    testDll: "System.IO.FileSystem.DriveInfo.Tests.dll",
    buildOutputs: src_libraries_System_IO_FileSystem_DriveInfo_tests_System_IO_FileSystem_DriveInfo_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemIOFileSystemWatcherTests = runTest({
    name: "System.IO.FileSystem.Watcher.Tests",
    testDll: "System.IO.FileSystem.Watcher.Tests.dll",
    buildOutputs: src_libraries_System_IO_FileSystem_Watcher_tests_System_IO_FileSystem_Watcher_Tests,
    tfm: "net11.0-linux",
});

@@public
export const systemIOHashingTests = runTest({
    name: "System.IO.Hashing.Tests",
    testDll: "System.IO.Hashing.Tests.dll",
    buildOutputs: src_libraries_System_IO_Hashing_tests_System_IO_Hashing_Tests,
});

@@public
export const systemIOIsolatedStorageTests = runTest({
    name: "System.IO.IsolatedStorage.Tests",
    testDll: "System.IO.IsolatedStorage.Tests.dll",
    buildOutputs: src_libraries_System_IO_IsolatedStorage_tests_System_IO_IsolatedStorage_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemIOMemoryMappedFilesTests = runTest({
    name: "System.IO.MemoryMappedFiles.Tests",
    testDll: "System.IO.MemoryMappedFiles.Tests.dll",
    buildOutputs: src_libraries_System_IO_MemoryMappedFiles_tests_System_IO_MemoryMappedFiles_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemIOPackagingTests = runTest({
    name: "System.IO.Packaging.Tests",
    testDll: "System.IO.Packaging.Tests.dll",
    buildOutputs: src_libraries_System_IO_Packaging_tests_System_IO_Packaging_Tests,
});

@@public
export const systemIOPipesTests = runTest({
    name: "System.IO.Pipes.Tests",
    testDll: "System.IO.Pipes.Tests.dll",
    buildOutputs: src_libraries_System_IO_Pipes_tests_System_IO_Pipes_Tests,
});

// System.IO.Pipes.AccessControl.Tests — Windows-only (net11.0-windows), skipped on Linux

@@public
export const systemIOPortsTests = runTest({
    name: "System.IO.Ports.Tests",
    testDll: "System.IO.Ports.Tests.dll",
    buildOutputs: src_libraries_System_IO_Ports_tests_System_IO_Ports_Tests,
    tfm: "net11.0-linux",
});

@@public
export const systemLinqTests = runTest({
    name: "System.Linq.Tests",
    testDll: "System.Linq.Tests.dll",
    buildOutputs: src_libraries_System_Linq_tests_System_Linq_Tests,
});

@@public
export const systemLinqAsyncEnumerableTests = runTest({
    name: "System.Linq.AsyncEnumerable.Tests",
    testDll: "System.Linq.AsyncEnumerable.Tests.dll",
    buildOutputs: src_libraries_System_Linq_AsyncEnumerable_tests_System_Linq_AsyncEnumerable_Tests,
});

@@public
export const systemLinqExpressionsTests = runTest({
    name: "System.Linq.Expressions.Tests",
    testDll: "System.Linq.Expressions.Tests.dll",
    buildOutputs: src_libraries_System_Linq_Expressions_tests_System_Linq_Expressions_Tests,
});

@@public
export const systemLinqParallelTests = runTest({
    name: "System.Linq.Parallel.Tests",
    testDll: "System.Linq.Parallel.Tests.dll",
    buildOutputs: src_libraries_System_Linq_Parallel_tests_System_Linq_Parallel_Tests,
});

@@public
export const systemLinqQueryableTests = runTest({
    name: "System.Linq.Queryable.Tests",
    testDll: "System.Linq.Queryable.Tests.dll",
    buildOutputs: src_libraries_System_Linq_Queryable_tests_System_Linq_Queryable_Tests,
});

@@public
export const systemMemoryTests = runTest({
    name: "System.Memory.Tests",
    testDll: "System.Memory.Tests.dll",
    buildOutputs: src_libraries_System_Memory_tests_System_Memory_Tests,
});

@@public
export const systemMemoryDataTests = runTest({
    name: "System.Memory.Data.Tests",
    testDll: "System.Memory.Data.Tests.dll",
    buildOutputs: src_libraries_System_Memory_Data_tests_System_Memory_Data_Tests,
});

@@public
export const systemNetHttpUnitTests = runTest({
    name: "System.Net.Http.Unit.Tests",
    testDll: "System.Net.Http.Unit.Tests.dll",
    buildOutputs: src_libraries_System_Net_Http_tests_UnitTests_System_Net_Http_Unit_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetHttpJsonUnitTests = runTest({
    name: "System.Net.Http.Json.Unit.Tests",
    testDll: "System.Net.Http.Json.Unit.Tests.dll",
    buildOutputs: src_libraries_System_Net_Http_Json_tests_UnitTests_System_Net_Http_Json_Unit_Tests,
});

@@public
export const systemNetHttpListenerTests = runTest({
    name: "System.Net.HttpListener.Tests",
    testDll: "System.Net.HttpListener.Tests.dll",
    buildOutputs: src_libraries_System_Net_HttpListener_tests_System_Net_HttpListener_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetMailUnitTests = runTest({
    name: "System.Net.Mail.Unit.Tests",
    testDll: "System.Net.Mail.Unit.Tests.dll",
    buildOutputs: src_libraries_System_Net_Mail_tests_Unit_System_Net_Mail_Unit_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetNameResolutionFunctionalTests = runTest({
    name: "System.Net.NameResolution.Functional.Tests",
    testDll: "System.Net.NameResolution.Functional.Tests.dll",
    buildOutputs: src_libraries_System_Net_NameResolution_tests_FunctionalTests_System_Net_NameResolution_Functional_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetNetworkInformationFunctionalTests = runTest({
    name: "System.Net.NetworkInformation.Functional.Tests",
    testDll: "System.Net.NetworkInformation.Functional.Tests.dll",
    buildOutputs: src_libraries_System_Net_NetworkInformation_tests_FunctionalTests_System_Net_NetworkInformation_Functional_Tests,
});

@@public
export const systemNetPingFunctionalTests = runTest({
    name: "System.Net.Ping.Functional.Tests",
    testDll: "System.Net.Ping.Functional.Tests.dll",
    buildOutputs: src_libraries_System_Net_Ping_tests_FunctionalTests_System_Net_Ping_Functional_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetPrimitivesFunctionalTests = runTest({
    name: "System.Net.Primitives.Functional.Tests",
    testDll: "System.Net.Primitives.Functional.Tests.dll",
    buildOutputs: src_libraries_System_Net_Primitives_tests_FunctionalTests_System_Net_Primitives_Functional_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetRequestsTests = runTest({
    name: "System.Net.Requests.Tests",
    testDll: "System.Net.Requests.Tests.dll",
    buildOutputs: src_libraries_System_Net_Requests_tests_System_Net_Requests_Tests,
});

@@public
export const systemNetSecurityTests = runTest({
    name: "System.Net.Security.Tests",
    testDll: "System.Net.Security.Tests.dll",
    buildOutputs: src_libraries_System_Net_Security_tests_FunctionalTests_System_Net_Security_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetServerSentEventsTests = runTest({
    name: "System.Net.ServerSentEvents.Tests",
    testDll: "System.Net.ServerSentEvents.Tests.dll",
    buildOutputs: src_libraries_System_Net_ServerSentEvents_tests_System_Net_ServerSentEvents_Tests,
});

@@public
export const systemNetSocketsTests = runTest({
    name: "System.Net.Sockets.Tests",
    testDll: "System.Net.Sockets.Tests.dll",
    buildOutputs: src_libraries_System_Net_Sockets_tests_FunctionalTests_System_Net_Sockets_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemNetWebClientTests = runTest({
    name: "System.Net.WebClient.Tests",
    testDll: "System.Net.WebClient.Tests.dll",
    buildOutputs: src_libraries_System_Net_WebClient_tests_System_Net_WebClient_Tests,
});

@@public
export const systemNetWebSocketsTests = runTest({
    name: "System.Net.WebSockets.Tests",
    testDll: "System.Net.WebSockets.Tests.dll",
    buildOutputs: src_libraries_System_Net_WebSockets_tests_System_Net_WebSockets_Tests,
});

@@public
export const systemNetWebSocketsClientTests = runTest({
    name: "System.Net.WebSockets.Client.Tests",
    testDll: "System.Net.WebSockets.Client.Tests.dll",
    buildOutputs: src_libraries_System_Net_WebSockets_Client_tests_System_Net_WebSockets_Client_Tests,
});

@@public
export const systemNumericsTensorsTests = runTest({
    name: "System.Numerics.Tensors.Tests",
    testDll: "System.Numerics.Tensors.Tests.dll",
    buildOutputs: src_libraries_System_Numerics_Tensors_tests_System_Numerics_Tensors_Tests,
});

@@public
export const systemNumericsVectorsTests = runTest({
    name: "System.Numerics.Vectors.Tests",
    testDll: "System.Numerics.Vectors.Tests.dll",
    buildOutputs: src_libraries_System_Numerics_Vectors_tests_System_Numerics_Vectors_Tests,
});

@@public
export const systemObjectModelTests = runTest({
    name: "System.ObjectModel.Tests",
    testDll: "System.ObjectModel.Tests.dll",
    buildOutputs: src_libraries_System_ObjectModel_tests_System_ObjectModel_Tests,
});

@@public
export const systemPrivateUriFunctionalTests = runTest({
    name: "System.Private.Uri.Functional.Tests",
    testDll: "System.Private.Uri.Functional.Tests.dll",
    buildOutputs: src_libraries_System_Private_Uri_tests_FunctionalTests_System_Private_Uri_Functional_Tests,
});

@@public
export const systemPrivateXmlTests = runTest({
    name: "System.Private.Xml.Tests",
    testDll: "System.Private.Xml.Tests.dll",
    buildOutputs: src_libraries_System_Private_Xml_tests_System_Private_Xml_Tests,
});

@@public
export const systemReflectionContextTests = runTest({
    name: "System.Reflection.Context.Tests",
    testDll: "System.Reflection.Context.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_Context_tests_System_Reflection_Context_Tests,
});

@@public
export const systemReflectionDispatchProxyTests = runTest({
    name: "System.Reflection.DispatchProxy.Tests",
    testDll: "System.Reflection.DispatchProxy.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_DispatchProxy_tests_System_Reflection_DispatchProxy_Tests,
});

@@public
export const systemReflectionEmitTests = runTest({
    name: "System.Reflection.Emit.Tests",
    testDll: "System.Reflection.Emit.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_Emit_tests_System_Reflection_Emit_Tests,
});

@@public
export const systemReflectionEmitILGenerationTests = runTest({
    name: "System.Reflection.Emit.ILGeneration.Tests",
    testDll: "System.Reflection.Emit.ILGeneration.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_Emit_ILGeneration_tests_System_Reflection_Emit_ILGeneration_Tests,
});

@@public
export const systemReflectionEmitLightweightTests = runTest({
    name: "System.Reflection.Emit.Lightweight.Tests",
    testDll: "System.Reflection.Emit.Lightweight.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_Emit_Lightweight_tests_System_Reflection_Emit_Lightweight_Tests,
});

@@public
export const systemReflectionMetadataTests = runTest({
    name: "System.Reflection.Metadata.Tests",
    testDll: "System.Reflection.Metadata.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_Metadata_tests_System_Reflection_Metadata_Tests,
});

@@public
export const systemReflectionMetadataLoadContextTests = runTest({
    name: "System.Reflection.MetadataLoadContext.Tests",
    testDll: "System.Reflection.MetadataLoadContext.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_MetadataLoadContext_tests_System_Reflection_MetadataLoadContext_Tests,
});

@@public
export const systemReflectionTypeExtensionsTests = runTest({
    name: "System.Reflection.TypeExtensions.Tests",
    testDll: "System.Reflection.TypeExtensions.Tests.dll",
    buildOutputs: src_libraries_System_Reflection_TypeExtensions_tests_System_Reflection_TypeExtensions_Tests,
});

@@public
export const systemResourcesExtensionsTests = runTest({
    name: "System.Resources.Extensions.Tests",
    testDll: "System.Resources.Extensions.Tests.dll",
    buildOutputs: src_libraries_System_Resources_Extensions_tests_System_Resources_Extensions_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemRuntimeCompilerServicesVisualCTests = runTest({
    name: "System.Runtime.CompilerServices.VisualC.Tests",
    testDll: "System.Runtime.CompilerServices.VisualC.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_CompilerServices_VisualC_tests_System_Runtime_CompilerServices_VisualC_Tests,
});

@@public
export const systemRuntimeInteropServicesTests = runTest({
    name: "System.Runtime.InteropServices.Tests",
    testDll: "System.Runtime.InteropServices.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_InteropServices_tests_System_Runtime_InteropServices_UnitTests_System_Runtime_InteropServices_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemRuntimeIntrinsicsTests = runTest({
    name: "System.Runtime.Intrinsics.Tests",
    testDll: "System.Runtime.Intrinsics.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Intrinsics_tests_System_Runtime_Intrinsics_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemRuntimeNumericsTests = runTest({
    name: "System.Runtime.Numerics.Tests",
    testDll: "System.Runtime.Numerics.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Numerics_tests_System_Runtime_Numerics_Tests,
});

@@public
export const systemRuntimeSerializationFormattersTests = runTest({
    name: "System.Runtime.Serialization.Formatters.Tests",
    testDll: "System.Runtime.Serialization.Formatters.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Serialization_Formatters_tests_System_Runtime_Serialization_Formatters_Tests,
    tfm: "net11.0-linux",
});

@@public
export const systemRuntimeSerializationJsonTests = runTest({
    name: "System.Runtime.Serialization.Json.Tests",
    testDll: "System.Runtime.Serialization.Json.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Serialization_Json_tests_System_Runtime_Serialization_Json_Tests,
});

@@public
export const systemRuntimeSerializationPrimitivesTests = runTest({
    name: "System.Runtime.Serialization.Primitives.Tests",
    testDll: "System.Runtime.Serialization.Primitives.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Serialization_Primitives_tests_System_Runtime_Serialization_Primitives_Tests,
});

@@public
export const systemRuntimeSerializationXmlTests = runTest({
    name: "System.Runtime.Serialization.Xml.Tests",
    testDll: "System.Runtime.Serialization.Xml.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Serialization_Xml_tests_System_Runtime_Serialization_Xml_Tests,
});

// System.Security.AccessControl.Tests — Windows-only (net11.0-windows), skipped on Linux

@@public
export const systemSecurityClaimsTests = runTest({
    name: "System.Security.Claims.Tests",
    testDll: "System.Security.Claims.Tests.dll",
    buildOutputs: src_libraries_System_Security_Claims_tests_System_Security_Claims_Tests,
});

@@public
export const systemSecurityCryptographyTests = runTest({
    name: "System.Security.Cryptography.Tests",
    testDll: "System.Security.Cryptography.Tests.dll",
    buildOutputs: src_libraries_System_Security_Cryptography_tests_System_Security_Cryptography_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemSecurityCryptographyCoseTests = runTest({
    name: "System.Security.Cryptography.Cose.Tests",
    testDll: "System.Security.Cryptography.Cose.Tests.dll",
    buildOutputs: src_libraries_System_Security_Cryptography_Cose_tests_System_Security_Cryptography_Cose_Tests,
});

@@public
export const systemSecurityCryptographyPkcsTests = runTest({
    name: "System.Security.Cryptography.Pkcs.Tests",
    testDll: "System.Security.Cryptography.Pkcs.Tests.dll",
    buildOutputs: src_libraries_System_Security_Cryptography_Pkcs_tests_System_Security_Cryptography_Pkcs_Tests,
});

@@public
export const systemSecurityCryptographyProtectedDataTests = runTest({
    name: "System.Security.Cryptography.ProtectedData.Tests",
    testDll: "System.Security.Cryptography.ProtectedData.Tests.dll",
    buildOutputs: src_libraries_System_Security_Cryptography_ProtectedData_tests_System_Security_Cryptography_ProtectedData_Tests,
});

@@public
export const systemSecurityCryptographyXmlTests = runTest({
    name: "System.Security.Cryptography.Xml.Tests",
    testDll: "System.Security.Cryptography.Xml.Tests.dll",
    buildOutputs: src_libraries_System_Security_Cryptography_Xml_tests_System_Security_Cryptography_Xml_Tests,
});

// System.Security.Principal.Windows.Tests — Windows-only (net11.0-windows), skipped on Linux

@@public
export const systemServiceModelSyndicationTests = runTest({
    name: "System.ServiceModel.Syndication.Tests",
    testDll: "System.ServiceModel.Syndication.Tests.dll",
    buildOutputs: src_libraries_System_ServiceModel_Syndication_tests_System_ServiceModel_Syndication_Tests,
});

@@public
export const systemTextEncodingCodePagesTests = runTest({
    name: "System.Text.Encoding.CodePages.Tests",
    testDll: "System.Text.Encoding.CodePages.Tests.dll",
    buildOutputs: src_libraries_System_Text_Encoding_CodePages_tests_System_Text_Encoding_CodePages_Tests,
});

@@public
export const systemTextEncodingExtensionsTests = runTest({
    name: "System.Text.Encoding.Extensions.Tests",
    testDll: "System.Text.Encoding.Extensions.Tests.dll",
    buildOutputs: src_libraries_System_Text_Encoding_Extensions_tests_System_Text_Encoding_Extensions_Tests,
});

@@public
export const systemTextEncodingsWebTests = runTest({
    name: "System.Text.Encodings.Web.Tests",
    testDll: "System.Text.Encodings.Web.Tests.dll",
    buildOutputs: src_libraries_System_Text_Encodings_Web_tests_System_Text_Encodings_Web_Tests,
});

@@public
export const systemThreadingTests = runTest({
    name: "System.Threading.Tests",
    testDll: "System.Threading.Tests.dll",
    buildOutputs: src_libraries_System_Threading_tests_System_Threading_Tests,
});

@@public
export const systemThreadingAccessControlTests = runTest({
    name: "System.Threading.AccessControl.Tests",
    testDll: "System.Threading.AccessControl.Tests.dll",
    buildOutputs: src_libraries_System_Threading_AccessControl_tests_System_Threading_AccessControl_Tests,
    tfm: "net10.0",
});

@@public
export const systemThreadingChannelsTests = runTest({
    name: "System.Threading.Channels.Tests",
    testDll: "System.Threading.Channels.Tests.dll",
    buildOutputs: src_libraries_System_Threading_Channels_tests_System_Threading_Channels_Tests,
});

@@public
export const systemThreadingOverlappedTests = runTest({
    name: "System.Threading.Overlapped.Tests",
    testDll: "System.Threading.Overlapped.Tests.dll",
    buildOutputs: src_libraries_System_Threading_Overlapped_tests_System_Threading_Overlapped_Tests,
});

@@public
export const systemThreadingRateLimitingTests = runTest({
    name: "System.Threading.RateLimiting.Tests",
    testDll: "System.Threading.RateLimiting.Tests.dll",
    buildOutputs: src_libraries_System_Threading_RateLimiting_tests_System_Threading_RateLimiting_Tests,
});

@@public
export const systemThreadingTasksDataflowTests = runTest({
    name: "System.Threading.Tasks.Dataflow.Tests",
    testDll: "System.Threading.Tasks.Dataflow.Tests.dll",
    buildOutputs: src_libraries_System_Threading_Tasks_Dataflow_tests_System_Threading_Tasks_Dataflow_Tests,
});

@@public
export const systemThreadingTasksParallelTests = runTest({
    name: "System.Threading.Tasks.Parallel.Tests",
    testDll: "System.Threading.Tasks.Parallel.Tests.dll",
    buildOutputs: src_libraries_System_Threading_Tasks_Parallel_tests_System_Threading_Tasks_Parallel_Tests,
});

@@public
export const systemThreadingThreadTests = runTest({
    name: "System.Threading.Thread.Tests",
    testDll: "System.Threading.Thread.Tests.dll",
    buildOutputs: src_libraries_System_Threading_Thread_tests_System_Threading_Thread_Tests,
});

@@public
export const systemThreadingThreadPoolTests = runTest({
    name: "System.Threading.ThreadPool.Tests",
    testDll: "System.Threading.ThreadPool.Tests.dll",
    buildOutputs: src_libraries_System_Threading_ThreadPool_tests_System_Threading_ThreadPool_Tests,
});

@@public
export const systemTransactionsLocalTests = runTest({
    name: "System.Transactions.Local.Tests",
    testDll: "System.Transactions.Local.Tests.dll",
    buildOutputs: src_libraries_System_Transactions_Local_tests_System_Transactions_Local_Tests,
});

@@public
export const systemDataCommonTests = runTest({
    name: "System.Data.Common.Tests",
    testDll: "System.Data.Common.Tests.dll",
    buildOutputs: src_libraries_System_Data_Common_tests_System_Data_Common_Tests,
});

@@public
export const systemXmlLinqMiscTests = runTest({
    name: "System.Xml.Linq.Misc.Tests",
    testDll: "System.Xml.Linq.Misc.Tests.dll",
    buildOutputs: src_libraries_System_Private_Xml_Linq_tests_misc_System_Xml_Linq_Misc_Tests,
});

@@public
export const microsoftBclAsyncInterfacesTests = runTest({
    name: "Microsoft.Bcl.AsyncInterfaces.Tests",
    testDll: "Microsoft.Bcl.AsyncInterfaces.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Bcl_AsyncInterfaces_tests_Microsoft_Bcl_AsyncInterfaces_Tests,
});

@@public
export const microsoftBclCryptographyTests = runTest({
    name: "Microsoft.Bcl.Cryptography.Tests",
    testDll: "Microsoft.Bcl.Cryptography.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Bcl_Cryptography_tests_Microsoft_Bcl_Cryptography_Tests,
});

@@public
export const microsoftBclMemoryTests = runTest({
    name: "Microsoft.Bcl.Memory.Tests",
    testDll: "Microsoft.Bcl.Memory.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Bcl_Memory_tests_Microsoft_Bcl_Memory_Tests,
});

@@public
export const microsoftBclNumericsTests = runTest({
    name: "Microsoft.Bcl.Numerics.Tests",
    testDll: "Microsoft.Bcl.Numerics.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Bcl_Numerics_tests_Microsoft_Bcl_Numerics_Tests,
});

@@public
export const microsoftBclTimeProviderTests = runTest({
    name: "Microsoft.Bcl.TimeProvider.Tests",
    testDll: "Microsoft.Bcl.TimeProvider.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Bcl_TimeProvider_tests_Microsoft_Bcl_TimeProvider_Tests,
});

@@public
export const microsoftCSharpTests = runTest({
    name: "Microsoft.CSharp.Tests",
    testDll: "Microsoft.CSharp.Tests.dll",
    buildOutputs: src_libraries_Microsoft_CSharp_tests_Microsoft_CSharp_Tests,
});

@@public
export const microsoftExtensionsCachingMemoryTests = runTest({
    name: "Microsoft.Extensions.Caching.Memory.Tests",
    testDll: "Microsoft.Extensions.Caching.Memory.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Caching_Memory_tests_Microsoft_Extensions_Caching_Memory_Tests,
});

@@public
export const microsoftExtensionsConfigurationTests = runTest({
    name: "Microsoft.Extensions.Configuration.Tests",
    testDll: "Microsoft.Extensions.Configuration.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_tests_Microsoft_Extensions_Configuration_Tests,
});

@@public
export const microsoftExtensionsConfigurationBinderTests = runTest({
    name: "Microsoft.Extensions.Configuration.Binder.Tests",
    testDll: "Microsoft.Extensions.Configuration.Binder.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_Binder_tests_UnitTests_Microsoft_Extensions_Configuration_Binder_Tests,
});

@@public
export const microsoftExtensionsConfigurationCommandLineTests = runTest({
    name: "Microsoft.Extensions.Configuration.CommandLine.Tests",
    testDll: "Microsoft.Extensions.Configuration.CommandLine.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_CommandLine_tests_Microsoft_Extensions_Configuration_CommandLine_Tests,
});

@@public
export const microsoftExtensionsConfigurationEnvironmentVariablesTests = runTest({
    name: "Microsoft.Extensions.Configuration.EnvironmentVariables.Tests",
    testDll: "Microsoft.Extensions.Configuration.EnvironmentVariables.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_EnvironmentVariables_tests_Microsoft_Extensions_Configuration_EnvironmentVariables_Tests,
});

@@public
export const microsoftExtensionsConfigurationFileExtensionsTests = runTest({
    name: "Microsoft.Extensions.Configuration.FileExtensions.Tests",
    testDll: "Microsoft.Extensions.Configuration.FileExtensions.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_FileExtensions_tests_Microsoft_Extensions_Configuration_FileExtensions_Tests,
});

@@public
export const microsoftExtensionsConfigurationIniTests = runTest({
    name: "Microsoft.Extensions.Configuration.Ini.Tests",
    testDll: "Microsoft.Extensions.Configuration.Ini.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_Ini_tests_Microsoft_Extensions_Configuration_Ini_Tests,
});

@@public
export const microsoftExtensionsConfigurationJsonTests = runTest({
    name: "Microsoft.Extensions.Configuration.Json.Tests",
    testDll: "Microsoft.Extensions.Configuration.Json.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_Json_tests_Microsoft_Extensions_Configuration_Json_Tests,
});

@@public
export const microsoftExtensionsConfigurationUserSecretsTests = runTest({
    name: "Microsoft.Extensions.Configuration.UserSecrets.Tests",
    testDll: "Microsoft.Extensions.Configuration.UserSecrets.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_UserSecrets_tests_Microsoft_Extensions_Configuration_UserSecrets_Tests,
});

@@public
export const microsoftExtensionsConfigurationXmlTests = runTest({
    name: "Microsoft.Extensions.Configuration.Xml.Tests",
    testDll: "Microsoft.Extensions.Configuration.Xml.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Configuration_Xml_tests_Microsoft_Extensions_Configuration_Xml_Tests,
});

@@public
export const microsoftExtensionsDependencyInjectionTests = runTest({
    name: "Microsoft.Extensions.DependencyInjection.Tests",
    testDll: "Microsoft.Extensions.DependencyInjection.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_DependencyInjection_tests_DI_Tests_Microsoft_Extensions_DependencyInjection_Tests,
});

@@public
export const microsoftExtensionsDependencyModelTests = runTest({
    name: "Microsoft.Extensions.DependencyModel.Tests",
    testDll: "Microsoft.Extensions.DependencyModel.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_DependencyModel_tests_Microsoft_Extensions_DependencyModel_Tests,
});

@@public
export const microsoftExtensionsDiagnosticsAbstractionsTests = runTest({
    name: "Microsoft.Extensions.Diagnostics.Abstractions.Tests",
    testDll: "Microsoft.Extensions.Diagnostics.Abstractions.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Diagnostics_Abstractions_tests_Microsoft_Extensions_Diagnostics_Abstractions_Tests,
});

@@public
export const microsoftExtensionsDiagnosticsTests = runTest({
    name: "Microsoft.Extensions.Diagnostics.Tests",
    testDll: "Microsoft.Extensions.Diagnostics.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Diagnostics_tests_Microsoft_Extensions_Diagnostics_Tests,
});

@@public
export const microsoftExtensionsFileProvidersCompositeTests = runTest({
    name: "Microsoft.Extensions.FileProviders.Composite.Tests",
    testDll: "Microsoft.Extensions.FileProviders.Composite.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_FileProviders_Composite_tests_Microsoft_Extensions_FileProviders_Composite_Tests,
});

@@public
export const microsoftExtensionsFileProvidersPhysicalTests = runTest({
    name: "Microsoft.Extensions.FileProviders.Physical.Tests",
    testDll: "Microsoft.Extensions.FileProviders.Physical.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_FileProviders_Physical_tests_Microsoft_Extensions_FileProviders_Physical_Tests,
});

@@public
export const microsoftExtensionsFileSystemGlobbingTests = runTest({
    name: "Microsoft.Extensions.FileSystemGlobbing.Tests",
    testDll: "Microsoft.Extensions.FileSystemGlobbing.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_FileSystemGlobbing_tests_Microsoft_Extensions_FileSystemGlobbing_Tests,
});

@@public
export const microsoftExtensionsHostingAbstractionsTests = runTest({
    name: "Microsoft.Extensions.Hosting.Abstractions.Tests",
    testDll: "Microsoft.Extensions.Hosting.Abstractions.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Hosting_Abstractions_tests_Microsoft_Extensions_Hosting_Abstractions_Tests,
});

@@public
export const microsoftExtensionsHostingUnitTests = runTest({
    name: "Microsoft.Extensions.Hosting.Unit.Tests",
    testDll: "Microsoft.Extensions.Hosting.Unit.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Hosting_tests_UnitTests_Microsoft_Extensions_Hosting_Unit_Tests,
});

@@public
export const microsoftExtensionsHostingSystemdTests = runTest({
    name: "Microsoft.Extensions.Hosting.Systemd.Tests",
    testDll: "Microsoft.Extensions.Hosting.Systemd.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Hosting_Systemd_tests_Microsoft_Extensions_Hosting_Systemd_Tests,
});

@@public
export const microsoftExtensionsHttpTests = runTest({
    name: "Microsoft.Extensions.Http.Tests",
    testDll: "Microsoft.Extensions.Http.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Http_tests_Microsoft_Extensions_Http_Tests_Microsoft_Extensions_Http_Tests,
});

@@public
export const microsoftExtensionsLoggingTests = runTest({
    name: "Microsoft.Extensions.Logging.Tests",
    testDll: "Microsoft.Extensions.Logging.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Logging_tests_Common_Microsoft_Extensions_Logging_Tests,
});

@@public
export const microsoftExtensionsLoggingGeneratorsRoslyn48Tests = runTest({
    name: "Microsoft.Extensions.Logging.Generators.Roslyn4.8.Tests",
    testDll: "Microsoft.Extensions.Logging.Generators.Roslyn4.8.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Logging_Abstractions_tests_Microsoft_Extensions_Logging_Generators_Tests_Microsoft_Extensions_Logging_Generators_Roslyn4_8_Tests,
});

@@public
export const microsoftExtensionsLoggingConsoleTests = runTest({
    name: "Microsoft.Extensions.Logging.Console.Tests",
    testDll: "Microsoft.Extensions.Logging.Console.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Logging_Console_tests_Microsoft_Extensions_Logging_Console_Tests_Microsoft_Extensions_Logging_Console_Tests,
});

@@public
export const microsoftExtensionsLoggingEventSourceTests = runTest({
    name: "Microsoft.Extensions.Logging.EventSource.Tests",
    testDll: "Microsoft.Extensions.Logging.EventSource.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Logging_EventSource_tests_Microsoft_Extensions_Logging_EventSource_Tests,
});

@@public
export const microsoftExtensionsOptionsTests = runTest({
    name: "Microsoft.Extensions.Options.Tests",
    testDll: "Microsoft.Extensions.Options.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Options_tests_Microsoft_Extensions_Options_Tests_Microsoft_Extensions_Options_Tests,
});

@@public
export const microsoftExtensionsPrimitivesTests = runTest({
    name: "Microsoft.Extensions.Primitives.Tests",
    testDll: "Microsoft.Extensions.Primitives.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Extensions_Primitives_tests_Microsoft_Extensions_Primitives_Tests,
});

@@public
export const microsoftWin32PrimitivesTests = runTest({
    name: "Microsoft.Win32.Primitives.Tests",
    testDll: "Microsoft.Win32.Primitives.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Win32_Primitives_tests_Microsoft_Win32_Primitives_Tests,
    tfm: "net11.0-unix",
});

// Microsoft.Win32.Registry.Tests — Windows-only (net11.0-windows), skipped on Linux

@@public
export const microsoftWin32RegistryAccessControlTests = runTest({
    name: "Microsoft.Win32.Registry.AccessControl.Tests",
    testDll: "Microsoft.Win32.Registry.AccessControl.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Win32_Registry_AccessControl_tests_Microsoft_Win32_Registry_AccessControl_Tests,
    tfm: "net10.0",
});

@@public
export const microsoftWin32SystemEventsTests = runTest({
    name: "Microsoft.Win32.SystemEvents.Tests",
    testDll: "Microsoft.Win32.SystemEvents.Tests.dll",
    buildOutputs: src_libraries_Microsoft_Win32_SystemEvents_tests_Microsoft_Win32_SystemEvents_Tests,
    tfm: "net10.0",
});

@@public
export const systemCodeDomTests = runTest({
    name: "System.CodeDom.Tests",
    testDll: "System.CodeDom.Tests.dll",
    buildOutputs: src_libraries_System_CodeDom_tests_System_CodeDom_Tests,
});

@@public
export const systemConfigurationConfigurationManagerTests = runTest({
    name: "System.Configuration.ConfigurationManager.Tests",
    testDll: "System.Configuration.ConfigurationManager.Tests.dll",
    buildOutputs: src_libraries_System_Configuration_ConfigurationManager_tests_System_Configuration_ConfigurationManager_Tests,
});

@@public
export const systemDataOleDbTests = runTest({
    name: "System.Data.OleDb.Tests",
    testDll: "System.Data.OleDb.Tests.dll",
    buildOutputs: src_libraries_System_Data_OleDb_tests_System_Data_OleDb_Tests,
    tfm: "net10.0",
});

@@public
export const systemDiagnosticsEventLogTests = runTest({
    name: "System.Diagnostics.EventLog.Tests",
    testDll: "System.Diagnostics.EventLog.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_EventLog_tests_System_Diagnostics_EventLog_Tests,
    tfm: "net10.0",
});

@@public
export const systemDiagnosticsProcessTests = runTest({
    name: "System.Diagnostics.Process.Tests",
    testDll: "System.Diagnostics.Process.Tests.dll",
    buildOutputs: src_libraries_System_Diagnostics_Process_tests_System_Diagnostics_Process_Tests,
    tfm: "net11.0-unix",
});

@@public
export const systemDirectoryServicesTests = runTest({
    name: "System.DirectoryServices.Tests",
    testDll: "System.DirectoryServices.Tests.dll",
    buildOutputs: src_libraries_System_DirectoryServices_tests_System_DirectoryServices_Tests,
    tfm: "net10.0",
});

@@public
export const systemDirectoryServicesAccountManagementTests = runTest({
    name: "System.DirectoryServices.AccountManagement.Tests",
    testDll: "System.DirectoryServices.AccountManagement.Tests.dll",
    buildOutputs: src_libraries_System_DirectoryServices_AccountManagement_tests_System_DirectoryServices_AccountManagement_Tests,
    tfm: "net10.0",
});

@@public
export const systemDirectoryServicesProtocolsTests = runTest({
    name: "System.DirectoryServices.Protocols.Tests",
    testDll: "System.DirectoryServices.Protocols.Tests.dll",
    buildOutputs: src_libraries_System_DirectoryServices_Protocols_tests_System_DirectoryServices_Protocols_Tests,
    tfm: "net11.0-linux",
});

@@public
export const systemManagementTests = runTest({
    name: "System.Management.Tests",
    testDll: "System.Management.Tests.dll",
    buildOutputs: src_libraries_System_Management_tests_System_Management_Tests,
    tfm: "net10.0",
});

@@public
export const systemNetQuicFunctionalTests = runTest({
    name: "System.Net.Quic.Functional.Tests",
    testDll: "System.Net.Quic.Functional.Tests.dll",
    buildOutputs: src_libraries_System_Net_Quic_tests_FunctionalTests_System_Net_Quic_Functional_Tests,
    tfm: "net11.0-linux",
});

@@public
export const systemRuntimeCachingTests = runTest({
    name: "System.Runtime.Caching.Tests",
    testDll: "System.Runtime.Caching.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Caching_tests_System_Runtime_Caching_Tests,
});

@@public
export const systemRuntimeSerializationSchemaTests = runTest({
    name: "System.Runtime.Serialization.Schema.Tests",
    testDll: "System.Runtime.Serialization.Schema.Tests.dll",
    buildOutputs: src_libraries_System_Runtime_Serialization_Schema_tests_System_Runtime_Serialization_Schema_Tests,
});

@@public
export const systemSecurityCryptographyOpenSslTests = runTest({
    name: "System.Security.Cryptography.OpenSsl.Tests",
    testDll: "System.Security.Cryptography.OpenSsl.Tests.dll",
    buildOutputs: src_libraries_System_Security_Cryptography_OpenSsl_tests_System_Security_Cryptography_OpenSsl_Tests,
    tfm: "net11.0-unix",
});

// System.Security.Permissions.Tests — Windows-only (net11.0-windows), skipped on Linux

@@public
export const systemServiceProcessServiceControllerTests = runTest({
    name: "System.ServiceProcess.ServiceController.Tests",
    testDll: "System.ServiceProcess.ServiceController.Tests.dll",
    buildOutputs: src_libraries_System_ServiceProcess_ServiceController_tests_System_ServiceProcess_ServiceController_Tests,
    tfm: "net10.0",
});

// System.Speech.Tests — Windows-only (net11.0-windows), skipped on Linux

// System.Windows.Extensions.Tests — Windows-only (net11.0-windows), skipped on Linux

