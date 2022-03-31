// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Manages process remoting in BuildXL.
    /// </summary>
    public interface IRemoteProcessManager : IDisposable
    {
        /// <summary>
        /// Inits remoting process manager.
        /// </summary>
        /// <remarks>
        /// This method should be idempotent, i.e., the first call initializes the manager, and the subsequent calls should no-op.
        /// If the first call throws an exception, then so do the subsequent calls.
        /// </remarks>
        Task InitAsync();

        /// <summary>
        /// Creates and start process pip remotely.
        /// </summary>
        /// <param name="processInfo">Process pip info.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An instance of <see cref="IRemoteProcessPip"/>.</returns>
        Task<IRemoteProcessPip> CreateAndStartAsync(RemoteProcessInfo processInfo, CancellationToken cancellationToken);

        /// <summary>
        /// Checks if this process manager has been initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Returns an installer for this remote process manager.
        /// </summary>
        /// <returns>An instance of <see cref="IRemoteProcessManagerInstaller"/>.</returns>
        IRemoteProcessManagerInstaller? GetInstaller();

        /// <summary>
        /// Registers static directories to be sent to the remoting engine during initialization by <see cref="InitAsync"/>.
        /// </summary>
        /// <param name="staticDirectories">A list of static directories.</param>
        /// <remarks>
        /// Static directories are directories whose contents (including descendant directories) are assume to not change during a build session.
        /// Static directories can improve build performance because the enumeration results of such directories
        /// can be cached.
        /// 
        /// This method should be called before calling <see cref="InitAsync"/>.
        /// </remarks>
        void RegisterStaticDirectories(IEnumerable<string> staticDirectories);
    }
}
