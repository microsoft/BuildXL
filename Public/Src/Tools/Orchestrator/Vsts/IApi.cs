// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BuildXL.Orchestrator.Vsts
{
    /// <summary>
    /// Defines the interactions with the VSTS API
    /// </summary>
    public interface IApi
    {
        /// <summary>
        /// VSTS BuildId. This is unique per VSTS account
        /// </summary>
        string BuildId { get; }

        /// <summary>
        /// Team project that the build definition belongs to
        /// </summary>
        string TeamProject { get; }

        /// <summary>
        /// The id of the team project the build definition belongs to
        /// </summary>
        string TeamProjectId { get; }

        /// <summary>
        /// Uri of the VSTS server that kicked off the build
        /// </summary>
        string ServerUri { get; }

        /// <summary>
        /// PAT token used to authenticate with VSTS
        /// </summary>
        string AccessToken { get; }

        /// <summary>
        /// Used to uniquely identify a VSTS Agent in each phase. Each Agent has consecutive number starting from 1.
        /// </summary>
        int JobPositionInPhase { get; }

        /// <summary>
        /// The total number of agents being requested to run the build in the given phase
        /// </summary>
        int TotalJobsInPhase { get; }

        /// <summary>
        /// Name of the Agent running the build
        /// </summary>
        string AgentName { get; }

        /// <summary>
        /// Folder where the sources are being built from
        /// </summary>
        string SourcesDirectory { get; }

        /// <summary>
        /// Id of the timeline of the build
        /// </summary>
        string TimelineId { get; }

        /// <summary>
        /// Id of the plan of the build
        /// </summary>
        string PlanId { get; }

        /// <summary>
        /// Url of the build repository
        /// </summary>
        string RepositoryUrl { get; }

        /// <summary>
        /// Get the address information of all the agents participating as workers
        /// </summary>
        /// <returns>Address information of all agents participating as workers, consisting of hostname and IP address entries</returns>
        Task<IEnumerable<IDictionary<string, string>>> GetWorkerAddressInformationAsync();

        /// <summary>
        /// Get the address information of the master
        /// </summary>
        /// <returns>Address information of the master, consisting of hostname and IP address entries</returns>
        Task<IEnumerable<IDictionary<string, string>>> GetMasterAddressInformationAsync();

        /// <summary>
        /// Get the starting time of the build according to VSTS
        /// </summary>
        /// <returns>Time when the build started</returns>
        Task<DateTime> GetBuildStartTimeAsync();

        /// <summary>
        /// Indicate that this machine is ready to build using a timeline record
        /// </summary>
        /// <returns></returns>
        Task SetMachineReadyToBuild(string hostName, string ipV4Address, bool isMaster = false);

        /// <summary>
        /// Wait until all the other workers are ready
        /// </summary>
        /// <returns></returns>
        Task WaitForOtherWorkersToBeReady();

        /// <summary>
        /// Wait until the master is ready
        /// </summary>
        /// <returns></returns>
        Task WaitForMasterToBeReady();
    }
}
