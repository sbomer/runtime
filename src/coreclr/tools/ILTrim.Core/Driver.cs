// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Xml;

using ILCompiler;
using ILCompiler.Dataflow;
using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;

using ILLink.Shared;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace Mono.Linker
{
    public partial class Driver
    {
        public int Run(ILogWriter logWriter = null)
        {
            int setupStatus = SetupContext(logWriter);
            if (setupStatus > 0)
                return 0;
            if (setupStatus < 0)
                return 1;

            var tsContext = new ILTrimTypeSystemContext();
            tsContext.ReferenceFilePaths = context.Resolver.ToReferenceFilePaths();

            EcmaModule corelib = tsContext.GetModuleForSimpleName("System.Private.CoreLib");
            tsContext.SetSystemModule(corelib);

            foreach (DependencyNodeCore<NodeFactory> input in context.Inputs)
            {
                if (input is AssemblyRootNode assemblyRoot)
                    assemblyRoot.ApplyConfiguration(context, tsContext);
            }

            var baseILProvider = new ILTrimILProvider();

            var suppressedCategories = new List<string> { MessageSubCategory.AotAnalysis };
            int firstSingleFileWarning = (int)DiagnosticId.AvoidAssemblyLocationInSingleFile;
            int singleFileWarningCount = (int)DiagnosticId.RequiresDynamicCode - firstSingleFileWarning;
            IEnumerable<int> suppressedWarnings = context.NoWarn.Concat(
                Enumerable.Range(firstSingleFileWarning, singleFileWarningCount));
            if (context.NoTrimWarn)
                suppressedCategories.Add(MessageSubCategory.TrimAnalysis);

            Logger logger = new Logger(
                context.LogWriter,
                baseILProvider,
                isVerbose: context.LogMessages,
                suppressedWarnings: suppressedWarnings,
                singleWarn: context.GeneralSingleWarn,
                singleWarnEnabledModules: context.SingleWarn.Where(kv => kv.Value).Select(kv => kv.Key),
                singleWarnDisabledModules: context.SingleWarn.Where(kv => !kv.Value).Select(kv => kv.Key),
                suppressedCategories: suppressedCategories,
                treatWarningsAsErrors: context.GeneralWarnAsError,
                warningsAsErrors: context.WarnAsError,
                disableGeneratedCodeHeuristics: context.DisableGeneratedCodeHeuristics)
            {
                MaximumWarningVersion = (int)context.WarnVersion,
            };
            context.AnalysisLogger = logger;

            foreach (string linkAttributesFilePath in context.LinkAttributesFiles)
            {
                LinkAttributesSuppressionsReader.Process(
                    logger,
                    tsContext,
                    File.OpenRead(linkAttributesFilePath),
                    linkAttributesFilePath,
                    context.FeatureSettings);
            }

            BodyAndFieldSubstitutions substitutions = default;
            foreach (string substitutionFilePath in context.SubstitutionFiles)
            {
                using FileStream stream = File.OpenRead(substitutionFilePath);
                substitutions.AppendFrom(BodySubstitutionsParser.GetSubstitutions(
                    logger,
                    tsContext,
                    XmlReader.Create(stream),
                    substitutionFilePath,
                    context.FeatureSettings));
            }

            var substitutionProvider = new SubstitutionProvider(
                logger,
                context.FeatureSettings,
                substitutions,
                context.IgnoreSubstitutions,
                module => context.Optimizations.IsEnabled(
                    CodeOptimizations.SubstituteFeatureGuards,
                    module.Assembly.GetName().Name));
            var ilProvider = new SubstitutedILProvider(baseILProvider, substitutionProvider);

            var factory = new NodeFactory(context, logger, ilProvider, tsContext);

            DependencyTrackingLevel trackingLevel = context.DependenciesFileName is not null
                ? DependencyTrackingLevel.All
                : DependencyTrackingLevel.None;
            DependencyAnalyzerBase<NodeFactory> analyzer = trackingLevel.CreateDependencyGraph(factory);

            analyzer.ComputeDependencyRoutine += ComputeDependencyNodeDependencies;

            foreach (var input in context.Inputs)
                analyzer.AddRoot(input, "Command line root");

            foreach (string linkAttributesFilePath in context.LinkAttributesFiles)
                analyzer.AddRoot(new LinkAttributesNode(linkAttributesFilePath), "Link attributes file");

            foreach ((string assemblyName, string assemblyPath) in context.Resolver.ToExplicitReferenceFilePaths())
            {
                if (context.Actions.TryGetValue(assemblyName, out AssemblyAction explicitAction))
                {
                    if (explicitAction != AssemblyAction.Copy)
                        continue;
                }
                else if (context.DefaultAction != AssemblyAction.Copy && context.TrimAction != AssemblyAction.Copy)
                {
                    continue;
                }

                EcmaModule module;
                try
                {
                    module = tsContext.ResolveAssembly(
                        AssemblyNameInfo.Parse(assemblyName),
                        throwIfNotFound: false) as EcmaModule;
                }
                catch (Exception ex) when (context.IgnoreUnresolved && ex is TypeSystemException or BadImageFormatException)
                {
                    continue;
                }

                if (module is null)
                {
                    if (!context.IgnoreUnresolved)
                        context.LogError(null, DiagnosticId.ReferenceAssemblyCouldNotBeLoaded, assemblyPath);
                    continue;
                }

                if (context.CalculateAssemblyAction(module) == AssemblyAction.Copy)
                    analyzer.AddRoot(new AssemblyRootNode(assemblyName, AssemblyRootMode.AllMembers), "Copy assembly action");
            }

            analyzer.AddRoot(factory.VirtualMethodUse(
                (EcmaMethod)tsContext.GetWellKnownType(WellKnownType.Object).GetMethod("Finalize"u8, null)),
                "Finalizer");

            analyzer.ComputeMarkedNodes();
            GatherMarkedSuppressions();
            logger.ReportRedundantSuppressions();

            var writers = ModuleWriter.CreateWriters(factory, analyzer.MarkedNodeList);
            if (!Directory.Exists(context.OutputDirectory))
                Directory.CreateDirectory(context.OutputDirectory);
            RunForEach(writers, writer =>
            {
                string outputPath = Path.Combine(context.OutputDirectory, writer.FileName);
                using var outputStream = File.Create(outputPath);
                writer.Save(outputStream);
            });

            if (context.DependenciesFileName is not null)
            {
                using var logStream = File.OpenWrite(context.DependenciesFileName);
                DgmlWriter.WriteDependencyGraphToStream<NodeFactory>(logStream, analyzer, factory);
            }

            return logger.HasLoggedErrors || context.HasLoggedErrors ? 1 : 0;

            void GatherMarkedSuppressions()
            {
                var markedNodes = new HashSet<DependencyNodeCore<NodeFactory>>(analyzer.MarkedNodeList);
                foreach (string assemblyName in context.Resolver.ToReferenceFilePaths().Keys)
                {
                    if (tsContext.ResolveAssembly(AssemblyNameInfo.Parse(assemblyName), throwIfNotFound: false) is not EcmaModule module)
                        continue;

                    MetadataReader reader = module.MetadataReader;
                    foreach (CustomAttributeHandle attributeHandle in reader.CustomAttributes)
                    {
                        CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                        if (module.TryGetMethod(attribute.Constructor)?.OwningType is not MetadataType attributeType
                            || attributeType.Namespace != "System.Diagnostics.CodeAnalysis"u8
                            || attributeType.Name != "UnconditionalSuppressMessageAttribute"u8)
                        {
                            continue;
                        }

                        TypeSystemEntity provider = GetAttributeProvider(module, attribute.Parent);
                        if (provider is not null && IsProviderMarked(provider))
                            logger.GatherSuppressions(provider);
                    }
                }

                foreach (TypeSystemEntity provider in logger.GetSuppressionProviders())
                {
                    if (IsProviderMarked(provider))
                        logger.GatherSuppressions(provider);
                }

                bool IsProviderMarked(TypeSystemEntity provider)
                {
                    switch (provider)
                    {
                        case EcmaAssembly assembly:
                            return markedNodes.Contains(factory.AssemblyDefinition(assembly))
                                || markedNodes.Contains(factory.ModuleDefinition(assembly));
                        case EcmaType type:
                            return markedNodes.Contains(factory.TypeDefinition(type.Module, type.Handle));
                        case EcmaMethod method:
                            return markedNodes.Contains(factory.MethodDefinition((EcmaModule)method.Module, method.Handle));
                        case EcmaField field:
                            return markedNodes.Contains(factory.FieldDefinition((EcmaModule)field.Module, field.Handle));
                        case PropertyPseudoDesc property:
                            return IsPropertyMarked(property);
                        case EventPseudoDesc @event:
                            return IsEventMarked(@event);
                        default:
                            return false;
                    }
                }

                bool IsPropertyMarked(PropertyPseudoDesc property)
                {
                    var module = (EcmaModule)property.OwningType.Module;
                    PropertyAccessors accessors = module.MetadataReader.GetPropertyDefinition(property.Handle).GetAccessors();
                    return (!accessors.Getter.IsNil && markedNodes.Contains(factory.MethodDefinition(module, accessors.Getter)))
                        || (!accessors.Setter.IsNil && markedNodes.Contains(factory.MethodDefinition(module, accessors.Setter)));
                }

                bool IsEventMarked(EventPseudoDesc @event)
                {
                    var module = (EcmaModule)@event.OwningType.Module;
                    EventAccessors accessors = module.MetadataReader.GetEventDefinition(@event.Handle).GetAccessors();
                    return (!accessors.Adder.IsNil && markedNodes.Contains(factory.MethodDefinition(module, accessors.Adder)))
                        || (!accessors.Remover.IsNil && markedNodes.Contains(factory.MethodDefinition(module, accessors.Remover)))
                        || (!accessors.Raiser.IsNil && markedNodes.Contains(factory.MethodDefinition(module, accessors.Raiser)));
                }
            }

            static TypeSystemEntity GetAttributeProvider(EcmaModule module, EntityHandle parent) =>
                parent.Kind switch
                {
                    HandleKind.AssemblyDefinition or HandleKind.ModuleDefinition => module,
                    HandleKind.TypeDefinition => module.GetType((TypeDefinitionHandle)parent),
                    HandleKind.MethodDefinition => module.GetMethod((MethodDefinitionHandle)parent),
                    HandleKind.FieldDefinition => module.GetField((FieldDefinitionHandle)parent),
                    HandleKind.PropertyDefinition => CreateProperty(module, (PropertyDefinitionHandle)parent),
                    HandleKind.EventDefinition => CreateEvent(module, (EventDefinitionHandle)parent),
                    _ => null,
                };

            static PropertyPseudoDesc CreateProperty(EcmaModule module, PropertyDefinitionHandle handle)
            {
                TypeDefinitionHandle declaringType = module.MetadataReader.GetPropertyDefinition(handle).GetDeclaringType();
                return new PropertyPseudoDesc((EcmaType)module.GetType(declaringType), handle);
            }

            static EventPseudoDesc CreateEvent(EcmaModule module, EventDefinitionHandle handle)
            {
                TypeDefinitionHandle declaringType = module.MetadataReader.GetEventDefinition(handle).GetDeclaringType();
                return new EventPseudoDesc((EcmaType)module.GetType(declaringType), handle);
            }

            void ComputeDependencyNodeDependencies(List<DependencyNodeCore<NodeFactory>> nodesWithPendingDependencyCalculation) =>
                RunForEach(
                    nodesWithPendingDependencyCalculation.Cast<INodeWithDeferredDependencies>(),
                    node => node.ComputeDependencies(factory));

            void RunForEach<T>(IEnumerable<T> inputs, Action<T> action)
            {
#if !SINGLE_THREADED
                if (context.MaxDegreeOfParallelism == 1)
#endif
                {
                    foreach (var input in inputs)
                        action(input);
                }
#if !SINGLE_THREADED
                else
                {
                    Parallel.ForEach(
                        inputs,
                        new() { MaxDegreeOfParallelism = context.EffectiveDegreeOfParallelism },
                        action);
                }
#endif
            }
        }

        protected virtual LinkContext GetDefaultContext(IReadOnlyList<DependencyNodeCore<NodeFactory>> inputs, ILogWriter logger)
        {
            return new LinkContext(inputs, logger ?? new TextLogWriter(Console.Out), "output")
            {
                TrimAction = AssemblyAction.Link,
                DefaultAction = AssemblyAction.Link,
                KeepComInterfaces = true,
            };
        }

        // TODO-ILTRIM: ILTrim only supports DGML format; XML is emitted as DGML
        protected virtual void AddXmlDependencyRecorder(LinkContext context, string fileName)
        {
            context.DependenciesFileName = fileName;
        }

        protected virtual void AddDgmlDependencyRecorder(LinkContext context, string fileName)
        {
            context.DependenciesFileName = fileName;
        }

        static List<DependencyNodeCore<NodeFactory>> GetStandardPipeline()
        {
            return new List<DependencyNodeCore<NodeFactory>>();
        }

        public void Dispose() { }
    }
}
