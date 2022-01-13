// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// #define FEATURE_ANYBUILD_PROCESS_REMOTING
#if FEATURE_ANYBUILD_PROCESS_REMOTING

using System.Threading.Tasks;
using AnyBuild;
using BuildXL.Processes.Remoting.AnyBuild;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Adapter for AnyBuild <see cref="IRemoteProcess"/>.
    /// </summary>
    internal class AnyBuildRemoteProcess : IRemoteProcessPip
    {
        private readonly IRemoteProcess m_remoteProcess;
        private readonly Task<IRemoteProcessPipResult> m_completion;

        public AnyBuildRemoteProcess(IRemoteProcess remoteProcess)
        {
            m_remoteProcess = remoteProcess;
            m_completion = GetCompletionAsync(remoteProcess);
        }

        /// <inheritdoc/>
        public Task<IRemoteProcessPipResult> Completion => m_completion;

        /// <inheritdoc/>
        public void Dispose() => m_remoteProcess.Dispose();

        private static async Task<IRemoteProcessPipResult> GetCompletionAsync(IRemoteProcess remoteProcess)
        {
            IRemoteProcessResult anyBuildResult = await remoteProcess.Completion;
            return AnyBuildRemoteProcessPipResult.FromAnyBuildResult(anyBuildResult);
        }
    }
}

#endif