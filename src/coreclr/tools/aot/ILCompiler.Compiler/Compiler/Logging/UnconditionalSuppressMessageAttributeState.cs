// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using ILCompiler.Dataflow;

using ILLink.Shared;

#nullable enable

namespace ILCompiler.Logging
{
    public class UnconditionalSuppressMessageAttributeState
    {
        internal const string ScopeProperty = "Scope";
        internal const string TargetProperty = "Target";
        internal const string MessageIdProperty = "MessageId";

        internal const string UnconditionalSuppressMessageAttributeNamespace = "System.Diagnostics.CodeAnalysis";
        internal const string UnconditionalSuppressMessageAttributeName = "UnconditionalSuppressMessageAttribute";

        public class Suppression
        {
            public SuppressMessageInfo SuppressMessageInfo { get; }
            public bool Used { get; set; }
            public MessageOrigin Origin { get; }
            public TypeSystemEntity Provider { get; }

            public Suppression(SuppressMessageInfo suppressMessageInfo, MessageOrigin origin, TypeSystemEntity provider)
            {
                SuppressMessageInfo = suppressMessageInfo;
                Origin = origin;
                Provider = provider;
            }
        }

        private readonly CompilerGeneratedState? _compilerGeneratedState;
        private readonly Logger _logger;
        private readonly Dictionary<TypeSystemEntity, Dictionary<int, Suppression>> _suppressions = new();
        private readonly HashSet<TypeSystemEntity> _initializedProviders = new();
        private readonly HashSet<EcmaAssembly> _initializedAssemblies = new();

        public UnconditionalSuppressMessageAttributeState(CompilerGeneratedState? compilerGeneratedState, Logger logger)
        {
            _compilerGeneratedState = compilerGeneratedState;
            _logger = logger;
        }

        public bool IsSuppressed(int id, MessageOrigin warningOrigin)
        {
            // Check for suppressions on both the suppression context as well as the original member
            // (if they're different). This is to correctly handle compiler generated code
            // which needs to use suppressions from both the compiler generated scope
            // as well as the original user defined method.

            TypeSystemEntity? provider = warningOrigin.MemberDefinition;
            if (provider == null)
                return false;

            if (IsSuppressed(id, provider))
                return true;

            if (_compilerGeneratedState != null)
            {
                while (_compilerGeneratedState.TryGetOwningMethodForCompilerGeneratedMember(provider, out MethodDesc? owningMethod))
                {
                    Debug.Assert(owningMethod != provider);
                    if (IsSuppressed(id, owningMethod))
                        return true;
                    provider = owningMethod;
                }
            }

            return false;
        }

        public void AddExternalSuppression(SuppressMessageInfo info, TypeSystemEntity provider, MessageOrigin origin)
        {
            lock (_suppressions)
                AddSuppression(new Suppression(info, origin, provider));
        }

        public void GatherSuppressions(TypeSystemEntity provider)
        {
            lock (_suppressions)
                TryGetSuppressionsForProvider(provider, out _);
        }

        public IEnumerable<Suppression> GetUnusedSuppressions()
        {
            lock (_suppressions)
            {
                return _suppressions.Values
                    .SelectMany(static suppressions => suppressions.Values)
                    .Where(suppression => !suppression.Used && IsProviderInitialized(suppression.Provider))
                    .ToArray();
            }

            bool IsProviderInitialized(TypeSystemEntity provider)
            {
                if (provider is ModuleDesc)
                    return GetModuleFromProvider(provider) is EcmaAssembly assembly && _initializedAssemblies.Contains(assembly);

                return _initializedProviders.Contains(provider);
            }
        }

        public IEnumerable<TypeSystemEntity> GetSuppressionProviders()
        {
            lock (_suppressions)
                return _suppressions.Keys.ToArray();
        }

        private bool IsSuppressed(int id, TypeSystemEntity? warningOrigin)
        {
            if (warningOrigin == null)
                return false;

            lock (_suppressions)
            {
                TypeSystemEntity? warningOriginMember = warningOrigin;
                while (warningOriginMember != null)
                {
                    if (IsSuppressedOnElement(id, warningOriginMember))
                        return true;

                    if (warningOriginMember is MethodDesc method)
                    {
                        if (method.GetPropertyForAccessor() is { } property)
                        {
                            Debug.Assert(property.OwningType == method.OwningType);
                            warningOriginMember = property;
                            continue;
                        }
                        else if (method.GetEventForAccessor() is { } @event)
                        {
                            Debug.Assert(@event.OwningType == method.OwningType);
                            warningOriginMember = @event;
                            continue;
                        }
                    }

                    warningOriginMember = warningOriginMember.GetOwningType();
                }

                ModuleDesc? module = GetModuleFromProvider(warningOrigin);
                return module is EcmaAssembly ecmaAssembly && IsSuppressedOnElement(id, ecmaAssembly);
            }
        }

        private void AddSuppression(Suppression suppression)
        {
            if (!_suppressions.TryGetValue(suppression.Provider, out Dictionary<int, Suppression>? suppressions))
            {
                suppressions = new Dictionary<int, Suppression>();
                _suppressions.Add(suppression.Provider, suppressions);
            }
            else if (suppressions.TryGetValue(suppression.SuppressMessageInfo.Id, out Suppression? existing))
            {
                suppression.Used = existing.Used;
                _logger.LogMessage($"Element '{suppression.Provider.GetDisplayName()}' has more than one unconditional suppression. Note that only the last one is used.");
            }

            suppressions[suppression.SuppressMessageInfo.Id] = suppression;
        }

        private bool IsSuppressedOnElement(int id, TypeSystemEntity provider)
        {
            if (TryGetSuppressionsForProvider(provider, out Dictionary<int, Suppression>? suppressions)
                && suppressions is not null
                && suppressions.TryGetValue(id, out Suppression? suppression))
            {
                suppression.Used = true;
                return true;
            }

            return false;
        }

        private bool TryGetSuppressionsForProvider(TypeSystemEntity provider, out Dictionary<int, Suppression>? suppressions)
        {
            ModuleDesc? module = GetModuleFromProvider(provider);
            if (module is EcmaAssembly ecmaAssembly && _initializedAssemblies.Add(ecmaAssembly))
            {
                List<(DiagnosticId, string?[])> generatedWarnings = new();
                foreach (Suppression suppression in DecodeAssemblyAndModuleSuppressions(ecmaAssembly, generatedWarnings))
                    AddSuppression(suppression);

                foreach ((DiagnosticId id, string?[] args) in generatedWarnings)
                {
                    _logger.LogWarning(ecmaAssembly, id, args);
                }
            }

            if (provider is not ModuleDesc && _initializedProviders.Add(provider))
            {
                foreach (Suppression suppression in DecodeSuppressions(provider))
                    AddSuppression(suppression);
            }

            return _suppressions.TryGetValue(provider, out suppressions);
        }

        private static bool TryDecodeSuppressMessageAttributeData(CustomAttributeValue<TypeDesc> attribute, out SuppressMessageInfo info)
        {
            info = default;

            // We need at least the Category and Id to decode the warning to suppress.
            // The only UnconditionalSuppressMessageAttribute constructor requires those two parameters.
            if (attribute.FixedArguments.Length < 2)
            {
                return false;
            }

            // Ignore the category parameter because it does not identify the warning
            // and category information can be obtained from warnings themselves.
            // We only support warnings with code pattern IL####.
            if (!(attribute.FixedArguments[1].Value is string warningId) ||
                warningId.Length < 6 ||
                !warningId.StartsWith("IL", StringComparison.Ordinal) ||
                !int.TryParse(warningId.AsSpan(2, 4), out info.Id))
            {
                return false;
            }

            if (warningId.Length > 6 && warningId[6] != ':')
                return false;

            foreach (var p in attribute.NamedArguments)
            {
                switch (p.Name)
                {
                    case ScopeProperty when p.Value is string scope:
                        info.Scope = scope;
                        break;
                    case TargetProperty when p.Value is string target:
                        info.Target = target;
                        break;
                    case MessageIdProperty when p.Value is string messageId:
                        info.MessageId = messageId;
                        break;
                }
            }

            return true;
        }

        public static ModuleDesc? GetModuleFromProvider(TypeSystemEntity provider)
        {
            switch (provider)
            {
                case ModuleDesc module:
                    return module;
                case MetadataType type:
                    return type.Module;
                default:
                    return (provider.GetOwningType() as MetadataType)?.Module;
            }
        }

        private static IEnumerable<Suppression> DecodeSuppressions(TypeSystemEntity provider)
        {
            Debug.Assert(provider is not ModuleDesc);

            foreach (CustomAttributeValue<TypeDesc> ca in GetDecodedCustomAttributes(provider, UnconditionalSuppressMessageAttributeNamespace, UnconditionalSuppressMessageAttributeName))
            {
                if (!TryDecodeSuppressMessageAttributeData(ca, out var info))
                    continue;

                yield return new Suppression(info, new MessageOrigin(provider), provider);
            }
        }

        private static List<Suppression> DecodeAssemblyAndModuleSuppressions(EcmaAssembly ecmaAssembly, List<(DiagnosticId, string?[])> warnings)
        {
            List<Suppression> suppressions = new();
            DecodeGlobalSuppressions(
                ecmaAssembly,
                ecmaAssembly.GetDecodedCustomAttributes(UnconditionalSuppressMessageAttributeNamespace, UnconditionalSuppressMessageAttributeName),
                suppressions,
                warnings);

            DecodeGlobalSuppressions(
                ecmaAssembly,
                ecmaAssembly.GetDecodedCustomAttributesForModule(UnconditionalSuppressMessageAttributeNamespace, UnconditionalSuppressMessageAttributeName),
                suppressions,
                warnings);

            return suppressions;
        }

        private static void DecodeGlobalSuppressions(
            EcmaAssembly module,
            IEnumerable<CustomAttributeValue<TypeDesc>> attributes,
            List<Suppression> suppressions,
            List<(DiagnosticId, string?[])> warnings)
        {
            foreach (CustomAttributeValue<TypeDesc> instance in attributes)
            {
                if (!TryDecodeSuppressMessageAttributeData(instance, out SuppressMessageInfo info))
                    continue;

                var scope = info.Scope?.ToLowerInvariant();
                if (info.Target == null && (scope == "module" || scope == null))
                {
                    suppressions.Add(new Suppression(info, new MessageOrigin(module), module));
                    continue;
                }

                switch (scope)
                {
                    case "module":
                        suppressions.Add(new Suppression(info, new MessageOrigin(module), module));
                        break;

                    case "type":
                    case "member":
                        if (info.Target == null)
                            break;

                        foreach (var result in DocumentationSignatureParser.GetMembersForDocumentationSignature(info.Target, module))
                        {
                            suppressions.Add(new Suppression(info, new MessageOrigin(result), result));
                        }

                        break;
                    default:
                        warnings.Add((DiagnosticId.InvalidScopeInUnconditionalSuppressMessage, new string?[] { info.Scope ?? "", module.GetName().Name, info.Target ?? "" }));
                        break;
                }
            }
        }

        private static IEnumerable<CustomAttributeValue<TypeDesc>> GetDecodedCustomAttributes(TypeSystemEntity entity, string attributeNamespace, string attributeName)
        {
            switch (entity)
            {
                case MethodDesc method:
                    if (method.GetTypicalMethodDefinition() is not EcmaMethod ecmaMethod)
                        return Enumerable.Empty<CustomAttributeValue<TypeDesc>>();
                    return ecmaMethod.GetDecodedCustomAttributes(attributeNamespace, attributeName);
                case MetadataType type:
                    if (type.GetTypeDefinition() is not EcmaType ecmaType)
                        return Enumerable.Empty<CustomAttributeValue<TypeDesc>>();
                    return ecmaType.GetDecodedCustomAttributes(attributeNamespace, attributeName);
                case FieldDesc field:
                    if (field.GetTypicalFieldDefinition() is not EcmaField ecmaField)
                        return Enumerable.Empty<CustomAttributeValue<TypeDesc>>();
                    return ecmaField.GetDecodedCustomAttributes(attributeNamespace, attributeName);
                case PropertyPseudoDesc property:
                    return property.GetDecodedCustomAttributes(attributeNamespace, attributeName);
                case EventPseudoDesc @event:
                    return @event.GetDecodedCustomAttributes(attributeNamespace, attributeName);
                default:
                    Debug.Fail("Trying to operate with unsupported TypeSystemEntity " + entity.GetType().ToString());
                    return Enumerable.Empty<CustomAttributeValue<TypeDesc>>();
            }
        }
    }
}
