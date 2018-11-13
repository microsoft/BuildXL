// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Interface for <see cref="SandboxedKextConnection"/> used to establish a sandbox kernel extension connection
    /// and manage communication between BuildXL and the macOS kernel extension.
    /// </summary>
    public interface ISandboxedKextConnection : IDisposable
    {
        /// <summary>
        /// Indicates how many connections are available to the sandbox kernel extension for processing file access reports
        /// </summary>
        int NumberOfKextConnections { get; }

        /// <summary>
        /// Notifies the kernel extension that a new pip is about to start. Since the kernel extension expects to receive the
        /// process ID of the pip, this method requires that the supplied <paramref name="process"/> has already been started,
        /// and hence already has an ID assigned to it. To ensure that the process is not going to request file accesses before the
        /// kernel extension is notified about it being started, the process should be started in some kind of suspended mode, and
        /// resumed only after the kernel extension has been notified.
        /// </summary>
        bool NotifyKextPipStarted(FileAccessManifest fam, SandboxedProcessMacKext process);

        /// <summary>
        /// Notifies the sandbox kernel extension that <paramref name="process"/> is done processing access reports
        /// for Pip <paramref name="pipId"/> so that resources can be freed up.
        /// Returns whether the sandbox kernel extension was successfully notified and cleaned up all resources
        /// for the pip with <paramref name="pipId"/>d.
        /// </summary>
        bool NotifyKextProcessFinished(long pipId, SandboxedProcessMacKext process);

        /// <summary>
        /// Releases all resources held by the sandbox kernel extension connection including all unmanaged references too. This is only for unit testing and should not
        /// be called directly at any time! Unit tests need this as they reference a static sandbox kernel extension connection instance that is torn down on process exit.
        /// This is done to not overburden the host system and kernel extension with connection spam.
        /// </summary>
        void ReleaseResources();
    }
}
