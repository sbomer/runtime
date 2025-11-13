// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using DependencyGraphCore;
using ImGuiNET;

namespace DependencyGraphCli.ImGuiUi;

internal sealed class GraphBrowser : IDisposable
{
    private const int NodeDisplayLimit = 10;

    private readonly GraphCollection _collection;
    private readonly List<Node> _filteredNodes = new();
    private readonly Action? _previousRefresh;

    private Graph? _selectedGraph;
    private Node? _selectedNode;
    private string _filterText = string.Empty;
    private string? _filterError;
    private bool _filterDirty = true;
    private bool _disposed;

    public GraphBrowser(GraphCollection collection)
    {
        _collection = collection;
        _previousRefresh = collection.RefreshAction;
        collection.RefreshAction = OnGraphsChanged;

        if (collection.Graphs.Count > 0)
        {
            _selectedGraph = collection.Graphs[^1];
        }
    }

    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ImGui.DockSpaceOverViewport(ImGui.GetMainViewport());

        RenderGraphListWindow();
        RenderNodeTreeWindow();
        RenderDetailsWindow();
        RenderDiagnosticsWindow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _collection.RefreshAction = _previousRefresh;
    }

    private void OnGraphsChanged()
    {
        _previousRefresh?.Invoke();

        Graph? selected = _selectedGraph;
        if (_collection.Graphs.Count > 0 && (selected is null || !_collection.Graphs.Contains(selected)))
        {
            _selectedGraph = _collection.Graphs[^1];
            _selectedNode = null;
        }

        _filterDirty = true;
    }

    private void RenderGraphListWindow()
    {
        if (!ImGui.Begin("Graphs"))
        {
            ImGui.End();
            return;
        }

        if (_collection.Graphs.Count == 0)
        {
            ImGui.TextUnformatted("No graphs loaded.");
            ImGui.End();
            return;
        }

        Vector2 size = new(-1.0f, -1.0f);
        if (ImGui.BeginListBox("##graph-list", size))
        {
            foreach (Graph graph in _collection.Graphs)
            {
                bool selected = ReferenceEquals(graph, _selectedGraph);
                string label = string.IsNullOrEmpty(graph.Name)
                    ? $"Graph {graph.ID}##graph-{graph.PID}-{graph.ID}"
                    : $"{graph.Name}##graph-{graph.PID}-{graph.ID}";

                if (ImGui.Selectable(label, selected))
                {
                    _selectedGraph = graph;
                    _selectedNode = null;
                    _filterDirty = true;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"PID: {graph.PID}\nID: {graph.ID}\nNodes: {graph.Nodes.Count}");
                }
            }

            ImGui.EndListBox();
        }

        ImGui.End();
    }

    private void RenderNodeTreeWindow()
    {
        if (!ImGui.Begin("Nodes"))
        {
            ImGui.End();
            return;
        }

        if (_selectedGraph == null)
        {
            ImGui.TextUnformatted("Select a graph to inspect its nodes.");
            ImGui.End();
            return;
        }

        string filterBuffer = _filterText;
        if (ImGui.InputTextWithHint("Filter", "substring or regex", ref filterBuffer, 512))
        {
            _filterText = filterBuffer;
            _filterDirty = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _filterText = string.Empty;
            _filterDirty = true;
        }

        if (_filterDirty)
        {
            RebuildFilteredNodes();
        }

        if (!string.IsNullOrEmpty(_filterError))
        {
            Vector4 color = new(1.0f, 0.45f, 0.35f, 1.0f);
            ImGui.TextColored(color, _filterError);
        }

        ImGui.Separator();
        ImGui.TextUnformatted(_filteredNodes.Count == _selectedGraph.Nodes.Count
            ? $"Nodes: {_selectedGraph.Nodes.Count}"
            : $"Nodes: {_filteredNodes.Count} / {_selectedGraph.Nodes.Count}");
        if (_filteredNodes.Count > NodeDisplayLimit)
        {
            ImGui.TextDisabled($"Showing first {NodeDisplayLimit} nodes. Apply a filter to narrow the list.");
        }

        Vector2 available = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("node-tree", available, ImGuiChildFlags.Border))
        {
            int shownCount = Math.Min(_filteredNodes.Count, NodeDisplayLimit);
            for (int i = 0; i < shownCount; i++)
            {
                Node node = _filteredNodes[i];
                ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanFullWidth;
                if (ReferenceEquals(node, _selectedNode))
                {
                    flags |= ImGuiTreeNodeFlags.Selected;
                }

                string label = $"[{node.Index}] {node.Name}";
                bool open = ImGui.TreeNodeEx($"{label}##node-{node.Index}", flags);
                if (ImGui.IsItemClicked())
                {
                    _selectedNode = node;
                }

                if (open)
                {
                    DrawEdgeGroup("Incoming", node.Sources, $"in-{node.Index}");
                    DrawEdgeGroup("Outgoing", node.Targets, $"out-{node.Index}");
                    ImGui.TreePop();
                }
            }
        }
        ImGui.EndChild();

        ImGui.End();
    }

    private void RenderDetailsWindow()
    {
        if (!ImGui.Begin("Details"))
        {
            ImGui.End();
            return;
        }

        if (_selectedNode == null)
        {
            ImGui.TextUnformatted("Select a node to see additional information.");
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted($"Index: {_selectedNode.Index}");
        ImGui.TextWrapped(_selectedNode.Name);

        ImGui.Separator();
        ImGui.TextUnformatted($"Incoming edges: {_selectedNode.Sources.Count}");
        ImGui.TextUnformatted($"Outgoing edges: {_selectedNode.Targets.Count}");

        if (ImGui.Button("Copy name"))
        {
            ImGui.SetClipboardText(_selectedNode.Name);
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy index"))
        {
            ImGui.SetClipboardText(_selectedNode.Index.ToString());
        }

        ImGui.Separator();
        if (_selectedNode.Sources.Count == 0 && _selectedNode.Targets.Count == 0)
        {
            ImGui.TextUnformatted("No edges associated with this node.");
        }
        else
        {
            if (ImGui.TreeNodeEx("Incoming reasons", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth))
            {
                foreach (var entry in _selectedNode.Sources.OrderBy(e => e.Key.Index))
                {
                    ImGui.BulletText(BuildReasonLine(entry.Key, entry.Value));
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("Outgoing reasons", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth))
            {
                foreach (var entry in _selectedNode.Targets.OrderBy(e => e.Key.Index))
                {
                    ImGui.BulletText(BuildReasonLine(entry.Key, entry.Value));
                }
                ImGui.TreePop();
            }
        }

        ImGui.End();
    }

    private void RebuildFilteredNodes()
    {
        _filterDirty = false;
        _filterError = null;
        _filteredNodes.Clear();

        if (_selectedGraph == null)
        {
            return;
        }

        string filter = _filterText?.Trim() ?? string.Empty;
        bool hasFilter = filter.Length > 0;
        Regex? regex = null;

        if (hasFilter)
        {
            try
            {
                regex = new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                _filterError = $"Invalid regex: {ex.Message}";
            }
        }

        foreach (Node node in _selectedGraph.Nodes.Values.OrderBy(n => n.Index))
        {
            if (!hasFilter)
            {
                _filteredNodes.Add(node);
                continue;
            }

            if (MatchesFilter(node, filter, regex))
            {
                _filteredNodes.Add(node);
            }
        }
    }

    private static bool MatchesFilter(Node node, string filter, Regex? regex)
    {
        if (node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (node.Index.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (regex != null)
        {
            return regex.IsMatch(node.ToString());
        }

        return false;
    }

    private void DrawEdgeGroup(string title, Dictionary<Node, List<string>> edges, string idSuffix)
    {
        if (edges.Count == 0)
        {
            ImGui.TextUnformatted($"{title}: (none)");
            return;
        }

        if (!ImGui.TreeNodeEx($"{title}##{idSuffix}", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth))
        {
            return;
        }

        foreach (var entry in edges.OrderBy(e => e.Key.Index))
        {
            Node target = entry.Key;
            List<string> reasons = entry.Value;
            string header = $"[{target.Index}] {target.Name} ({reasons.Count})";
            bool open = ImGui.TreeNodeEx($"{header}##edge-{idSuffix}-{target.Index}", ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.OpenOnDoubleClick);

            if (ImGui.IsItemClicked())
            {
                _selectedNode = target;
            }

            if (open)
            {
                foreach (string reason in reasons)
                {
                    string text = string.IsNullOrEmpty(reason) ? "(no reason)" : reason;
                    ImGui.BulletText(text);
                }

                ImGui.TreePop();
            }
        }

        ImGui.TreePop();
    }

    private static string BuildReasonLine(Node node, List<string> reasons)
    {
        string reasonSummary = reasons.Count == 0
            ? "(no reason)"
            : string.Join(", ", reasons.Select(r => string.IsNullOrEmpty(r) ? "(no reason)" : r));
        return $"[{node.Index}] {node.Name}: {reasonSummary}";
    }

    private void RenderDiagnosticsWindow()
    {
        if (!ImGui.Begin("Diagnostics"))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted($"Graphs loaded: {_collection.Graphs.Count}");
        Graph? selected = _selectedGraph;
        if (selected is null)
        {
            ImGui.TextUnformatted("Selected graph: (none)");
        }
        else
        {
            ImGui.TextUnformatted($"Selected graph nodes: {selected.Nodes.Count}");
            ImGui.TextUnformatted($"Filtered nodes: {_filteredNodes.Count}");
            ImGui.TextUnformatted($"Display limit: {Math.Min(_filteredNodes.Count, NodeDisplayLimit)}");
        }

        ImGui.Separator();
        ImGuiIOPtr io = ImGui.GetIO();
        float framerate = io.Framerate;
        if (framerate > 0)
        {
            float frameTimeMs = 1000f / framerate;
            ImGui.TextUnformatted($"Frame time: {frameTimeMs:F2} ms");
        }
        else
        {
            ImGui.TextUnformatted("Frame time: (unknown)");
        }
        ImGui.TextUnformatted($"FPS: {framerate:F1}");

        ImGui.End();
    }
}
