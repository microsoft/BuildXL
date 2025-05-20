// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.FrontEnd.Lage.Mock
{
    internal class Program
    {
        /// <summary>
        /// Mocks the build graph generation for Lage.
        /// </summary>
        /// <remarks>
        /// Source dependencies are not available yet on Lage. This mock is intended to be used in the meantime for testing purposes.
        /// Expected arguments are: "info [targets]". This mimics the CLI of (a subset of of) the actual lage tool.
        /// Environment variables control how the graph is produced (environment variables are used as opposed to command line arguments 
        /// to avoid interfering with the actual arguments).  
        /// LAGE_BUILD_GRAPH_MOCK_NODES can be set to a comma-separated list of nodes to include in the graph.
        /// E.g.
        /// "A -> B -> C, D -> C"
        /// If not set, an empty graph is produced
        /// LAGE_BUILD_GRAPH_MOCK_SOURCE_DIRECTORIES can be set to a comma-separated list of nodes to source directories to include in the graph.
        /// E.g.
        /// A -> path/to/foo, A -> path/to/bar, B -> path/to/baz
        /// </remarks>
        public static int Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("LageMockDebugOnStart") == "1")
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    Console.WriteLine("=== Attach to this process from a debugger, then press ENTER to continue ...");
                    Console.ReadLine();
                }
                else
                {
                    Debugger.Launch();
                }
            }

            if (args.Length < 1)
            {
                Console.Error.WriteLine("Expected arguments are: info [targets]");
                return 1;
            }

            var nodes = Environment.GetEnvironmentVariable("LAGE_BUILD_GRAPH_MOCK_NODES");
            var sourceDirectories = Environment.GetEnvironmentVariable("LAGE_BUILD_GRAPH_MOCK_SOURCE_DIRECTORIES");

            string graph = SerializeGraph(nodes, sourceDirectories);

            Console.Write(graph);

            return 0;
        }

        private static string SerializeGraph(string nodes, string sourceDirectories)
        {
            // Store here all the nodes (keys) and their dependencies (values)
            var graph = new MultiValueDictionary<string, string>();
            // Nodes (keys) and their source directories (values)
            var processedSourceDirectories = new MultiValueDictionary<string, string>();

            if (nodes != null)
            {
                // Split the comma separated chains
                var chains = nodes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                foreach (var chain in chains)
                {
                    // Split the chain into individual nodes
                    var nodeNames = chain.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < nodeNames.Length; i++)
                    {
                        // If we have an actual dependency n1 -> n2, add it to the graph
                        if (i < nodeNames.Length - 1)
                        {
                            graph.Add(nodeNames[i], nodeNames[i + 1]);
                        }
                        else
                        {
                            // If we are in the last node of the chain, and it is not part of the graph yet, add it with no dependencies
                            if (!graph.ContainsKey(nodeNames[i]))
                            {
                                graph.Add(nodeNames[i], Array.Empty<string>());
                            }
                        }
                    }
                }
            }

            if (sourceDirectories != null)
            {
                // Split the comma separated chains
                var chains = sourceDirectories.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                foreach (var chain in chains)
                {
                    // Split the chain into individual nodes
                    var nodeToSourceDir = chain.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (nodeToSourceDir.Length != 2)
                    {
                        throw new ArgumentException($"Invalid node definition: {chain}. Expected format is 'node -> sourceDir'.");
                    }

                    // Add the node to the source directory mapping
                    processedSourceDirectories.Add(nodeToSourceDir[0], nodeToSourceDir[1]);
                }
            }

            // Now build the JSON representation of the graph. This format should be in sync with the actual build graph plugin format.
            var jsonNodes = new StringBuilder();
            foreach (var kvp in graph)
            {
                var node = kvp.Key;

                IReadOnlyList<string> sourceDependencies = null;
                processedSourceDirectories.TryGetValue(node, out sourceDependencies);

                jsonNodes.Append(@$"{{
    ""id"": ""{node}"",
    ""task"": ""{node}"",
    ""package"": ""{node}"",
    ""dependencies"": [ {string.Join(", ", kvp.Value.Select(v => @$"""{v}"""))} ],
    ""workingDirectory"": ""{node}"",
    ""command"": [""echo {node}""]
    {(sourceDependencies != null ? @$",""sourceDependencies"": [ {string.Join(", ", sourceDependencies.Select(sd => @$"""{sd}"""))} ]" : string.Empty)}
}},");
            }

            if (graph.Count > 0)
            {
                jsonNodes.Remove(jsonNodes.Length - 1, 1); // Remove the last comma
            }

            return 
@$"{{
  ""timestamp"": 1737434500288,
  ""level"": 30,
  ""msg"": ""info"",
  ""data"": {{
    ""command"": [""build""],
    ""packageTasks"": [ {jsonNodes} ]
    }}
}}";
        }
    }
}
