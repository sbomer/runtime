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
            if (parts.Length != 5)
            {
                Log.LogWarning($"Ignoring malformed project reference capture record: {line}");
                continue;
            }

            string parentProjectPath = parts[0];
            string parentTargetFramework = parts[1];
            string projectPath = parts[2];
            string setTargetFramework = parts[3];
            if (!Enum.TryParse(parts[4], ignoreCase: true, out ProjectReferenceCaptureOperation operation))
            {
                Log.LogError($"Project reference capture record has an invalid operation: {line}");
                continue;
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

            if (operation == ProjectReferenceCaptureOperation.Configure)
            {
                if (!string.IsNullOrWhiteSpace(projectPath) || !string.IsNullOrWhiteSpace(setTargetFramework))
                {
                    Log.LogWarning($"Ignoring invalid project reference configuration record: {line}");
                    continue;
                }

                projectReferenceSet.UseTraversalSetTargetFramework = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                Log.LogWarning($"Ignoring project reference capture record with an empty referenced project: {line}");
                continue;
            }

            if (operation == ProjectReferenceCaptureOperation.Remove && !string.IsNullOrWhiteSpace(setTargetFramework))
            {
                Log.LogWarning($"Ignoring removed project reference with SetTargetFramework metadata: {line}");
                continue;
            }

            CapturedProjectReference capturedProjectReference = new(setTargetFramework, operation);
            if (!projectReferenceSet.ProjectReferences.TryAdd(projectPath, capturedProjectReference))
            {
                CapturedProjectReference existingProjectReference = projectReferenceSet.ProjectReferences[projectPath];
                if (!existingProjectReference.HasSameMetadata(capturedProjectReference))
                {
                    Log.LogError($"Project reference capture has conflicting metadata for '{parentProjectPath}' -> '{projectPath}'.");
                }
            }

            if (operation == ProjectReferenceCaptureOperation.Remove ||
                string.IsNullOrWhiteSpace(setTargetFramework))
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
        int replayFileCount = 0;
        foreach (string projectPath in replayProjects)
        {
            selectedFrameworksByProject.TryGetValue(projectPath, out SortedSet<string>? frameworks);
            projectReferenceSetsByParent.TryGetValue(projectPath, out SortedDictionary<string, ProjectReferenceSet>? projectReferenceSetsByTargetFramework);

            bool hasProjectReferenceDeltas = projectReferenceSetsByTargetFramework is not null &&
                projectReferenceSetsByTargetFramework.Values.Any(
                    projectReferenceSet => projectReferenceSet.ProjectReferences.Values.Any(
                        projectReference => projectReference.Operation != ProjectReferenceCaptureOperation.Update));

            if (frameworks is null && !hasProjectReferenceDeltas)
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

            if (hasProjectReferenceDeltas)
            {
                wroteReplayContent |= WriteDisableDynamicProjectReferences(
                    projectWriter,
                    projectReferenceSetsByTargetFramework!);
                wroteReplayContent |= WriteProjectReferenceDelta(
                    projectWriter,
                    projectReferenceSetsByTargetFramework!);
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

        Log.LogMessage(MessageImportance.High, $"Wrote {replayFileCount} project replay files with {selectedFrameworksByProject.Count} target framework selections to '{replayOutputDirectory}'.");
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

    private static bool WriteProjectReferenceDelta(
        XmlWriter writer,
        SortedDictionary<string, ProjectReferenceSet> projectReferenceSetsByTargetFramework)
    {
        bool wroteDelta = false;

        foreach ((string targetFramework, ProjectReferenceSet projectReferenceSet) in projectReferenceSetsByTargetFramework)
        {
            if (projectReferenceSet.ProjectReferences.Count == 0)
            {
                continue;
            }

            bool wroteItemGroup = false;
            foreach ((string projectReference, CapturedProjectReference capturedProjectReference) in projectReferenceSet.ProjectReferences)
            {
                if (capturedProjectReference.Operation == ProjectReferenceCaptureOperation.Update)
                {
                    continue;
                }

                if (capturedProjectReference.Operation == ProjectReferenceCaptureOperation.Remove)
                {
                    StartItemGroup();
                    writer.WriteStartElement("ProjectReference");
                    writer.WriteAttributeString("Remove", $"$(RepoRoot){projectReference}");
                    writer.WriteEndElement();
                    continue;
                }

                StartItemGroup();
                writer.WriteStartElement("ProjectReference");
                writer.WriteAttributeString("Include", $"$(RepoRoot){projectReference}");
                if (!string.IsNullOrWhiteSpace(capturedProjectReference.SetTargetFramework))
                {
                    writer.WriteElementString(
                        projectReferenceSet.UseTraversalSetTargetFramework ? "ProjectReferenceReplaySetTargetFramework" : "SetTargetFramework",
                        capturedProjectReference.SetTargetFramework);
                }
                writer.WriteEndElement();
            }

            if (wroteItemGroup)
            {
                writer.WriteEndElement();
            }

            void StartItemGroup()
            {
                if (wroteItemGroup)
                {
                    return;
                }

                writer.WriteStartElement("ItemGroup");
                writer.WriteAttributeString("Condition", $"'$(TargetFramework)' == '{targetFramework}'");
                wroteItemGroup = true;
                wroteDelta = true;
            }
        }

        return wroteDelta;
    }

    private static bool WriteDisableDynamicProjectReferences(
        XmlWriter writer,
        SortedDictionary<string, ProjectReferenceSet> projectReferenceSetsByTargetFramework)
    {
        bool wroteProperty = false;

        foreach ((string targetFramework, ProjectReferenceSet projectReferenceSet) in projectReferenceSetsByTargetFramework)
        {
            if (!projectReferenceSet.ProjectReferences.Values.Any(
                    projectReference => projectReference.Operation == ProjectReferenceCaptureOperation.Add))
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

    private sealed class ProjectReferenceSet
    {
        public bool UseTraversalSetTargetFramework { get; set; }

        public SortedDictionary<string, CapturedProjectReference> ProjectReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record CapturedProjectReference(
        string SetTargetFramework,
        ProjectReferenceCaptureOperation Operation)
    {
        public bool HasSameMetadata(CapturedProjectReference other) =>
            string.Equals(SetTargetFramework, other.SetTargetFramework, StringComparison.OrdinalIgnoreCase) &&
            Operation == other.Operation;
    }
}
