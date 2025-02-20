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

namespace Test.BuildXL.FrontEnd.Rush.BuildGraphPluginMock
{
    internal class Program
    {
        /// <summary>
        /// Mocks the build graph plugin generation for Rush.
        /// </summary>
        /// <remarks>
        /// Expected arguments are: "[rush verb] [rush arguments] --drop-graph [outputGraphFile]". This mimics the CLI of the actual rush tool.
        /// Environment variables control how the graph is produced (environment variables are used as opposed to command line arguments 
        /// to avoid interfering with the actual arguments).  
        /// RUSH_BUILD_GRAPH_MOCK_NODES can be set to a comma-separated list of nodes to include in the graph.
        /// RUSH_BUILD_GRAPH_UNCACHEABLE_NODES can be set to a comma-separated list of nodes to mark as uncacheable.
        /// E.g.
        /// "A -> B -> C, D -> C"
        /// If not set, an empty graph is produced
        /// RUSH_BUILD_GRAPH_MOCK_ROOT can be set to the root of the repo, so that generated paths fall under it. An arbitrary default is used if not set.
        /// Prints to stderr the actual arguments passed to the rush tool, so they can be verified as a graph construction warning
        /// </remarks>
        public static int Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("BuildGraphPluginMockDebugOnStart") == "1")
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

            if (args.Length < 3)
            {
                Console.Error.WriteLine("Expected arguments are: verb [rush arguments] --drop-graph [outputGraphFile]");
                return 1;
            }

            int dropGraphArg = Array.IndexOf(args, "--drop-graph");
            if (dropGraphArg == -1 || args.Length <= dropGraphArg + 1)
            {
                Console.Error.WriteLine("Expected arguments are: verb [rush arguments] --drop-graph [outputGraphFile]");
                return 1;
            }

            var outputFilePath = args[dropGraphArg + 1];

            var nodes = Environment.GetEnvironmentVariable("RUSH_BUILD_GRAPH_MOCK_NODES");
            var root = Environment.GetEnvironmentVariable("RUSH_BUILD_GRAPH_MOCK_ROOT");
            var uncacheableNodes = Environment.GetEnvironmentVariable("RUSH_BUILD_GRAPH_UNCACHEABLE_NODES");

            string graph = SerializeGraph(root, nodes, uncacheableNodes);

            File.WriteAllText(outputFilePath, graph);

            // Print the actual arguments passed to the rush tool to stderr
            // This will show up as a graph construction warning that can be retrieved from the log listener
            Console.Error.WriteLine(string.Join(" ", args));

            return 0;
        }

        /// <summary>
        /// For the Windows case, paths are going into a JSON object, so they need extra escaping.
        /// </summary>
        private static string BuildPath(string root, string suffix) => root != null 
            ? OperatingSystemHelper.IsWindowsOS ? $"{root}\\\\{suffix}" : Path.Combine(root, suffix)
            : OperatingSystemHelper.IsWindowsOS ? $"C:\\\\repo\\\\{suffix}" : $"/home/user/src/repo/{suffix}";

        private static string MockRepoSettings(string root) =>
@$"""repoSettings"": {{
    ""commonTempFolder"": ""{BuildPath(root, "temp")}""
}}";

        private static string SerializeGraph(string root, string nodes, string uncacheableNodeList)
        {
            // Store here all the nodes (keys) and their dependencies (values)
            var graph = new MultiValueDictionary<string, string>();
            var uncacheableSet = new HashSet<string>();

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

            if (uncacheableNodeList != null)
            {
                uncacheableSet.AddRange(uncacheableNodeList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }

            // Now build the JSON representation of the graph. This format should be in sync with the actual build graph plugin format.
            var jsonNodes = new StringBuilder();
            foreach (var kvp in graph)
            {
                var node = kvp.Key;

                jsonNodes.Append(@$"{{
    ""id"": ""{node}"",
    ""task"": ""{node}"",
    ""package"": ""{node}"",
    ""dependencies"": [ {string.Join(", ", kvp.Value.Select(v => @$"""{v}"""))} ],
    ""workingDirectory"": ""{BuildPath(root,node)}"",
    ""command"": ""echo {node}""
    {(uncacheableSet.Contains(node) ? @",""cacheable"": false" : string.Empty)}
}},");
            }

            if (graph.Count > 0)
            {
                jsonNodes.Remove(jsonNodes.Length - 1, 1); // Remove the last comma
            }

            return 
@$"{{
    ""nodes"": [ {jsonNodes} ],
    {MockRepoSettings(root)}
}}";
        }
    }
}
