// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Remoting process manager that throws an exception whenever there is a remoting attempt.
    /// </summary>
    public class NoRemotingRemoteProcessManager : IRemoteProcessManager
    {
        private const string RemotingNotSupportedMessage = "Process remoting is not supported";

        /// <inheritdoc/>
        public bool IsInitialized => false;

        /// <inheritdoc/>
        public Task<IRemoteProcessPip> CreateAndStartAsync(RemoteProcessInfo processInfo, CancellationToken cancellationToken) => throw new BuildXLException(RemotingNotSupportedMessage);

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        public IRemoteProcessManagerInstaller? GetInstaller() => null;

        /// <inheritdoc/>
        public Task InitAsync() => throw new BuildXLException(RemotingNotSupportedMessage);

        /// <inheritdoc/>
        public void RegisterStaticDirectories(IEnumerable<string> staticDirectories)
        {
        }
    }
}
