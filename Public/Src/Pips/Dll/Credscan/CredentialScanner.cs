// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tracing;
using Logger = BuildXL.Pips.Tracing.Logger;
using BuildXL.Utilities.Configuration;
using BuildXL.Tracing;
using BuildXL.Utilities.Collections;
#if (MICROSOFT_INTERNAL && NETCOREAPP)
using Microsoft.Security.CredScan.ClientLib;
using Microsoft.Security.CredScan.KnowledgeBase.Client;
#endif

namespace BuildXL.Pips.Builders
{
#if (MICROSOFT_INTERNAL && NETCOREAPP)
    /// <summary>
    /// This class is used to detect credentials in environment variables.
    /// </summary>
    /// <remarks>
    /// For now the implementation of the methods in the class are not yet finalized until sufficient information is collected from logging of the warnings on detection of credentials.
    /// </remarks>
    public sealed class CredentialScanner : IBuildXLCredentialScanner
    {
        /// <summary>
        /// The dictionary is used as a cache to store the envvar(key, value), this is used to avoid scanning of the same env var multiple times.
        /// </summary>
        private ConcurrentBigSet<EnvironmentVariable> m_scannedEnvVars = new ConcurrentBigSet<EnvironmentVariable>();

        /// <summary>
        /// Concurrent bag to store a list of environment variables whose values are credentials and the associated pip info which is later used for logging.
        /// </summary>
        private readonly ConcurrentBag<(string envVarKey, Process process)> m_envVarsWithCredentials = new ConcurrentBag<(string, Process)>();
        private readonly CounterCollection<CredScanCounter> m_counters = new CounterCollection<CredScanCounter>();
        private readonly LoggingContext m_loggingContext;
        private readonly PipFragmentRenderer m_renderer;
        private readonly ActionBlockSlim<(string kvp, Process process)> m_credScanActionBlock;

        /// <summary>
        /// This list of user defined environment variables which are to ignored by the CredScan library.
        /// </summary>
        private readonly IReadOnlyList<string> m_credScanEnvironmentVariablesAllowList;

        private readonly ICredentialScanner m_credScan;

        /// <nodoc/>
        public CredentialScanner(PathTable pathTable, LoggingContext loggingContext, IReadOnlyList<string> credScanEnvironmentVariablesAllowList = null)
        {
            m_loggingContext = loggingContext;
            m_credScanEnvironmentVariablesAllowList = credScanEnvironmentVariablesAllowList;
            m_credScanActionBlock = ActionBlockSlim.CreateWithAsyncAction<(string, Process)>(
                      degreeOfParallelism: Environment.ProcessorCount,
                      processItemAction: ScanForCredentialsAsync);
            m_credScan = CredentialScannerFactory.Create();
            m_renderer = new PipFragmentRenderer(pathTable);
        }

        /// <summary>
        /// Method to send required envVars for CredentialScanning and logging of envVars with credentials.
        /// </summary>
        public void PostEnvVarsForProcessing(Process process, ReadOnlyArray<EnvironmentVariable> environmentVariables)
        {
            // There are two possible implementations to handle the scenario when a credential is detected in the env var.
            // 1.) The env var is set as a PassthroughEnvVariable, SetPassThroughEnvironmentVariable method is used for that.
            // 2.) An error is thrown to break the build and the user is suggested to pass that env var using the /credScanEnvironmentVariablesAllowList flag to ensure that the variable is not scanned for credentials.
            // The results of credscan are being logged. Based on the results obtained from telemetry one of the above two implementations is opted.
            // Adding a stopwatch here to measure the performance of the credscan implementation.
            using (m_counters.StartStopwatch(CredScanCounter.PostDuration))
            {
                foreach (var env in environmentVariables)
                {
                    if (!env.Value.IsValid || !m_scannedEnvVars.Add(env))
                    {
                        // We already queued this item for processing.
                        m_counters.IncrementCounter(CredScanCounter.NumSkipped);
                        continue;
                    }

                    string envVarKey = m_renderer.Render(env.Name);
                    if (m_credScanEnvironmentVariablesAllowList?.Contains(envVarKey) == true)
                    {
                        continue;
                    }

                    string value = env.Value.ToString(m_renderer);
                    m_counters.IncrementCounter(CredScanCounter.NumProcessed);

                    // Converting the env variable into the below pattern.
                    // Ex: string input = "password = Cr3d5c@n_D3m0_P@55w0rd";
                    // The above example is one of the suggested patterns to represent the input string which is to be passed to the CredScan method.
                    m_credScanActionBlock.Post(($"{envVarKey} = {value}", process));
                }
            }
        }

        /// <summary>
        /// This method is used to scan env variables for credentials.
        /// </summary>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        private async Task ScanForCredentialsAsync((string envVar, Process process) item)
        {
            using (m_counters.StartStopwatch(CredScanCounter.ScanDuration))
            {
                m_counters.IncrementCounter(CredScanCounter.NumScanCalls);

                var results = await m_credScan.ScanAsync(item.envVar);
                foreach (var result in results)
                {
                    string kvp = result.Match.MatchPrefix;
                    string key = kvp.Split('=')[0];
                    m_envVarsWithCredentials.Add((key, item.process));
                }
            }
        }
#pragma warning restore 1998

        /// <summary>
        /// Wait for the completion of the action block and log the detected credentials.
        /// </summary>
        /// <returns> Returns true when there are no credentials detected.</returns>
        public bool Complete(PipExecutionContext context)
        {
            using (m_counters.StartStopwatch(CredScanCounter.CompleteDuration))
            {
                m_credScanActionBlock.Complete();
                int credScanCompletionWaitTimeInMs = 60000;
                if (!m_credScanActionBlock.Completion.Wait(credScanCompletionWaitTimeInMs, context.CancellationToken))
                {
                    Logger.Log.CredScanFailedToCompleteInfo(m_loggingContext, credScanCompletionWaitTimeInMs);
                }
            }

            m_counters.LogAsStatistics("CredScan", m_loggingContext);

            // Clear reference to scannedEnvVars due to the huge size in order to help GC
            m_scannedEnvVars = null;

            if (m_envVarsWithCredentials.Count > 0)
            {
                foreach (var tuple in m_envVarsWithCredentials)
                {
                   Logger.Log.CredScanDetection(m_loggingContext, tuple.process.GetDescription(context), tuple.envVarKey);
                }
                return false;
            }
            return true;
        }
    }
#endif
}
