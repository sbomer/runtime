// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILCompiler;
using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;
using ILCompiler.Logging;

using ILLink.Shared;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace Mono.Linker
{
    public partial class LinkContext
    {
        private readonly Dictionary<EcmaModule, AssemblyAction> _calculatedActions = new();
        private Logger _analysisLogger;
        private bool _hasLoggedErrors;

        internal Logger AnalysisLogger
        {
            set => _analysisLogger = value;
        }

        public IReadOnlyList<DependencyNodeCore<NodeFactory>> Inputs { get; }

        public ILogWriter LogWriter => _logger;

        public int? MaxDegreeOfParallelism { get; set; }

        public int EffectiveDegreeOfParallelism => MaxDegreeOfParallelism ?? Environment.ProcessorCount;

        public string DependenciesFileName { get; set; }

        internal bool HasLoggedErrors => _hasLoggedErrors;

        public void LogError(MessageOrigin? origin, DiagnosticId id, params string[] args)
        {
            MessageContainer? error = MessageContainer.CreateErrorMessage(origin, id, args);
            if (error.HasValue)
            {
                _hasLoggedErrors = true;
                _logger.WriteError(error.Value);
            }
        }

        public LinkContext(IReadOnlyList<DependencyNodeCore<NodeFactory>> inputs, ILogWriter logger, string outputDirectory)
        {
            Inputs = inputs;
            _logger = logger;
            _actions = new Dictionary<string, AssemblyAction>();
            _parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            _cachedWarningMessageContainers = new List<MessageContainer>();
            OutputDirectory = outputDirectory;
            FeatureSettings = new Dictionary<string, bool>(StringComparer.Ordinal);
            LinkAttributesFiles = new List<string>();
            SubstitutionFiles = new List<string>();

            PInvokes = new List<PInvokeInfo>();
            NoWarn = new HashSet<int>();
            GeneralWarnAsError = false;
            WarnAsError = new Dictionary<int, bool>();
            WarnVersion = WarnVersion.Latest;
            GeneralSingleWarn = false;
            SingleWarn = new Dictionary<string, bool>();
            AssembliesWithGeneratedSingleWarning = new HashSet<string>();

            const CodeOptimizations defaultOptimizations =
                CodeOptimizations.BeforeFieldInit |
                CodeOptimizations.OverrideRemoval |
                CodeOptimizations.UnusedInterfaces |
                CodeOptimizations.UnusedTypeChecks |
                CodeOptimizations.IPConstantPropagation |
                CodeOptimizations.UnreachableBodies |
                CodeOptimizations.RemoveDescriptors |
                CodeOptimizations.RemoveLinkAttributes |
                CodeOptimizations.RemoveSubstitutions |
                CodeOptimizations.RemoveDynamicDependencyAttribute |
                CodeOptimizations.OptimizeTypeHierarchyAnnotations |
                CodeOptimizations.SubstituteFeatureGuards;

            DisableEventSourceSpecialHandling = true;

            Optimizations = new CodeOptimizationsSettings(defaultOptimizations);
        }

        public ResolverShim Resolver { get; } = new ResolverShim();

        internal List<string> LinkAttributesFiles { get; }

        internal List<string> SubstitutionFiles { get; }

        public AssemblyAction CalculateAssemblyAction(EcmaModule module)
        {
            lock (_calculatedActions)
            {
                if (_calculatedActions.TryGetValue(module, out AssemblyAction calculatedAction))
                    return calculatedAction;

                AssemblyAction action = CalculateAssemblyActionCore(module);
                _calculatedActions.Add(module, action);
                return action;
            }
        }

        private AssemblyAction CalculateAssemblyActionCore(EcmaModule module)
        {
            string assemblyName = module.Assembly.GetName().Name;
            if (_actions.TryGetValue(assemblyName, out AssemblyAction action))
                return action;

            if (!module.PEReader.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.ILOnly))
                return AssemblyAction.Copy;

            if (IsTrimmable(module))
                return TrimAction;

            return DefaultAction;
        }

        private bool IsTrimmable(EcmaModule module)
        {
            bool isTrimmable = false;
            foreach (CustomAttributeValue<TypeDesc> attribute in ((EcmaAssembly)module.Assembly).GetDecodedCustomAttributes(
                "System.Reflection",
                "AssemblyMetadataAttribute"))
            {
                if (attribute.FixedArguments.Length != 2
                    || !attribute.FixedArguments[0].Type.IsString
                    || attribute.FixedArguments[0].Value is not string key
                    || !key.Equals("IsTrimmable", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (attribute.FixedArguments[1].Type.IsString
                    && attribute.FixedArguments[1].Value is string value
                    && value.Equals("True", StringComparison.OrdinalIgnoreCase))
                {
                    isTrimmable = true;
                    continue;
                }

                Debug.Assert(_analysisLogger is not null);
                string assemblyName = module.Assembly.GetName().Name;
                MessageOrigin origin = Resolver.TryGetReferenceFilePath(assemblyName, out string assemblyPath)
                    ? new MessageOrigin(assemblyPath)
                    : new MessageOrigin(module);
                _analysisLogger?.LogWarning(
                    origin,
                    DiagnosticId.InvalidIsTrimmableValue,
                    attribute.FixedArguments[1].Value?.ToString() ?? "",
                    assemblyName);
            }

            return isTrimmable;
        }

        public class ResolverShim
        {
            private Dictionary<string, string> _referencePathsFromDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private Dictionary<string, string> _referencePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public void AddSearchDirectory(string directory)
            {
                foreach (var file in Directory.GetFiles(directory, "*.exe"))
                {
                    _referencePathsFromDirectories[Path.GetFileNameWithoutExtension(file)] = file;
                }

                foreach (var file in Directory.GetFiles(directory, "*.dll"))
                {
                    _referencePathsFromDirectories[Path.GetFileNameWithoutExtension(file)] = file;
                }
            }

            public void AddReferenceAssembly(string path)
            {
                _referencePaths[Path.GetFileNameWithoutExtension(path)] = path;
            }

            public IReadOnlyDictionary<string, string> ToReferenceFilePaths()
            {
                Dictionary<string, string> result = new Dictionary<string, string>(_referencePathsFromDirectories);

                foreach ((string assemblyName, string fileName) in _referencePaths)
                    result[assemblyName] = fileName;

                return result;
            }

            internal IReadOnlyDictionary<string, string> ToExplicitReferenceFilePaths() => _referencePaths;

            internal bool TryGetReferenceFilePath(string assemblyName, out string path)
            {
                if (_referencePaths.TryGetValue(assemblyName, out path))
                    return true;

                return _referencePathsFromDirectories.TryGetValue(assemblyName, out path);
            }
        }
    }
}
