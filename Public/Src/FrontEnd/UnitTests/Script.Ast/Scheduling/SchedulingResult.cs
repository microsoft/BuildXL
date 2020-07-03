// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.Scheduling
{
    /// <summary>
    /// The result of scheduling a set of projects
    /// </summary>
    public sealed class SchedulingResult<TProject>
    {
        private readonly Dictionary<TProject, (bool success, string failureDetail, Process process)> m_schedulingResult;

        /// <summary>
        /// The configuration that was used for scheduling
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// The scheduled graph
        /// </summary>
        public IMutablePipGraph PipGraph { get; }

        /// <nodoc/>
        internal SchedulingResult(IMutablePipGraph pipGraph, Dictionary<TProject, (bool, string, Process)> schedulingResult, IConfiguration configuration)
        {
            Contract.RequiresNotNull(schedulingResult);
            Contract.RequiresNotNull(configuration);

            PipGraph = pipGraph;
            m_schedulingResult = schedulingResult;
            Configuration = configuration;
        }

        /// <summary>
        /// Asserts that all scheduled projects suceeded
        /// </summary>
        public SchedulingResult<TProject> AssertSuccess()
        {
            foreach (var entry in m_schedulingResult)
            {
                XAssert.IsTrue(entry.Value.success, I($"Expected to schedule '{entry.Key}' but scheduling failed. Details: {entry.Value.failureDetail}"));
            }

            return this;
        }

        /// <summary>
        /// Retrieves the successfully scheduled process corresponding to the specified project
        /// </summary>
        public Process RetrieveSuccessfulProcess(TProject project)
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
        public IEnumerable<TProject> AssertFailureAndRetrieveFailedProjects()
        {
            var result = m_schedulingResult.Where(kvp => !kvp.Value.success).Select(kvp => kvp.Key).ToList();

            XAssert.IsTrue(result.Count > 1, "Expected to find a failure, but all projects succeeded at scheduling");
            return result;
        }
    }
}
