// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Remoting;
using BuildXL.Utilities;

namespace ExternalToolTest.BuildXL.Scheduler
{
    internal class TestRemoteProcessManager : IRemoteProcessManager
    {
        private readonly bool m_shouldRunLocally;

        public bool IsInitialized { get; private set; }

        public TestRemoteProcessManager(bool shouldRunLocally) => m_shouldRunLocally = shouldRunLocally;

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

        public IRemoteProcessManagerInstaller GetInstaller() => null;

        public void RegisterStaticDirectories(IEnumerable<AbsolutePath> staticDirectories)
        {
        }

        public Task<IEnumerable<AbsolutePath>> GetInputPredictionAsync(Process process) => Task.FromResult(Enumerable.Empty<AbsolutePath>());
    }
}
