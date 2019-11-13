// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Analzyer used to analyze cached graph and execution log
    /// </summary>
    public abstract class ExecutionAnalyzerBase : ExecutionLogTargetBase
    {
        /// <summary>
        /// The loaded pip graph
        /// </summary>
        public PipGraph PipGraph { get; }

        /// <summary>
        /// The loaded directed graph
        /// </summary>
        public DirectedGraph DataflowGraph => PipGraph.DataflowGraph;

        /// <summary>
        /// The loaded pip table
        /// </summary>
        public PipTable PipTable => PipGraph.PipTable;

        /// <summary>
        /// The loaded path table
        /// </summary>
        public PathTable PathTable => PipGraph.Context.PathTable;

        /// <summary>
        /// The loaded string table
        /// </summary>
        public StringTable StringTable => PipGraph.Context.StringTable;

        /// <summary>
        /// The loaded symbol table
        /// </summary>
        public SymbolTable SymbolTable => PipGraph.Context.SymbolTable;

        /// <summary>
        /// The loaded qualifier table
        /// </summary>
        public QualifierTable QualifierTable => PipGraph.Context.QualifierTable;

        /// <nodoc />
        protected ExecutionAnalyzerBase(PipGraph pipGraph)
        {
            Contract.Requires(pipGraph != null);

            PipGraph = pipGraph;
        }

        /// <summary>
        /// Runs before execution events are supplied to the analyzer to prepare
        /// state based on the cached graph.
        /// </summary>
        public virtual void Prepare()
        {
        }

        /// <summary>
        /// Runs the analysis on the given cached graph after all execution events have
        /// been raised on the analyzer
        /// </summary>
        /// <returns>an exit code for the analysis</returns>
        public abstract int Analyze();

        #region Utility Methods

        /// <summary>
        /// Gets the pip for the pip id
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public Pip GetPip(PipId pipId)
        {
            return PipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
        }

        /// <summary>
        /// Gets the pip for the node id
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public Pip GetPip(NodeId nodeId)
        {
            return PipTable.HydratePip(nodeId.ToPipId(), PipQueryContext.ViewerAnalyzer);
        }

        /// <summary>
        /// Gets the description for the pip
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string GetDescription(Pip pip)
        {
            return pip.GetDescription(PipGraph.Context);
        }

        /// <summary>
        /// Renders process' command line to string.
        /// </summary>
        public string RenderProcessArguments(Process process) => RenderPipData(GetArgumentsDataFromProcess(process));

        /// <summary>
        /// Renders <see cref="PipData"/> to string.
        /// </summary>
        public string RenderPipData(PipData pipData)
        {
            var rootExpander = new RootExpander(PathTable);
            Func<AbsolutePath, string> expandRoot = absPath => PathTable.ExpandName(absPath.Value, rootExpander);
            return pipData.ToString(expandRoot, PathTable.StringTable, PipData.MaxMonikerRenderer);
        }

        /// <summary>
        /// Gets the command line arguments for the process.
        /// </summary>
        public PipData GetArgumentsDataFromProcess(Process process)
        {
            PipData arguments = process.Arguments;
            if (process.ResponseFile.IsValid)
            {
                var responseFileData = process.ResponseFileData;
                PipDataBuilder pipDataBuilder = new PipDataBuilder(StringTable);

                // Add all the arguments from the command line excluding the response file (the last fragment)
                foreach (var fragment in process.Arguments.Take(process.Arguments.FragmentCount - 1).Concat(responseFileData))
                {
                    Contract.Assume(fragment.FragmentType != PipFragmentType.Invalid);

                    pipDataBuilder.Add(fragment);
                }

                arguments = pipDataBuilder.ToPipData(arguments.FragmentSeparator, arguments.FragmentEscaping);
            }

            return arguments;
        }

        #endregion
    }
}
