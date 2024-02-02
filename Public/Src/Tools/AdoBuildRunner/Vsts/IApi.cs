// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Build;

#nullable enable

namespace BuildXL.AdoBuildRunner.Vsts
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
        /// Gets the build context from the ADO build run information
        /// </summary>
        Task<BuildContext> GetBuildContextAsync(string buildKey);

        /// <summary>
        /// Wait until the orchestrator is ready and return its address
        /// </summary>
        /// <returns></returns>
        Task<BuildInfo> WaitForBuildInfo(BuildContext buildContext);
       
        /// <summary>
        /// Publish the orchestrator address
        /// </summary>
        /// <returns></returns>
        Task PublishBuildInfo(BuildContext buildContext, BuildInfo buildInfo);
    }
}
