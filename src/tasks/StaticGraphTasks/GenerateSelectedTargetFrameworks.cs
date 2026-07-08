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
        SortedDictionary<string, SortedSet<string>> selectedProjectReferencesByParent = new(StringComparer.OrdinalIgnoreCase);
        SortedSet<string> allowListParentProjects = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in File.ReadLines(rawSelectionFile))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 8)
            {
                Log.LogWarning($"Ignoring malformed selected target framework line: {line}");
                continue;
            }

            string parentProjectPath = parts[0];
            string projectPath = parts[1];
            string setTargetFramework = parts[3];

            if (string.IsNullOrWhiteSpace(parentProjectPath))
            {
                continue;
            }

            if (!selectedProjectReferencesByParent.TryGetValue(parentProjectPath, out SortedSet<string>? selectedProjectReferences))
            {
                selectedProjectReferences = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedProjectReferencesByParent.Add(parentProjectPath, selectedProjectReferences);
            }

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                allowListParentProjects.Add(parentProjectPath);
                continue;
            }

            selectedProjectReferences.Add(projectPath);

            if (string.IsNullOrWhiteSpace(setTargetFramework))
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

        foreach (string parentProjectPath in allowListParentProjects)
        {
            selectedProjectReferencesByParent.TryGetValue(parentProjectPath, out SortedSet<string>? selectedProjectReferences);

            writer.WriteStartElement("ItemGroup");
            writer.WriteAttributeString("Condition", $"'$(MSBuildProjectFullPath)' == '{parentProjectPath}'");

            if (selectedProjectReferences is not null)
            {
                foreach (string selectedProjectReference in selectedProjectReferences)
                {
                    writer.WriteStartElement("_StaticGraphSelectedProjectReference");
                    writer.WriteAttributeString("Include", selectedProjectReference);
                    writer.WriteEndElement();
                }
            }

            writer.WriteStartElement("_StaticGraphProjectReferenceToRemove");
            writer.WriteAttributeString("Include", "@(ProjectReference->'%(FullPath)')");
            writer.WriteEndElement();

            writer.WriteStartElement("_StaticGraphProjectReferenceToRemove");
            writer.WriteAttributeString("Remove", "@(_StaticGraphSelectedProjectReference)");
            writer.WriteEndElement();

            writer.WriteStartElement("ProjectReference");
            writer.WriteAttributeString("Remove", "@(_StaticGraphProjectReferenceToRemove)");
            writer.WriteEndElement();

            writer.WriteStartElement("_StaticGraphSelectedProjectReference");
            writer.WriteAttributeString("Remove", "@(_StaticGraphSelectedProjectReference)");
            writer.WriteEndElement();

            writer.WriteStartElement("_StaticGraphProjectReferenceToRemove");
            writer.WriteAttributeString("Remove", "@(_StaticGraphProjectReferenceToRemove)");
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        foreach ((string projectPath, SortedSet<string> frameworks) in selectedFrameworksByProject)
        {
            writer.WriteStartElement("PropertyGroup");
            writer.WriteAttributeString("Condition", $"'$(MSBuildProjectFullPath)' == '{projectPath}' and '$(TargetFramework)' == ''");
            writer.WriteElementString("TargetFrameworks", string.Join(';', frameworks));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        Log.LogMessage(MessageImportance.High, $"Wrote {selectedFrameworksByProject.Count} selected target framework entries and {allowListParentProjects.Count} project reference allow-lists to '{outputFile}'.");
        return !Log.HasLoggedErrors;
    }
}
