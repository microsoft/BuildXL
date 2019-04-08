// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using BuildXL.Execution.Analyzer;

namespace BuildXL.Explorer.Server.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class LoadController : ControllerBase
    {
        private const string LogNamePrefix = "BuildXL";

        protected IMemoryCache Cache;
        private IFileProvider m_fileProvider;
        private LoggingContext m_loggingContext = new LoggingContext("BuildXL.Explorer");

        public LoadController(IMemoryCache memoryCache, IHostingEnvironment env)
        {
            Cache = memoryCache;
            m_fileProvider = env.ContentRootFileProvider;
        }

        protected async Task<CachedGraph> GetCachedGraphAsync(string buildId)
        {
            var loggingContext = new LoggingContext("Website");
            var invocations = new Invocations().GetInvocations(loggingContext);
            var thisInvocation = invocations.First(invocation => invocation.SessionId == buildId);

            var entry = await Cache.GetOrCreateAsync(
                "CachedGraph: =" + buildId,
                (newEntry) => CachedGraph.LoadAsync(
                    Path.Combine(thisInvocation.LogsFolder, "BuildXL"),
                    loggingContext,
                    true
                ));

            return entry;
        }

        public AnalysisInput GetAnalysisInput(string xlgPath)
        {
            var entry = Cache.GetOrCreate(
                     "AnalysisInput:" + xlgPath,
                (newEntry) =>
                {
                    AnalysisInput analysisInput = new AnalysisInput();
                    analysisInput.ExecutionLogPath = xlgPath;
                    if (!analysisInput.LoadCacheGraph(null))
                    {
                        Console.Error.WriteLine("Could not load cached graph");
                    }
                    return analysisInput;
                });

            return entry;
        }

        protected string GetXlgPath(string buildId, string xlg)
        {
            var xlgPath = buildId == null ? xlg : "D:\\home\\site\\wwwroot\\wwwroot\\Logs\\" + buildId + "\\Logs";
            return xlgPath;
        }

        protected BuildXLStats GetStats(string sessionId)
        {
            var invocation = GetInvocation(sessionId);
            var filePath = Path.Combine(invocation.LogsFolder, LogNamePrefix + ".stats");

            return Cache.GetOrCreate(
                "build/" + sessionId + "/stats",
                newEntry =>
                {
                    if (!System.IO.File.Exists(filePath))
                    {
                        throw new ExplorerException("Stats file not found");
                    }

                    if (!BuildXLStats.TryLoadStatsFile(filePath, out var stats))
                    {
                        throw new ExplorerException("Error reading stats file");
                    }

                    newEntry.SlidingExpiration = TimeSpan.FromMinutes(30);
                    return stats;
                }
            );
        }

        protected Invocations.Invocation GetInvocation(string sessionId)
        {
            if (GetInvocations().TryGetValue(sessionId, out var invocation))
            {
                return invocation;
            }

            throw new InvalidOperationException("SessionId not found");
        }

        IReadOnlyDictionary<string, Invocations.Invocation> GetInvocations()
        {
            return Cache.GetOrCreate(
                "Builds.tsv",
                newEntry =>
                {
                    var buildXLInvocations = new Invocations();
                    newEntry.ExpirationTokens.Add(m_fileProvider.Watch(buildXLInvocations.GetBuildTsvFilePath()));
                    newEntry.SetSlidingExpiration(TimeSpan.FromMinutes(5));

                    var invocations = buildXLInvocations.GetInvocations(m_loggingContext);
                    var dictionary = new Dictionary<string, Invocations.Invocation>(invocations.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var invocation in invocations)
                    {
                        dictionary.Add(invocation.SessionId, invocation);
                    }

                    return dictionary;
                });
        }
    }
}
