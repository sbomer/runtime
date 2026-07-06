// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Runtime.Tasks;

public sealed class GenerateSelectedTargetFrameworks : Task
{
    [Required]
    public string? RawSelectionFile { get; set; }

    [Required]
    public string? OutputFile { get; set; }

    public override bool Execute()
    {
        string rawSelectionFile = RawSelectionFile!;
        string outputFile = OutputFile!;

        if (!File.Exists(rawSelectionFile))
        {
            Log.LogError($"Raw selection file '{rawSelectionFile}' does not exist.");
            return false;
        }

        SortedDictionary<string, SortedSet<string>> selectedFrameworksByProject = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in File.ReadLines(rawSelectionFile))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 8)
            {
                Log.LogWarning($"Ignoring malformed selected target framework line: {line}");
                continue;
            }

            string projectPath = parts[1];
            string setTargetFramework = parts[3];

            if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(setTargetFramework))
            {
                continue;
            }

            string[] property = setTargetFramework.Split('=', count: 2);
            if (property.Length != 2 || !string.Equals(property[0], "TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                Log.LogWarning($"Ignoring unsupported SetTargetFramework metadata '{setTargetFramework}' for '{projectPath}'.");
                continue;
            }

            if (!selectedFrameworksByProject.TryGetValue(projectPath, out SortedSet<string>? frameworks))
            {
                frameworks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedFrameworksByProject.Add(projectPath, frameworks);
            }

            frameworks.Add(property[1]);
        }

        string? outputDirectory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using XmlWriter writer = XmlWriter.Create(outputFile, new XmlWriterSettings { Indent = true });
        writer.WriteStartElement("Project");

        foreach ((string projectPath, SortedSet<string> frameworks) in selectedFrameworksByProject)
        {
            writer.WriteStartElement("PropertyGroup");
            writer.WriteAttributeString("Condition", $"'$(MSBuildProjectFullPath)' == '{projectPath}'");
            writer.WriteElementString("TargetFrameworks", string.Join(';', frameworks));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        Log.LogMessage(MessageImportance.High, $"Wrote {selectedFrameworksByProject.Count} selected target framework entries to '{outputFile}'.");
        return !Log.HasLoggedErrors;
    }
}
