// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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

        foreach (string line in File.ReadLines(rawSelectionFile))
        {
            string[] parts = line.Split('|');
            if (parts.Length is not 4 and not 6)
            {
                Log.LogWarning($"Ignoring malformed project reference capture record: {line}");
                continue;
            }

            string parentProjectPath = parts[0];
            string parentTargetFramework = parts[1];
            string projectPath = parts[2];
            string setTargetFramework = parts[3];
            bool isDynamicallyAdded = false;
            SortedDictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);

            if (parts.Length == 6)
            {
                if (!string.IsNullOrEmpty(parts[4]) &&
                    !bool.TryParse(parts[4], out isDynamicallyAdded))
                {
                    Log.LogWarning($"Ignoring project reference capture record with invalid dynamically-added marker: {line}");
                    continue;
                }

                if (!string.IsNullOrEmpty(parts[5]))
                {
                    try
                    {
                        byte[] serializedMetadata = Convert.FromBase64String(parts[5]);
                        metadata = JsonSerializer.Deserialize<SortedDictionary<string, string>>(
                            Encoding.UTF8.GetString(serializedMetadata))!;
                    }
                    catch (Exception exception) when (exception is FormatException or JsonException)
                    {
                        Log.LogWarning($"Ignoring project reference capture record with invalid metadata: {line}");
                        continue;
                    }
                }
            }

            if (!ValidateRelativeProjectPath(parentProjectPath, allowEmpty: false, line) ||
                !ValidateRelativeProjectPath(projectPath, allowEmpty: true, line) ||
                !ValidateConditionValue(parentTargetFramework, "parent target framework", line, allowEmpty: true))
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
                projectReferenceSet.IsAuthoritative = true;
                continue;
            }

            CapturedProjectReference capturedProjectReference = new(setTargetFramework, isDynamicallyAdded, metadata);
            if (!projectReferenceSet.ProjectReferences.TryAdd(projectPath, capturedProjectReference))
            {
                CapturedProjectReference existingProjectReference = projectReferenceSet.ProjectReferences[projectPath];
                if (!existingProjectReference.HasSameMetadata(capturedProjectReference))
                {
                    Log.LogError($"Project reference capture has conflicting metadata for '{parentProjectPath}' -> '{projectPath}'.");
                }
            }

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

        string replayOutputDirectory = $"{outputFile}.d";
        if (Directory.Exists(replayOutputDirectory))
        {
            Directory.Delete(replayOutputDirectory, recursive: true);
        }
        Directory.CreateDirectory(replayOutputDirectory);

        XmlWriterSettings xmlWriterSettings = new() { Indent = true, OmitXmlDeclaration = true };
        using XmlWriter writer = XmlWriter.Create(outputFile, xmlWriterSettings);
        writer.WriteStartElement("Project");
        WriteCurrentProjectPathProperty(writer);
        writer.WriteStartElement("Import");
        writer.WriteAttributeString("Project", "$(MSBuildThisFileFullPath).d/$(_ProjectReferenceReplayProject).targets");
        writer.WriteAttributeString("Condition", "Exists('$(MSBuildThisFileFullPath).d/$(_ProjectReferenceReplayProject).targets')");
        writer.WriteEndElement();
        writer.WriteEndElement();

        File.Delete($"{outputFile}.edges.targets");
        string oldEdgesOutputDirectory = $"{outputFile}.edges.targets.d";
        if (Directory.Exists(oldEdgesOutputDirectory))
        {
            Directory.Delete(oldEdgesOutputDirectory, recursive: true);
        }

        SortedSet<string> replayProjects = new(selectedFrameworksByProject.Keys, StringComparer.OrdinalIgnoreCase);
        replayProjects.UnionWith(projectReferenceSetsByParent.Keys);
        SortedSet<string> allowListParents = new(StringComparer.OrdinalIgnoreCase);
        int replayFileCount = 0;
        foreach (string projectPath in replayProjects)
        {
            selectedFrameworksByProject.TryGetValue(projectPath, out SortedSet<string>? frameworks);
            projectReferenceSetsByParent.TryGetValue(projectPath, out SortedDictionary<string, ProjectReferenceSet>? projectReferenceSetsByTargetFramework);

            bool hasProjectReferenceUpdates = false;
            bool hasAuthoritativeProjectReferenceSet = false;
            if (projectReferenceSetsByTargetFramework is not null)
            {
                foreach (ProjectReferenceSet projectReferenceSet in projectReferenceSetsByTargetFramework.Values)
                {
                    hasAuthoritativeProjectReferenceSet |= projectReferenceSet.IsAuthoritative;
                    hasProjectReferenceUpdates |= projectReferenceSet.ProjectReferences.Count > 0;
                }
            }

            if (frameworks is null && !hasAuthoritativeProjectReferenceSet && !hasProjectReferenceUpdates)
            {
                continue;
            }

            string replayFile = Path.Combine(
                replayOutputDirectory,
                $"{projectPath}.targets".Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(replayFile)!);

            using XmlWriter projectWriter = XmlWriter.Create(replayFile, xmlWriterSettings);
            projectWriter.WriteStartElement("Project");
            bool wroteReplayContent = false;

            if (frameworks is not null)
            {
                wroteReplayContent = true;
                projectWriter.WriteStartElement("PropertyGroup");
                projectWriter.WriteAttributeString("Condition", "'$(TargetFramework)' == ''");
                projectWriter.WriteElementString("TargetFrameworks", string.Join(';', frameworks));
                projectWriter.WriteEndElement();
            }

            if (projectReferenceSetsByTargetFramework is not null)
            {
                if (hasAuthoritativeProjectReferenceSet)
                {
                    wroteReplayContent = true;
                    projectWriter.WriteComment(" Select the captured project references for this exact project instance. ");
                    projectWriter.WriteStartElement("Choose");
                }

                foreach ((string targetFramework, ProjectReferenceSet projectReferenceSet) in projectReferenceSetsByTargetFramework)
                {
                    if (!projectReferenceSet.IsAuthoritative)
                    {
                        continue;
                    }

                    allowListParents.Add(projectPath);

                    projectWriter.WriteStartElement("When");
                    projectWriter.WriteAttributeString("Condition", $"'$(TargetFramework)' == '{targetFramework}'");
                    WriteProjectReferenceAllowListSelection(projectWriter, projectReferenceSet.ProjectReferences.Keys);
                    projectWriter.WriteEndElement();
                }

                if (hasAuthoritativeProjectReferenceSet)
                {
                    projectWriter.WriteEndElement();
                    projectWriter.WriteComment(" Apply the selected allow-list once, including authoritative empty sets. ");
                    WriteProjectReferenceAllowListApplication(projectWriter);
                }

                if (hasProjectReferenceUpdates)
                {
                    wroteReplayContent |= WriteDisableDynamicProjectReferences(
                        projectWriter,
                        projectReferenceSetsByTargetFramework);
                    wroteReplayContent |= WriteProjectReferenceMetadata(
                        projectWriter,
                        projectReferenceSetsByTargetFramework,
                        selectedFrameworksByProject);
                }
            }

            projectWriter.WriteEndElement();
            projectWriter.Close();

            if (wroteReplayContent)
            {
                replayFileCount++;
            }
            else
            {
                File.Delete(replayFile);
            }
        }

        Log.LogMessage(MessageImportance.High, $"Wrote {replayFileCount} project replay files with {selectedFrameworksByProject.Count} target framework selections and {allowListParents.Count} project reference allow-lists to '{replayOutputDirectory}'.");
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

    private static void WriteProjectReferenceAllowListSelection(
        XmlWriter writer,
        ICollection<string> selectedProjectReferences)
    {
        writer.WriteStartElement("PropertyGroup");
        writer.WriteElementString("_ApplyProjectReferenceReplayAllowList", "true");
        writer.WriteEndElement();

        if (selectedProjectReferences.Count > 0)
        {
            writer.WriteStartElement("ItemGroup");
            foreach (string selectedProjectReference in selectedProjectReferences)
            {
                writer.WriteStartElement("_ReplayProjectReference");
                writer.WriteAttributeString("Include", $"$(RepoRoot){selectedProjectReference}");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }

    private static bool WriteProjectReferenceMetadata(
        XmlWriter writer,
        SortedDictionary<string, ProjectReferenceSet> projectReferenceSetsByTargetFramework,
        SortedDictionary<string, SortedSet<string>> selectedFrameworksByProject)
    {
        bool wroteMetadata = false;

        foreach ((string targetFramework, ProjectReferenceSet projectReferenceSet) in projectReferenceSetsByTargetFramework)
        {
            if (projectReferenceSet.ProjectReferences.Count == 0)
            {
                continue;
            }

            bool wroteItemGroup = false;
            foreach ((string projectReference, CapturedProjectReference capturedProjectReference) in projectReferenceSet.ProjectReferences)
            {
                string setTargetFramework = capturedProjectReference.SetTargetFramework;
                string replaySetTargetFramework = setTargetFramework;
                if (string.IsNullOrWhiteSpace(replaySetTargetFramework) &&
                    selectedFrameworksByProject.TryGetValue(projectReference, out SortedSet<string>? selectedFrameworks) &&
                    selectedFrameworks.Count == 1)
                {
                    replaySetTargetFramework = $"TargetFramework={selectedFrameworks.Min}";
                }

                if (!capturedProjectReference.IsDynamicallyAdded &&
                    string.IsNullOrWhiteSpace(replaySetTargetFramework))
                {
                    continue;
                }

                if (!wroteItemGroup)
                {
                    writer.WriteStartElement("ItemGroup");
                    writer.WriteAttributeString("Condition", $"'$(TargetFramework)' == '{targetFramework}'");
                    wroteItemGroup = true;
                    wroteMetadata = true;
                }

                writer.WriteStartElement("ProjectReference");
                writer.WriteAttributeString(
                    capturedProjectReference.IsDynamicallyAdded ? "Include" : "Update",
                    $"$(RepoRoot){projectReference}");
                foreach ((string metadataName, string metadataValue) in capturedProjectReference.Metadata)
                {
                    if (!string.Equals(metadataName, "SetTargetFramework", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteElementString(metadataName, metadataValue);
                    }
                }
                // TODO: Investigate whether authoritative replay can use graph-recognized SetTargetFramework metadata.
                // ProjectReferenceReplaySetTargetFramework is only consumed during traversal execution, which can leave
                // static graph construction propagating Build to a discovery-only outer build.
                if (!string.IsNullOrWhiteSpace(replaySetTargetFramework))
                {
                    writer.WriteElementString(
                        projectReferenceSet.IsAuthoritative ? "ProjectReferenceReplaySetTargetFramework" : "SetTargetFramework",
                        replaySetTargetFramework);
                }
                writer.WriteEndElement();
            }

            if (wroteItemGroup)
            {
                writer.WriteEndElement();
            }
        }

        return wroteMetadata;
    }

    private static bool WriteDisableDynamicProjectReferences(
        XmlWriter writer,
        SortedDictionary<string, ProjectReferenceSet> projectReferenceSetsByTargetFramework)
    {
        bool wroteProperty = false;

        foreach ((string targetFramework, ProjectReferenceSet projectReferenceSet) in projectReferenceSetsByTargetFramework)
        {
            if (!projectReferenceSet.ProjectReferences.Values.Any(projectReference => projectReference.IsDynamicallyAdded))
            {
                continue;
            }

            wroteProperty = true;
            writer.WriteStartElement("PropertyGroup");
            writer.WriteAttributeString("Condition", $"'$(TargetFramework)' == '{targetFramework}'");
            writer.WriteElementString("DisableTransitiveProjectReferences", "true");
            writer.WriteEndElement();
        }

        return wroteProperty;
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

        public SortedDictionary<string, CapturedProjectReference> ProjectReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record CapturedProjectReference(
        string SetTargetFramework,
        bool IsDynamicallyAdded,
        SortedDictionary<string, string> Metadata)
    {
        public bool HasSameMetadata(CapturedProjectReference other) =>
            string.Equals(SetTargetFramework, other.SetTargetFramework, StringComparison.OrdinalIgnoreCase) &&
            IsDynamicallyAdded == other.IsDynamicallyAdded &&
            Metadata.Count == other.Metadata.Count &&
            Metadata.All(
                metadata => other.Metadata.TryGetValue(metadata.Key, out string? value) &&
                    string.Equals(metadata.Value, value, StringComparison.Ordinal));
    }
}
