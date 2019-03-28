// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Takes an existing <see cref="BuildGraph"/> and synthetically increases its size
    /// </remarks>
    public sealed class GraphMultiplier
    {
        private readonly BuildGraph m_graph;

        /// <summary>
        /// Constructor
        /// </summary>
        private GraphMultiplier(BuildGraph sourceGraph)
        {
            m_graph = sourceGraph;
        }

        /// <summary>
        /// Mutates the graph by duplicating the graph into n additional parallelizable graphs.
        /// </summary>
        public static void DuplicateAsParallelGraphs(BuildGraph graph, int duplicationFactor, int maxPipsPerSpec)
        {
            GraphMultiplier multiplier = new GraphMultiplier(graph);
            multiplier.Duplicate(duplicationFactor, maxPipsPerSpec);
        }

        private void Duplicate(int multiplicationFactor, int maxPipsPerSpec)
        {
            Console.WriteLine("Duplicating Graph {0} times", multiplicationFactor);

            // Take a snapshot of the pips currently in the graph before any multiplication is applied.
            Process[] originalProcesses = m_graph.Pips.Values.OfType<Process>().ToArray();
            WriteFile[] originalWriteFiles = m_graph.Pips.Values.OfType<WriteFile>().ToArray();

            // The basic strategy here is to clone every pip in a parallelizeable way. Each pip gets n copies with same
            // source inputs, but different outputs. Dependencies within the original set are translated to the new set
            for (int i = 0; i < multiplicationFactor; i++)
            {
                Console.WriteLine("Duplicating Graph iteration {0}", i);
                string newRoot = i.ToString(CultureInfo.InvariantCulture);
                for (int j = 0; j < originalProcesses.Length; j++)
                {
                    Process p = originalProcesses[j];
                    List<int> clonedConsumes = new List<int>(p.Consumes.Count);
                    List<int> clonedProduces = new List<int>(p.Produces.Count);
                    foreach (int consume in p.Consumes)
                    {
                        int producingPip;
                        if (m_graph.OutputArtifactToProducingPip.TryGetValue(consume, out producingPip))
                        {
                            Process process = m_graph.Pips[producingPip] as Process;
                            WriteFile writeFile = m_graph.Pips[producingPip] as WriteFile;
                            if (process != null || writeFile != null)
                            {
                                // This is an output file created by a pip that will also be cloned. We need to translate it to the new path
                                File f;
                                if (!m_graph.Files.TryGetValue(consume, out f))
                                {
                                    throw new MimicGeneratorException("Failed to find referenced file with id: {0}", consume);
                                }

                                clonedConsumes.Add(m_graph.DuplicateFile(f, newRoot));
                                continue;
                            }
                        }

                        // If the path isn't translated based on cloning, just consume it directly.
                        clonedConsumes.Add(consume);
                    }

                    foreach (int produce in p.Produces)
                    {
                        File f;
                        if (!m_graph.Files.TryGetValue(produce, out f))
                        {
                            throw new MimicGeneratorException("Failed to find referenced file with id: {0}", produce);
                        }

                        clonedProduces.Add(m_graph.DuplicateFile(f, newRoot));
                    }

                    Process cloned = new Process(0, string.Empty, BuildGraph.DuplicatePath(GetSpecName(p.Spec, j, maxPipsPerSpec), newRoot), clonedProduces, clonedConsumes, p.Semaphores);
                    int newPipId = m_graph.AddWithNewPipId(cloned, p.PipId);

                    // Need to register all of the outputs of the cloned pip so consumers reference it as an output
                    // rather than a source file when specs are generated
                    foreach (var file in clonedProduces)
                    {
                        m_graph.OutputArtifactToProducingPip.TryAdd(file, newPipId);
                    }
                }

                for (int j = 0; j < originalWriteFiles.Length; j++)
                {
                    WriteFile wf = originalWriteFiles[j];
                    File f;
                    if (!m_graph.Files.TryGetValue(wf.Destination, out f))
                    {
                        throw new MimicGeneratorException("Failed to find referenced file with id: {0}", wf.Destination);
                    }

                    int clonedDestination = m_graph.DuplicateFile(f, newRoot);
                    WriteFile cloned = new WriteFile(0, string.Empty, BuildGraph.DuplicatePath(GetSpecName(wf.Spec, j, maxPipsPerSpec), newRoot), clonedDestination);
                    int newPipId = m_graph.AddWithNewPipId(cloned, wf.PipId);
                    m_graph.OutputArtifactToProducingPip.TryAdd(clonedDestination, newPipId);
                }
            }

            // TODO: Mirror CopyFile pips
        }

        private static string GetSpecName(string specName, int processPipCount, int maxPipsPerSpec)
        {
            int clone = processPipCount / maxPipsPerSpec;
            if (clone > 0)
            {
                string specWithoutExtension = Path.GetFileNameWithoutExtension(specName) + clone;
                string newPath = Path.ChangeExtension(Path.Combine(Path.GetDirectoryName(specName), specWithoutExtension), Path.GetExtension(specWithoutExtension));
                return newPath;
            }

            return specName;
        }
    }
}
