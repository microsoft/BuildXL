// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Explorer.Server.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using BuildXL.Execution.Analyzer;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("b")]
    public class BuildController : LoadController
    {
        public BuildController(IMemoryCache memoryCache, IHostingEnvironment env) 
            : base(memoryCache, env)
        {
        }

        [Route("{sessionId}/summary")]
        public BuildSummary Summary(string sessionId)
        {
            var invocation = GetInvocation(sessionId);
            var stats = GetStats(sessionId);

            var summary = new BuildSummary()
            {
                SessionId = sessionId,
                Kind = "local",
                State = GetState(stats),
                StartTime = invocation.BuildStartTimeUtc,
                Duration = stats.GetMiliSeconds("TimeToEngineRunCompleteMs") ?? TimeSpan.Zero,
                PipStats = new PipStats(stats),
            };

            return summary;
        }


        private BuildState GetState(BuildXLStats stats)
        {
            if (stats.GetBoolean("ErrorWasLogged"))
            {
                return BuildState.Failed;
            }

            if (stats.GetBoolean("WarningWasLogged"))
            {
                return BuildState.PassedWithWarnings;
            }

            if (!stats.Contains("TimeToEngineRunCompleteMs"))
            {
                if (stats.Contains("TimeToFirstPipSyntheticMs"))
                {
                    return BuildState.Runningpips;
                }

                return BuildState.ConstructingGraph;
            }

            return BuildState.Passed;
        }
    }
}
