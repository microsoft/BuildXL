// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// Disposable container for directories that can be optionally deleted on dispose.
    /// </summary>
    public sealed class DirectoryRedirectionTracker : IDisposable
    {
        /// <summary>
        /// Set of directory junctions or symlinks to delete on dispose
        /// </summary>
        private readonly List<string> m_directoryLinks = new();

        private readonly LoggingContext m_loggingContext;

        /// <nodoc />
        public DirectoryRedirectionTracker(LoggingContext loggingContext) => m_loggingContext = loggingContext;

        /// <summary>
        /// Ensures that a redirected directory redirection exists.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if an I/O error prevents creating the redirected directory.</exception>
        public void CreateRedirection(string redirectedPath, string realPath, bool deleteOnDispose)
        {
            Contract.Requires(!string.IsNullOrEmpty(redirectedPath));
            Contract.Requires(!string.IsNullOrEmpty(realPath));

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
#pragma warning disable EPC13 // ThrowIfFailure() correctly observes the result of the Possible
                    FileUtilities.TryCreateReparsePointIfTargetsDoNotMatch(redirectedPath, realPath, OperatingSystemHelper.IsWindowsOS ? ReparsePointType.Junction : ReparsePointType.DirectorySymlink, out _).ThrowIfFailure();
#pragma warning restore EPC13 

                    if (deleteOnDispose)
                    {
                        m_directoryLinks.Add(redirectedPath);
                    }
                        
                },
                ex => { throw new BuildXLException(I($"Failed to create the redirected directory '{redirectedPath}'"), ex); });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var directory in m_directoryLinks)
            {
                try
                {
                    FileUtilities.DeleteFile(directory, retryOnFailure: true);
                }
                catch (Exception e)
                {
                    // Log a warning if DeleteFile throws an exception
                    Tracing.Logger.Log.DirectoryRedirectionTrackerFailedDelete(
                        m_loggingContext,
                        directory,
                        e.ToString());
                }
            }

            m_directoryLinks.Clear();
        }
    }
}
