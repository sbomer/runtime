// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;

namespace DependencyGraphCore
{
    /// <summary>
    /// Represents a node display aggregate pairing a node and the textual reasons
    /// associated with edges between this node and another.
    /// (Logic identical to original BoxDisplay in viewer.)
    /// </summary>
    public class BoxDisplay
    {
        public Node node;
        public List<string> reason;

        public BoxDisplay(Node node, List<string> reason)
        {
            this.node = node;
            this.reason = reason;
        }

        public override string ToString()
        {
            return $"Index: {node.Index}, Name: {node.Name}, {reason.Count} Reason(s): {string.Join(", ", reason.ToArray())}";
        }
    }

    /// <summary>
    /// Represents a node in the dependency graph. Holds source (incoming) and target (outgoing) edges,
    /// each mapped to a list of textual reasons.
    /// </summary>
    public class Node
    {
        public readonly int Index;
        public readonly string Name;
        public readonly Dictionary<Node, List<string>> Targets = new Dictionary<Node, List<string>>();
        public readonly Dictionary<Node, List<string>> Sources = new Dictionary<Node, List<string>>();

        public Node(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public override string ToString()
        {
            return $"Index: {Index}, Name: {Name}";
        }
    }

    /// <summary>
    /// Represents a dependency graph produced by compilation, with nodes indexed identically
    /// to the original implementation.
    /// </summary>
    public class Graph
    {
        public int NextConditionalNodeIndex = int.MaxValue;
        public int PID;
        public int ID;
        public string Name = string.Empty;
        public Dictionary<int, Node> Nodes = new Dictionary<int, Node>();

        public override string ToString()
        {
            return $"PID: {PID}, ID: {ID}, Name: {Name}";
        }

        /// <summary>
        /// Adds a directed edge between two existing nodes with a textual reason.
        /// Returns false if either node does not exist (logic unchanged).
        /// </summary>

                public bool AddEdge(int source, int target, string reason)

                {

                    // Use nullable local variables in TryGetValue to satisfy nullable analysis and guard explicitly.
                    if (Nodes.TryGetValue(source, out var a) && a != null &&
                        Nodes.TryGetValue(target, out var b) && b != null)
                    {
                        var normalizedReason = string.IsNullOrEmpty(reason) ? string.Empty : reason;

                        AddReason(a.Targets, b, normalizedReason);

                        AddReason(b.Sources, a, normalizedReason);

                        return true;
                    }
                    return false;

                }


        /// <summary>
        /// Creates a synthetic "conditional" node linking two reason nodes to a dependee node.
        /// Behavior preserved exactly from the original.
        /// </summary>
        public void AddConditionalEdge(int reason1, int reason2, int target, string reason)
        {
            Node reason1Node = Nodes[reason1];
            Node reason2Node = Nodes[reason2];
            Node dependee = Nodes[target];

            int conditionalNodeIndex = NextConditionalNodeIndex--;
            Node conditionalNode = new Node(conditionalNodeIndex, string.Format("Conditional({0} - {1})", reason1Node.ToString(), reason2Node.ToString()));
            Nodes.Add(conditionalNodeIndex, conditionalNode);

            string normalizedReason = string.IsNullOrEmpty(reason) ? string.Empty : reason;

            AddReason(conditionalNode.Targets, dependee, normalizedReason);
            AddReason(dependee.Sources, conditionalNode, normalizedReason);

            AddReason(reason1Node.Targets, conditionalNode, "Reason1Conditional - " + normalizedReason);
            AddReason(conditionalNode.Sources, reason1Node, "Reason1Conditional - " + normalizedReason);

            AddReason(reason2Node.Targets, conditionalNode, "Reason2Conditional - " + normalizedReason);
            AddReason(conditionalNode.Sources, reason2Node, "Reason2Conditional - " + normalizedReason);
        }

        public void AddNode(int index, string name)
        {
            Node n = new Node(index, name);
            this.Nodes.Add(index, n);
        }


        public void AddReason(Dictionary<Node, List<string>> dict, Node node, string reason)

        {

            // TryGetValue may mark 'reasons' as maybe-null in nullable analysis; guard explicitly.
            if (dict.TryGetValue(node, out var reasons) && reasons != null)
            {
                reasons.Add(reason);

            }

            else

            {

                dict[node] = new List<string> { reason };
            }

        }

    }

    /// <summary>
    /// Global collection of graphs. Mirrors original singleton pattern and methods.
    /// UI-specific ForceRefresh calls are replaced by invoking RefreshAction if set.
    /// </summary>
    public class GraphCollection
    {
        public static readonly GraphCollection Singleton = new GraphCollection();

        /// <summary>
        /// Optional action a host (WinForms viewer, CLI, etc.) can assign to be notified
        /// when the collection changes (add/remove).
        /// </summary>
        public Action? RefreshAction;

        public List<Graph> Graphs = new List<Graph>();

        public void AddGraph(Graph g)
        {
            Graphs.Add(g);
            RefreshAction?.Invoke();
        }

        public void RemoveGraph(Graph g)
        {
            Graphs.Remove(g);
            RefreshAction?.Invoke();
        }

        public Graph? GetGraph(int pid, int id)
        {
            foreach (Graph g in Graphs)
            {
                if ((g.PID == pid) && (g.ID == id))
                    return g;
            }
            return null;
        }

        public void AddNodeToGraph(int pid, int id, int index, string name)
        {
            Graph? g = GetGraph(pid, id);
            if (g == null)
                return;

            g.AddNode(index, name);
        }

        public bool AddEdgeToGraph(int pid, int id, int source, int target, string reason)
        {
            Graph? g = GetGraph(pid, id);
            if (g == null)
                return false;
            return g.AddEdge(source, target, reason);
        }

        public void AddConditionalEdgeToGraph(int pid, int id, int reason1, int reason2, int target, string reason)
        {
            Graph? g = GetGraph(pid, id);
            if (g == null)
                return;
            g.AddConditionalEdge(reason1, reason2, target, reason);
        }
    }
}
