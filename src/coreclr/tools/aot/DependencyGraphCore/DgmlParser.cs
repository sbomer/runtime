// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.IO;
using System.Xml;

namespace DependencyGraphCore
{
    /// <summary>
    /// Result of a DGML parse operation.
    /// </summary>
    public sealed class DgmlParseResult
    {
        /// <summary>
        /// True if parsing succeeded and the Graph is valid.
        /// </summary>
        public bool Success { get; init; }

        /// <summary>
        /// Populated when Success == true.
        /// </summary>
        public Graph? Graph { get; init; }

        /// <summary>
        /// Error message when Success == false.
        /// </summary>
        public string? ErrorMessage { get; init; }

        public static DgmlParseResult Ok(Graph g) => new DgmlParseResult { Success = true, Graph = g };
        public static DgmlParseResult Fail(string error) => new DgmlParseResult { Success = false, ErrorMessage = error };
    }

    /// <summary>
    /// Parses DGML dependency graph dumps (NativeAOT, Crossgen2, IL Linker) into Graph objects.
    /// Logic mirrors DGMLGraphProcessing.ParseXML from the WinForms viewer.
    /// </summary>
    public static class DgmlParser
    {
        /// <summary>
        /// Parse a DGML file from a path. On success returns a valid Graph wrapped in a DgmlParseResult.
        /// On failure returns a DgmlParseResult with Success == false and an ErrorMessage.
        /// </summary>
        /// <param name="filePath">Path to DGML (.dgml or .dgml.xml) or generic XML file with DirectedGraph root.</param>
        /// <param name="fileId">Numeric identifier; used to set Graph.PID and Graph.ID (kept identical to original semantics).</param>
        public static DgmlParseResult ParseFromFile(string filePath, int fileId)
        {
            FileStream fs;
            try
            {
                fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex)
            {
                return DgmlParseResult.Fail($"Failure to open file '{filePath}': {ex}");
            }

            try
            {
                return Parse(fs, fileId, filePath);
            }
            finally
            {
                fs.Dispose();
            }
        }

        /// <summary>
        /// Parse DGML from a stream. The stream is not disposed by this method; caller owns it.
        /// </summary>
        /// <param name="stream">Readable stream positioned at beginning of DGML/XML.</param>
        /// <param name="fileId">Numeric identifier for Graph.PID/ID.</param>
        /// <param name="graphName">Name recorded on the Graph (original code used the file name).</param>
        public static DgmlParseResult Parse(Stream stream, int fileId, string graphName)
        {
            if (stream == null || stream == Stream.Null)
                return DgmlParseResult.Fail("Invalid stream.");

            Graph g = new Graph();

            XmlReaderSettings settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true
            };

            try
            {
                using (XmlReader reader = XmlReader.Create(stream, settings))
                {
                    while (reader.Read())
                    {
                        // Skip Property/Properties elements (matching original logic).
                        if (reader.NodeType == XmlNodeType.Element &&
                            (reader.Name == "Property" || reader.Name == "Properties"))
                        {
                            continue;
                        }

                        if (reader.NodeType != XmlNodeType.Element)
                            continue;

                        switch (reader.Name)
                        {
                            case "Node":
                                {
                                    // Original code: int.Parse(reader.GetAttribute("Id"))
                                    // No validation added; maintain identical behavior.
                                    string? idStr = reader.GetAttribute("Id");
                                    if (string.IsNullOrEmpty(idStr))
                                        return DgmlParseResult.Fail("Node element missing 'Id' attribute.");
                                    int id = int.Parse(idStr!);
                                    string label = reader.GetAttribute("Label") ?? string.Empty;
                                    g.AddNode(id, label);
                                    break;
                                }
                            case "Link":
                                {
                                    string? sourceStr = reader.GetAttribute("Source");
                                    string? targetStr = reader.GetAttribute("Target");
                                    if (string.IsNullOrEmpty(sourceStr) || string.IsNullOrEmpty(targetStr))
                                        return DgmlParseResult.Fail("Link element missing 'Source' or 'Target' attribute.");
                                    int source = int.Parse(sourceStr!);
                                    int target = int.Parse(targetStr!);
                                    string edgeReason = reader.GetAttribute("Reason") ?? string.Empty; // normalized to non-null

                                    if (!g.AddEdge(source, target, edgeReason))
                                    {
                                        // Original returned false -> invalid graph (nonexistent nodes).
                                        return DgmlParseResult.Fail("Nonexistent nodes present in Links.");
                                    }
                                    break;
                                }
                            case "DirectedGraph":
                                {
                                    // Set identifying info (original semantics: g.ID = FileID; g.PID = FileID; g.Name = _name)
                                    g.ID = fileId;
                                    g.PID = fileId;
                                    g.Name = graphName;
                                    break;
                                }
                            default:
                                // All other elements are ignored as in the original implementation.
                                break;
                        }
                    }
                }
            }
            catch (XmlException xex)
            {
                return DgmlParseResult.Fail($"XML parsing error: {xex.Message}");
            }
            catch (FormatException fex)
            {
                // int.Parse failures or similar
                return DgmlParseResult.Fail($"Format error while parsing numeric attribute: {fex.Message}");
            }
            catch (Exception ex)
            {
                return DgmlParseResult.Fail($"Unexpected error during parse: {ex}");
            }

            return DgmlParseResult.Ok(g);
        }
    }
}
