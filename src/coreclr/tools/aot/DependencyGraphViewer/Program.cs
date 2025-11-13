// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using DependencyGraphCore; // Shared core (Graph, Node, GraphCollection, BoxDisplay)

[assembly: InternalsVisibleTo("DependecyGraphViewer.Tests")]

namespace DependencyLogViewer
{
    // BoxDisplay moved to DependencyGraphCore (shared logic)
    // Node moved to DependencyGraphCore (shared logic)

    // Graph moved to DependencyGraphCore (shared logic)

    // GraphCollection moved to DependencyGraphCore (shared logic); viewer subscribes via DependencyGraphs.DependencyGraphsUI

    internal enum GraphEventType
    {
        NewGraph,
        NewNode,
        NewEdge,
        NewConditionalEdge,
    }

    internal struct GraphEvent
    {
        public int Pid;
        public int Id;
        public GraphEventType EventType;
        public int Num1;
        public int Num2;
        public int Num3;
        public string Str;
    }

    public class DGMLGraphProcessing
    {
        public delegate void OnCompleted(int currFileID);
        public event OnCompleted Complete;
        public Graph g;
        private string _name;

        internal DGMLGraphProcessing(int file)
        {
            FileID = file;
            g = new Graph();
        }

        public static bool StartProcess(int fileID, string argPath)
        {
            var proc = new DGMLGraphProcessing(fileID);
            proc._name = argPath;
            GraphCollection collection = GraphCollection.Singleton;
            proc.Complete += (fid) =>
            {
                lock (collection)
                {
                    collection.AddGraph(proc.g);
                }
                Debug.Assert(fid == proc.FileID);
            };
            return proc.LoadGraph(argPath);
        }

        public int FileID { get; init; }

        private bool LoadGraph(string argPath)
        {
            Stream stream = null;
            string name = null;
            if (argPath != null)
            {
                try
                {
                    stream = new FileStream(argPath, FileMode.Open);
                    name = argPath;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failure to open file {argPath} \n{e}");
                    return false;
                }
            }
            else
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = @"XML (*.xml)|*.xml| DGML (*.dgml; *.dgml.xml)|*.dgml;*.dgml.xml";
                    openFileDialog.FilterIndex = 2;
                    openFileDialog.RestoreDirectory = true;

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        stream = (FileStream)openFileDialog.OpenFile();
                        name = stream.Name;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return LoadGraph(stream, name);
        }

        internal bool LoadGraph(Stream stream, string name)
        {
            if (stream == Stream.Null)
                return false;

            _name = name;
            // Run parse on background thread to preserve original async behavior.
            Thread th = new Thread(ProcessingMain);
            th.Start(new Tuple<DGMLGraphProcessing, Stream>(this, stream));
            return true;
        }

        public static void ProcessingMain(object obj)
        {
            var tuple = (Tuple<DGMLGraphProcessing, Stream>)obj;
            var writer = tuple.Item1;
            var stream = tuple.Item2;

            var result = DgmlParser.Parse(stream, writer.FileID, writer._name ?? "graph");
            // Close stream after parse (original code closed fileStream).
            try { stream.Close(); } catch { }

            if (!result.Success)
            {
                DependencyGraphs.showError(result.ErrorMessage ?? "Nonexistent nodes present in Links");
                return;
            }

            writer.g = result.Graph!;
            writer.Complete?.Invoke(writer.FileID);
        }
    }

    internal class ETWGraphProcessing
    {
        public static ETWGraphProcessing Singleton;

        private ConcurrentQueue<GraphEvent> events = new ConcurrentQueue<GraphEvent>();
        private TraceEventSession session;
        private volatile bool stopped;

        public ETWGraphProcessing()
        {
            var sessionName = "GraphETWEventProcessingSession";

            session = new TraceEventSession(sessionName);

            Thread t = new Thread(EventProcessingThread);
            t.Start();

            Thread t2 = new Thread(ETWImportingThread);
            t2.Start();
        }

        private void EventProcessingThread()
        {
            GraphCollection collection = GraphCollection.Singleton;

            while (!stopped)
            {
                Thread.Sleep(1);

                lock (collection)
                {
                    GraphEvent eventRead;
                    while (events.TryDequeue(out eventRead))
                    {
                        try
                        {
                            switch (eventRead.EventType)
                            {
                                case GraphEventType.NewEdge:
                                    collection.AddEdgeToGraph(eventRead.Pid, eventRead.Id, eventRead.Num1, eventRead.Num2, eventRead.Str);
                                    break;
                                case GraphEventType.NewNode:
                                    collection.AddNodeToGraph(eventRead.Pid, eventRead.Id, eventRead.Num1, eventRead.Str);
                                    break;

                                                                case GraphEventType.NewGraph:
                                                                    // Use shared Graph model with object initializer (logic unchanged)
                                                                    Graph g = new Graph { PID = eventRead.Pid, ID = eventRead.Id, Name = eventRead.Str };

                                                                    collection.AddGraph(g);

                                                                    break;

                                case GraphEventType.NewConditionalEdge:
                                    collection.AddConditionalEdgeToGraph(eventRead.Pid, eventRead.Id, eventRead.Num1, eventRead.Num2, eventRead.Num3, eventRead.Str);
                                    break;
                            }
                        }
                        catch
                        {
                            // Ignore bad input
                        }
                    }
                }
            }
        }

        private void ETWImportingThread()
        {

            using (session)
            {
                session.BufferSizeMB = 1024;
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-ILCompiler-DependencyGraph", "Graph", delegate (TraceEvent data)
                {
                    GraphEvent ge = default(GraphEvent);
                    ge.EventType = GraphEventType.NewGraph;
                    ge.Pid = data.ProcessID;
                    ge.Id = (int)data.PayloadValue(0);
                    ge.Str = (string)data.PayloadValue(1);
                    events.Enqueue(ge);
                });
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-ILCompiler-DependencyGraph", "Node", delegate (TraceEvent data)
                {
                    GraphEvent ge = default(GraphEvent);
                    ge.EventType = GraphEventType.NewNode;
                    ge.Pid = data.ProcessID;
                    ge.Id = (int)data.PayloadValue(0);
                    ge.Num1 = (int)data.PayloadValue(1);
                    ge.Str = (string)data.PayloadValue(2);
                    events.Enqueue(ge);
                });
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-ILCompiler-DependencyGraph", "Edge", delegate (TraceEvent data)
                {
                    GraphEvent ge = default(GraphEvent);
                    ge.EventType = GraphEventType.NewEdge;
                    ge.Pid = data.ProcessID;
                    ge.Id = (int)data.PayloadValue(0);
                    ge.Num1 = (int)data.PayloadValue(1);
                    ge.Num2 = (int)data.PayloadValue(2);
                    ge.Str = (string)data.PayloadValue(3);
                    events.Enqueue(ge);
                });
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-ILCompiler-DependencyGraph", "ConditionalEdge", delegate (TraceEvent data)
                {
                    GraphEvent ge = default(GraphEvent);
                    ge.EventType = GraphEventType.NewConditionalEdge;
                    ge.Pid = data.ProcessID;
                    ge.Id = (int)data.PayloadValue(0);
                    ge.Num1 = (int)data.PayloadValue(1);
                    ge.Num2 = (int)data.PayloadValue(2);
                    ge.Num3 = (int)data.PayloadValue(3);
                    ge.Str = (string)data.PayloadValue(4);
                    events.Enqueue(ge);
                });

                var restarted = session.EnableProvider("Microsoft-ILCompiler-DependencyGraph");
                session.Source.Process();
            }
        }

        public void Stop()
        {
            session.Source.Dispose();
            stopped = true;
        }
    }

    public static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
#nullable enable
        [STAThread]
        public static int Main(string[] args)
        {
            string? argPath = args.Length > 0 ? args[0] : null;

            //Application.EnableVisualStyles();
            //Application.SetCompatibleTextRenderingDefault(true);

            ApplicationConfiguration.Initialize();

            GraphCollection.DependencyGraphsUI = new DependencyGraphs(argPath);


            Application.Run(GraphCollection.DependencyGraphsUI);

            return 0;
        }
#nullable restore
    }
}
