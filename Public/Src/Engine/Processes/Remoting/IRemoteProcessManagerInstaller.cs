// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Interface for installer of remote process manager.
    /// </summary>
    public interface IRemoteProcessManagerInstaller
    {
        /// <summary>
        /// Installs remote process manager.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="forceInstall">Force installation by cleaning existing installation when set to true.</param>
        /// <returns>True if installation was successful.</returns>
        Task<bool> InstallAsync(CancellationToken token, bool forceInstall = false);
    }
}
