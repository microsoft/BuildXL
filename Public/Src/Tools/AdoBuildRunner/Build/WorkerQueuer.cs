// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// Queues a worker pipeline specifying 
    ///     (1) user-provided parameters and
    ///     (2) the BuildXL build information in the form of triggerInfo
    /// </summary>
    public class WorkerQueuer
    {
        private readonly IApi m_api;
        private readonly BuildContext m_buildContext;
        private readonly ILogger m_logger;

        /// <nodoc />
        public WorkerQueuer(BuildContext context, ILogger logger, IApi api)
        {
            m_api = api;
            m_buildContext = context;
            m_logger = logger;
        }

        /// <summary>
        /// Queue a build on the worker pipeline. We include any extra parameters provided by the user, and also a payload
        /// for the worker in the form of triggerInfo key-value pairs
        /// </summary>
        /// <param name="pipelineId">The id of the pipeline that will be triggered</param>
        /// <param name="args">Queueing parameters: either template parameters (/param:K=V) or queue-time variables (/var:K=V). Unrecognized options are ignored</param>
        public Task QueueWorkerPipelineAsync(int pipelineId, string[] args)
        {
            // Parse the rest of the arguments looking for the extra parameters
            // TODO: Better argument parsing. This is pretty awful but it should do for now:
            // we just ignore malformed arguments and pay attention only to the ones of the form
            // /param:K=V
            var templateParams = new Dictionary<string, string>();
            var queueTimeVars = new Dictionary<string, string>();
            foreach (var arg in args)
            {
                const string ParamOpt = "/param:";
                const string VarOpt = "/var:";
                if (!TryExtractKeyValuePair(ParamOpt, arg, templateParams) && !TryExtractKeyValuePair(VarOpt, arg, queueTimeVars))
                {
                    m_logger.Warning($"Ignoring unrecognized argument {arg}");
                }
            }

            var ip = BuildManager.GetAgentIPAddress(ipv6: false);

            // Include information for the worker pipeline in the trigger info
            var triggerInfo = new Dictionary<string, string>()
            {
                [Constants.OrchestratorLocationParameter] = $"{ip}:{Constants.MachineGrpcPort}",
                [Constants.RelatedSessionIdParameter] = m_buildContext.RelatedSessionId,
                [Constants.TriggeringAdoBuildIdParameter] = m_api.BuildId,
            };

            var sourceBranch = Environment.GetEnvironmentVariable(Constants.SourceBranchVariableName)!;
            var sourceVersion = Environment.GetEnvironmentVariable(Constants.SourceVersionVariableName)!;

            return m_api.QueueBuildAsync(pipelineId, sourceBranch, sourceVersion, queueTimeVars, templateParams, triggerInfo);
        }

        private bool TryExtractKeyValuePair(string optName, string arg, Dictionary<string, string> targetDict)
        {
            var trimmed = arg.Trim();
            if (trimmed.StartsWith(optName))
            {
                var kvp = trimmed.Substring(optName.Length).Split("=", 2);
                if (kvp.Length == 2)
                {
                    targetDict[kvp[0]] = kvp[1];
                    return true;
                }
            }

            return false;
        }
    }
}