// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes.Remoting;

namespace ExternalToolTest.BuildXL.Scheduler
{
    internal class TestRemoteProcessManager : IRemoteProcessManager
    {
        private readonly bool m_shouldRunLocally;

        public bool IsInitialized { get; private set; }

        public TestRemoteProcessManager(bool shouldRunLocally)
        {
            m_shouldRunLocally = shouldRunLocally;
        }

        public async Task<IRemoteProcessPip> CreateAndStartAsync(RemoteProcessInfo processInfo, CancellationToken cancellationToken)
        {
            var rp = new TestRemoteProcess(processInfo, cancellationToken, m_shouldRunLocally);
            await rp.StartAsync();
            return rp;
        }

        public void Dispose()
        {
        }

        public Task InitAsync()
        {
            IsInitialized = true;
            return Task.CompletedTask;
        }
    }
}
