// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

using Mono.Cecil;

namespace Mono.Linker.Tests.TestCasesRunner
{
    public class TestDependencyRecorder
    {
        public record struct Dependency
        {
            public string Source;
            public string Target;
            public bool Marked;
            public string DependencyKind;
        }

        public List<Dependency> Dependencies = new List<Dependency>();

        public void Load(string dependencyFilePath, string inputAssemblyPath)
        {
            if (!System.IO.File.Exists(dependencyFilePath))
                return;

            using ModuleDefinition module = ModuleDefinition.ReadModule(inputAssemblyPath);
            Dictionary<string, string> nodes = ReadNodes(dependencyFilePath, module);
            XNamespace dgml = "http://schemas.microsoft.com/vs/2009/dgml";
            XElement links = XDocument.Load(dependencyFilePath).Root?.Element(dgml + "Links");
            if (links is null)
                return;

            var primarySources = new Dictionary<string, string>();
            foreach (XElement link in links.Elements(dgml + "Link"))
            {
                string targetId = (string)link.Attribute("Target");
                string sourceId = (string)link.Attribute("Source");
                if ((string)link.Attribute("Reason") == "Primary" && targetId is not null && sourceId is not null)
                    primarySources[targetId] = sourceId;
            }

            foreach (XElement link in links.Elements(dgml + "Link"))
            {
                string reason = (string)link.Attribute("Reason");
                if (reason is "Primary" or "Secondary")
                    continue;

                string sourceId = (string)link.Attribute("Source");
                string targetId = (string)link.Attribute("Target");
                if (sourceId is null || targetId is null)
                    continue;

                if (primarySources.TryGetValue(sourceId, out string primarySourceId))
                    sourceId = primarySourceId;

                if (nodes.TryGetValue(sourceId, out string source) &&
                    nodes.TryGetValue(targetId, out string target))
                {
                    Dependencies.Add(new Dependency
                    {
                        Source = source,
                        Target = target,
                        Marked = true,
                        DependencyKind = reason,
                    });
                }
            }
        }

        private static Dictionary<string, string> ReadNodes(string dependencyFilePath, ModuleDefinition module)
        {
            XNamespace dgml = "http://schemas.microsoft.com/vs/2009/dgml";
            XElement nodes = XDocument.Load(dependencyFilePath).Root?.Element(dgml + "Nodes");
            if (nodes is null)
                return new Dictionary<string, string>();

            Dictionary<string, TypeDefinition> types = module.GetTypes()
                .ToDictionary(type => type.FullName.Replace('/', '+'), StringComparer.Ordinal);
            var result = new Dictionary<string, string>();

            foreach (XElement node in nodes.Elements(dgml + "Node"))
            {
                string label = (string)node.Attribute("Label");
                result[(string)node.Attribute("Id")] = GetProviderName(label, module, types);
            }

            return result;
        }

        private static string GetProviderName(
            string label,
            ModuleDefinition module,
            Dictionary<string, TypeDefinition> types)
        {
            int tokenStart = label.LastIndexOf(" (", StringComparison.Ordinal);
            if (tokenStart >= 0 && label.EndsWith(')'))
            {
                int moduleEnd = label.LastIndexOf(':');
                string tokenText = label.Substring(label.Length - 9, 8);
                if (moduleEnd > tokenStart &&
                    label.AsSpan(tokenStart + 2, moduleEnd - tokenStart - 2).SequenceEqual(module.Name.AsSpan()) &&
                    int.TryParse(tokenText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int token) &&
                    module.LookupToken(token) is IMemberDefinition member)
                {
                    return member.FullName;
                }
            }

            const string ConstructedSuffix = " constructed";
            if (label.EndsWith(ConstructedSuffix, StringComparison.Ordinal))
            {
                string typeName = label.Substring(0, label.Length - ConstructedSuffix.Length);
                if (typeName.StartsWith('['))
                    typeName = typeName.Substring(typeName.IndexOf(']') + 1);

                if (types.TryGetValue(typeName, out TypeDefinition type))
                    return type.FullName;
            }

            return label;
        }
    }
}
