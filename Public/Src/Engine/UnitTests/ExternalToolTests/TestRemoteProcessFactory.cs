// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes.Remoting;

namespace ExternalToolTest.BuildXL.Scheduler
{
    /// <summary>
    /// Remote process factory for testing.
    /// </summary>
    internal class TestRemoteProcessFactory : IRemoteProcessFactory
    {
        private readonly bool m_shouldRunLocally;

        /// <summary>
        /// Creates an instance of <see cref="TestRemoteProcess"/>.
        /// </summary>
        public TestRemoteProcessFactory(bool shouldRunLocally = false) => m_shouldRunLocally = shouldRunLocally;

        public async Task<IRemoteProcess> CreateAndStartAsync(RemoteCommandExecutionInfo remoteCommandInfo, CancellationToken cancellationToken)
        {
            var rp = new TestRemoteProcess(remoteCommandInfo, cancellationToken, m_shouldRunLocally);
            await rp.StartAsync();
            return rp;
        }
    }
}
