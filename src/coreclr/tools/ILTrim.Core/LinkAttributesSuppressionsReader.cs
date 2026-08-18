// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.XPath;

using ILCompiler.Logging;

using Internal.TypeSystem;

namespace ILCompiler
{
    internal sealed class LinkAttributesSuppressionsReader : ProcessLinkerXmlBase
    {
        private readonly Logger _logger;

        private LinkAttributesSuppressionsReader(
            Logger logger,
            TypeSystemContext context,
            Stream documentStream,
            string xmlDocumentLocation,
            IReadOnlyDictionary<string, bool> featureSwitchValues)
            : base(logger, context, documentStream, xmlDocumentLocation, featureSwitchValues)
        {
            _logger = logger;
        }

        public static void Process(
            Logger logger,
            TypeSystemContext context,
            Stream documentStream,
            string xmlDocumentLocation,
            IReadOnlyDictionary<string, bool> featureSwitchValues)
        {
            var reader = new LinkAttributesSuppressionsReader(
                logger,
                context,
                documentStream,
                xmlDocumentLocation,
                featureSwitchValues);
            reader.ProcessXml(ignoreResource: false);
        }

        protected override AllowedAssemblies AllowedAssemblySelector => AllowedAssemblies.AnyAssembly;

        protected override void ProcessAssembly(ModuleDesc assembly, XPathNavigator nav, bool warnOnUnresolvedTypes)
        {
            ProcessAttributes(assembly, nav);
            ProcessTypes(assembly, nav, warnOnUnresolvedTypes);
        }

        protected override void ProcessType(TypeDesc type, XPathNavigator nav)
        {
            ProcessAttributes(type, nav);
            ProcessTypeChildren(type, nav);

            foreach (XPathNavigator nestedTypeNav in nav.SelectChildren("type", XmlNamespace))
            {
                string name = GetAttribute(nestedTypeNav, "name");
                MetadataType? nestedType = (type as MetadataType)?.GetNestedType(System.Text.Encoding.UTF8.GetBytes(name));
                if (nestedType is not null && ShouldProcessElement(nestedTypeNav))
                    ProcessType(nestedType, nestedTypeNav);
            }
        }

        protected override void ProcessField(TypeDesc type, FieldDesc field, XPathNavigator nav) =>
            ProcessAttributes(field, nav);

        protected override void ProcessMethod(TypeDesc type, MethodDesc method, XPathNavigator nav, object? customData) =>
            ProcessAttributes(method, nav);

        protected override void ProcessEvent(TypeDesc type, EventPseudoDesc @event, XPathNavigator nav, object? customData) =>
            ProcessAttributes(@event, nav);

        protected override void ProcessProperty(TypeDesc type, PropertyPseudoDesc property, XPathNavigator nav, object? customData, bool fromSignature) =>
            ProcessAttributes(property, nav);

        private void ProcessAttributes(TypeSystemEntity provider, XPathNavigator nav)
        {
            foreach (XPathNavigator attributeNav in nav.SelectChildren("attribute", XmlNamespace))
            {
                if (!ShouldProcessElement(attributeNav)
                    || GetFullName(attributeNav) != UnconditionalSuppressMessageAttributeState.UnconditionalSuppressMessageAttributeNamespace
                        + "."
                        + UnconditionalSuppressMessageAttributeState.UnconditionalSuppressMessageAttributeName)
                {
                    continue;
                }

                string[] arguments = attributeNav.SelectChildren("argument", XmlNamespace)
                    .Cast<XPathNavigator>()
                    .Select(static argument => argument.Value)
                    .ToArray();
                if (arguments.Length < 2 || !TryParseWarningId(arguments[1], out int id))
                    continue;

                var info = new SuppressMessageInfo { Id = id };
                foreach (XPathNavigator propertyNav in attributeNav.SelectChildren("property", XmlNamespace))
                {
                    switch (GetAttribute(propertyNav, "name"))
                    {
                        case UnconditionalSuppressMessageAttributeState.ScopeProperty:
                            info.Scope = propertyNav.Value;
                            break;
                        case UnconditionalSuppressMessageAttributeState.TargetProperty:
                            info.Target = propertyNav.Value;
                            break;
                        case UnconditionalSuppressMessageAttributeState.MessageIdProperty:
                            info.MessageId = propertyNav.Value;
                            break;
                    }
                }

                if (provider is ModuleDesc module && info.Target is not null)
                {
                    foreach (TypeSystemEntity target in DocumentationSignatureParser.GetMembersForDocumentationSignature(info.Target, module))
                        _logger.AddExternalSuppression(info, target, GetExternalOrigin(attributeNav, target));
                }
                else
                {
                    _logger.AddExternalSuppression(info, provider, GetExternalOrigin(attributeNav, provider));
                }
            }
        }

        private MessageOrigin GetExternalOrigin(XPathNavigator nav, TypeSystemEntity provider)
        {
            MessageOrigin origin = GetMessageOriginForPosition(nav);
            return new MessageOrigin(
                origin.FileName!,
                origin.SourceLine ?? 0,
                origin.SourceColumn ?? 0,
                UnconditionalSuppressMessageAttributeState.GetModuleFromProvider(provider));
        }

        private static bool TryParseWarningId(string warningId, out int id)
        {
            id = 0;
            return warningId.Length >= 6
                && warningId.StartsWith("IL", StringComparison.Ordinal)
                && int.TryParse(warningId.AsSpan(2, 4), out id)
                && (warningId.Length == 6 || warningId[6] == ':');
        }
    }
}
