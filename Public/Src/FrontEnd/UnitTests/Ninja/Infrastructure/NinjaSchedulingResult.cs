// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Ninja.Infrastructure
{
    /// <summary>
    /// The result of scheduling a list of <see cref="NinjaNode"/>
    /// </summary>
    public sealed class NinjaSchedulingResult
    {
        private readonly PathTable m_pathTable;
        private readonly Dictionary<NinjaNode, (bool success, Process process)> m_schedulingResult;

        /// <summary>
        /// The scheduled graph
        /// </summary>
        public IPipGraph PipGraph { get; }

        /// <nodoc/>
        internal NinjaSchedulingResult(PathTable pathTable, IPipGraph pipGraph, Dictionary<NinjaNode, (bool, Process)> schedulingResult)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(schedulingResult != null);

            m_pathTable = pathTable;
            PipGraph = pipGraph;
            m_schedulingResult = schedulingResult;
        }

        /// <summary>
        /// Asserts that all scheduled projects suceeded
        /// </summary>
        public NinjaSchedulingResult AssertSuccess()
        {
            foreach (var entry in m_schedulingResult)
            {
                XAssert.IsTrue(entry.Value.success, I($"Expected to schedule '{entry.Key.Command}' but scheduling failed."));
            }

            return this;
        }

        /// <summary>
        /// Retrieves the successfully scheduled process corresponding to the specified project
        /// </summary>
        public Process RetrieveSuccessfulProcess(NinjaNode project)
        {
            if (!m_schedulingResult.TryGetValue(project, out var result))
            {
                XAssert.IsTrue(false, "Specified project did not run");
            }

            if (!result.success)
            {
                XAssert.IsTrue(false, "Specified project did not succeed");
            }

            return result.process;
        }

        /// <summary>
        /// Retrieves all processes that were scheduled
        /// </summary>
        public IEnumerable<Process> RetrieveAllProcesses()
        {
            return m_schedulingResult.Values.Select(kvp => kvp.process);
        }

        /// <summary>
        /// Asserts the occurrence of at least one scheduled failured and retrieves all the failed projects
        /// </summary>
        /// <returns></returns>
        public IEnumerable<NinjaNode> AssertFailureAndRetrieveFailedProjects()
        {
            var result = m_schedulingResult.Where(kvp => !kvp.Value.success).Select(kvp => kvp.Key).ToList();

            XAssert.IsTrue(result.Count > 1, "Expected to find a failure, but all projects succeeded at scheduling");
            return result;
        }
    }
}
