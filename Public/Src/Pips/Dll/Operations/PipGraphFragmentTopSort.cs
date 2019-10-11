// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Class for sorting pips in pip graph fragment according to its topological order.
    /// </summary>
    public sealed class PipGraphFragmentTopSort
    {
        private readonly IPipGraph m_pipGraph;

        /// <summary>
        /// When true, ensure that pips should be in a similar order to how they were originally inserted into the graph.
        /// </summary>
        public bool PreservePipOrder { get; set; } = true;

        /// <summary>
        /// Create an instance of <see cref="PipGraphFragmentTopSort"/>/
        /// </summary>
        /// <param name="pipGraph">Pip graph.</param>
        public PipGraphFragmentTopSort(IPipGraph pipGraph)
        {
            Contract.Requires(pipGraph != null);
            m_pipGraph = pipGraph;
        }

        /// <summary>
        /// Topologically sort pips in the pip graph.
        /// </summary>
        public List<List<Pip>> Sort() => TopSort(m_pipGraph.RetrieveScheduledPips().ToList());


        private static List<List<Pip>> StableSortPips(List<Pip> pips, List<List<Pip>> finalPipList)
        {
            Dictionary<Pip, int> order = new Dictionary<Pip, int>();
            for (int i = 0; i < pips.Count; i++)
            {
                order[pips[i]] = i;
            }

            finalPipList = finalPipList.Select(pipGroup => pipGroup.OrderBy(pip => order[pip]).ToList()).ToList();
            return finalPipList;
        }

        private List<List<Pip>> TopSort(List<Pip> pips)
        {
            List<List<Pip>> sortedPipGroups = new List<List<Pip>>();
            List<Pip> modules = new List<Pip>();
            List<Pip> specs = new List<Pip>();
            List<Pip> values = new List<Pip>();

            // SpecialIpcPips are service-shutdown process pip, ipc drop finalization, service-start process pip, drop create ipc pip
            List<Pip> specialIpcPips = new List<Pip>();
            List<Pip> ipcPips = new List<Pip>();
            List<Pip> otherPips = new List<Pip>();

            // The first IPC pip after the finalization pip is a create pip, and needs to be before the other ipc pips
            // There is not currently a way to identify the create pip short of guessing from the command line.
            HashSet<StringId> foundIpcCreatePips = new HashSet<StringId>();
            foreach (var pip in pips)
            {
                if (pip is ModulePip)
                {
                    modules.Add(pip);
                }
                else if (pip is SpecFilePip)
                {
                    specs.Add(pip);
                }
                else if (pip is ValuePip)
                {
                    values.Add(pip);
                }
                else if ((pip is Process && (((Process)pip).IsService || ((Process)pip).IsStartOrShutdownKind))
                    || (pip is IpcPip && ((IpcPip)pip).IsServiceFinalization))
                {
                    specialIpcPips.Add(pip);
                }
                else if (pip is IpcPip && !foundIpcCreatePips.Contains(((IpcPip)pip).IpcInfo.IpcMonikerId))
                {
                    specialIpcPips.Add(pip);
                    foundIpcCreatePips.Add(((IpcPip)pip).IpcInfo.IpcMonikerId);
                }
                else if (pip is IpcPip)
                {
                    ipcPips.Add(pip);
                }
                else
                {
                    otherPips.Add(pip);
                }
            }

            sortedPipGroups.Add(modules);
            sortedPipGroups.Add(specs);
            sortedPipGroups.Add(values);
            TopSortInternal(otherPips, sortedPipGroups);

            // Special IPC related pips must go in sequential order.
            sortedPipGroups.AddRange(specialIpcPips.Select(pip => new List<Pip>() { pip }));
            sortedPipGroups.Add(ipcPips);
            sortedPipGroups = StableSortPips(pips, sortedPipGroups);
            return sortedPipGroups;
        }

        private void TopSortInternal(List<Pip> pips, List<List<Pip>> sortedPipGroups)
        {
            Dictionary<Pip, int> childrenLeftToVisit = new Dictionary<Pip, int>();
            sortedPipGroups.Add(new List<Pip>());
            int totalAdded = 0;
            foreach (var pip in pips)
            {
                childrenLeftToVisit[pip] = 0;
            }

            foreach (var pip in pips)
            {
                foreach (var dependent in m_pipGraph.RetrievePipImmediateDependents(pip) ?? Enumerable.Empty<Pip>())
                {
                    childrenLeftToVisit[dependent]++;
                }
            }

            foreach (var pip in pips)
            {
                if (childrenLeftToVisit[pip] == 0)
                {
                    totalAdded++;
                    sortedPipGroups[sortedPipGroups.Count - 1].Add(pip);
                }
            }

            int currentLevel = sortedPipGroups.Count - 1;
            while (totalAdded < pips.Count)
            {
                sortedPipGroups.Add(new List<Pip>());
                foreach (var pip in sortedPipGroups[currentLevel])
                {
                    foreach (var dependent in m_pipGraph.RetrievePipImmediateDependents(pip) ?? Enumerable.Empty<Pip>())
                    {
                        if (--childrenLeftToVisit[dependent] == 0)
                        {
                            totalAdded++;
                            sortedPipGroups[currentLevel + 1].Add(dependent);
                        }
                    }
                }

                currentLevel++;
            }
        }
    }
}
