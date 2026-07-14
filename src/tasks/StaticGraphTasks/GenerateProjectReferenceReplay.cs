// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Runtime.Tasks;

public sealed class GenerateProjectReferenceReplay : Task
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
        SortedDictionary<string, SortedDictionary<string, ProjectReferenceSet>> projectReferenceSetsByParent = new(StringComparer.OrdinalIgnoreCase);
        bool hasAuthoritativeProjectReferenceSets = false;

        foreach (string line in File.ReadLines(rawSelectionFile))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 4)
            {
                Log.LogWarning($"Ignoring malformed project reference capture record: {line}");
                continue;
            }

            string parentProjectPath = parts[0];
            string parentTargetFramework = parts[1];
            string projectPath = parts[2];
            string setTargetFramework = parts[3];

            if (!ValidateRelativeProjectPath(parentProjectPath, allowEmpty: false, line) ||
                !ValidateRelativeProjectPath(projectPath, allowEmpty: true, line))
            {
                continue;
            }

            if (!projectReferenceSetsByParent.TryGetValue(parentProjectPath, out SortedDictionary<string, ProjectReferenceSet>? projectReferenceSetsByTargetFramework))
            {
                projectReferenceSetsByTargetFramework = new SortedDictionary<string, ProjectReferenceSet>(StringComparer.OrdinalIgnoreCase);
                projectReferenceSetsByParent.Add(parentProjectPath, projectReferenceSetsByTargetFramework);
            }

            if (!projectReferenceSetsByTargetFramework.TryGetValue(parentTargetFramework, out ProjectReferenceSet? projectReferenceSet))
            {
                projectReferenceSet = new ProjectReferenceSet();
                projectReferenceSetsByTargetFramework.Add(parentTargetFramework, projectReferenceSet);
            }

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                if (!ValidateConditionValue(parentTargetFramework, "parent target framework", line, allowEmpty: true))
                {
                    continue;
                }

                projectReferenceSet.IsAuthoritative = true;
                hasAuthoritativeProjectReferenceSets = true;
                continue;
            }

            projectReferenceSet.ProjectReferences.Add(projectPath);

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

            if (!ValidateConditionValue(property[1], "selected target framework", line))
            {
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
        if (string.IsNullOrEmpty(outputDirectory))
        {
            outputDirectory = Directory.GetCurrentDirectory();
        }

        Directory.CreateDirectory(outputDirectory);

        using XmlWriter writer = XmlWriter.Create(outputFile, new XmlWriterSettings { Indent = true });
        writer.WriteStartElement("Project");
        WriteCurrentProjectPathProperty(writer);

        if (selectedFrameworksByProject.Count > 0)
        {
            writer.WriteComment(" Restrict outer builds to the captured target frameworks. ");
            writer.WriteStartElement("Choose");
            writer.WriteStartElement("When");
            writer.WriteAttributeString("Condition", "'$(TargetFramework)' == ''");

            foreach ((string projectPath, SortedSet<string> frameworks) in selectedFrameworksByProject)
            {
                writer.WriteStartElement("PropertyGroup");
                writer.WriteAttributeString("Condition", $"'$(_ProjectReferenceReplayProject)' == '{projectPath}'");
                writer.WriteElementString("TargetFrameworks", string.Join(';', frameworks));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        string edgesOutputFile = $"{outputFile}.edges.targets";
        using XmlWriter edgesWriter = XmlWriter.Create(edgesOutputFile, new XmlWriterSettings { Indent = true });
        edgesWriter.WriteStartElement("Project");
        WriteCurrentProjectPathProperty(edgesWriter);

        int allowListParentCount = 0;
        if (hasAuthoritativeProjectReferenceSets)
        {
            edgesWriter.WriteComment(" Select the captured allow-list for this exact project instance. ");
            edgesWriter.WriteStartElement("Choose");

            foreach ((string parentProjectPath, SortedDictionary<string, ProjectReferenceSet> projectReferenceSetsByTargetFramework) in projectReferenceSetsByParent)
            {
                bool wroteParent = false;
                foreach ((string targetFramework, ProjectReferenceSet projectReferenceSet) in projectReferenceSetsByTargetFramework)
                {
                    if (!projectReferenceSet.IsAuthoritative)
                    {
                        continue;
                    }

                    if (!wroteParent)
                    {
                        allowListParentCount++;
                        wroteParent = true;
                    }

                    edgesWriter.WriteStartElement("When");
                    edgesWriter.WriteAttributeString("Condition", $"'$(_ProjectReferenceReplayProject)' == '{parentProjectPath}' and '$(TargetFramework)' == '{targetFramework}'");
                    WriteProjectReferenceAllowListSelection(edgesWriter, projectReferenceSet.ProjectReferences);
                    edgesWriter.WriteEndElement();
                }
            }

            edgesWriter.WriteEndElement();
        }

        edgesWriter.WriteComment(" Apply the selected allow-list once, including authoritative empty sets. ");
        WriteProjectReferenceAllowListApplication(edgesWriter);
        edgesWriter.WriteEndElement();

        Log.LogMessage(MessageImportance.High, $"Wrote {selectedFrameworksByProject.Count} project target framework selections and {allowListParentCount} project reference allow-lists to '{outputFile}' and '{edgesOutputFile}'.");
        return !Log.HasLoggedErrors;
    }

    private bool ValidateRelativeProjectPath(string path, bool allowEmpty, string record)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (allowEmpty)
            {
                return true;
            }

            Log.LogError($"Project reference capture record has an empty parent project path: {record}");
            return false;
        }

        if (Path.IsPathRooted(path) ||
            path.Contains('\\') ||
            path.Contains('\'') ||
            string.Equals(path, "..", StringComparison.Ordinal) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal))
        {
            Log.LogError($"Project reference capture path '{path}' must be a normalized path beneath RepoRoot.");
            return false;
        }

        return true;
    }

    private bool ValidateConditionValue(string value, string description, string record, bool allowEmpty = false)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Contains('\''))
        {
            Log.LogError($"Project reference capture record has an invalid {description} '{value}': {record}");
            return false;
        }

        return true;
    }

    private static void WriteCurrentProjectPathProperty(XmlWriter writer)
    {
        writer.WriteStartElement("PropertyGroup");
        writer.WriteElementString("_ProjectReferenceReplayProject", "$([MSBuild]::MakeRelative('$(RepoRoot)', '$(MSBuildProjectFullPath)').Replace('\\', '/'))");
        writer.WriteEndElement();
    }

    private static void WriteProjectReferenceAllowListSelection(XmlWriter writer, SortedSet<string>? selectedProjectReferences)
    {
        writer.WriteStartElement("PropertyGroup");
        writer.WriteElementString("_ApplyProjectReferenceReplayAllowList", "true");
        writer.WriteEndElement();

        if (selectedProjectReferences is null || selectedProjectReferences.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("ItemGroup");
        foreach (string selectedProjectReference in selectedProjectReferences)
        {
            writer.WriteStartElement("_ReplayProjectReference");
            writer.WriteAttributeString("Include", $"$(RepoRoot){selectedProjectReference}");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteProjectReferenceAllowListApplication(XmlWriter writer)
    {
        writer.WriteStartElement("ItemGroup");
        writer.WriteAttributeString("Condition", "'$(_ApplyProjectReferenceReplayAllowList)' == 'true'");

        writer.WriteStartElement("_ProjectReferenceToRemove");
        writer.WriteAttributeString("Include", "@(ProjectReference->'%(FullPath)')");
        writer.WriteEndElement();

        writer.WriteStartElement("_ProjectReferenceToRemove");
        writer.WriteAttributeString("Remove", "@(_ReplayProjectReference)");
        writer.WriteEndElement();

        writer.WriteStartElement("ProjectReference");
        writer.WriteAttributeString("Remove", "@(_ProjectReferenceToRemove)");
        writer.WriteEndElement();

        writer.WriteStartElement("_ReplayProjectReference");
        writer.WriteAttributeString("Remove", "@(_ReplayProjectReference)");
        writer.WriteEndElement();

        writer.WriteStartElement("_ProjectReferenceToRemove");
        writer.WriteAttributeString("Remove", "@(_ProjectReferenceToRemove)");
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private sealed class ProjectReferenceSet
    {
        public bool IsAuthoritative { get; set; }

        public SortedSet<string> ProjectReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
