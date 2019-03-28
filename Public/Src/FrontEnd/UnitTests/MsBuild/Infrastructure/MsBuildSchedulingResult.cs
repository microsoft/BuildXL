// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.AbsolutePath>;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.MsBuild.Infrastructure
{
    /// <summary>
    /// The result of scheduling a set of <see cref="ProjectWithPredictions{TPathType}"/>
    /// </summary>
    public sealed class MsBuildSchedulingResult
    {
        private readonly PathTable m_pathTable;
        private readonly Dictionary<ProjectWithPredictions, (bool success, string failureDetail, Process process)> m_schedulingResult;

        /// <summary>
        /// The scheduled graph
        /// </summary>
        public IPipGraph PipGraph { get; }

        /// <nodoc/>
        internal MsBuildSchedulingResult(PathTable pathTable, IPipGraph pipGraph, Dictionary<ProjectWithPredictions, (bool, string, Process)> schedulingResult)
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
        public MsBuildSchedulingResult AssertSuccess()
        {
            foreach (var entry in m_schedulingResult)
            {
                XAssert.IsTrue(entry.Value.success, I($"Expected to schedule '{entry.Key.FullPath.ToString(m_pathTable)}' but scheduling failed. Details: {entry.Value.failureDetail}"));
            }

            return this;
        }

        /// <summary>
        /// Retrieves the successfully scheduled process corresponding to the specified project
        /// </summary>
        public Process RetrieveSuccessfulProcess(ProjectWithPredictions project)
        {
            if (!m_schedulingResult.TryGetValue(project, out var result))
            {
                XAssert.IsTrue(false, "Specified project did not run");
            }

            if (!result.success)
            {
                XAssert.IsTrue(false, "Specified project did not succeed. Failure: " + result.failureDetail);
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
        public IEnumerable<ProjectWithPredictions> AssertFailureAndRetrieveFailedProjects()
        {
            var result = m_schedulingResult.Where(kvp => !kvp.Value.success).Select(kvp => kvp.Key).ToList();

            XAssert.IsTrue(result.Count > 1, "Expected to find a failure, but all projects succeeded at scheduling");
            return result;
        }
    }
}
