// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
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
        public void CreateRedirection(string redirectedPath, string realPath, bool deleteExisting, bool deleteOnDispose)
        {
            Contract.Requires(!string.IsNullOrEmpty(redirectedPath));
            Contract.Requires(!string.IsNullOrEmpty(realPath));

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    var probeRedirectedPath = FileUtilities.TryProbePathExistence(redirectedPath, false);
                    if (!probeRedirectedPath.Succeeded)
                    {
                        throw probeRedirectedPath.Failure.Throw();
                    }

                    if (deleteExisting)
                    {
                        if (probeRedirectedPath.Result == PathExistence.ExistsAsFile)
                        {
                            if (!OperatingSystemHelper.IsUnixOS)
                            {
                                if (!FileUtilities.TryRemoveDirectory(redirectedPath, out var hr))
                                {
                                    throw new NativeWin32Exception(hr);
                                }
                            }
                            else
                            {
                                FileUtilities.DeleteFile(redirectedPath, retryOnFailure: true);
                            }
                        }
                        else if (probeRedirectedPath.Result == PathExistence.ExistsAsDirectory)
                        {
                            FileUtilities.DeleteDirectoryContents(redirectedPath, deleteRootDirectory: true);
                        }
                    }

                    Possible<Unit> maybeCreated = Unit.Void;

                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        // On some Windows OS, creating directory symlinks is not allowed, so we create a junction.
                        Directory.CreateDirectory(redirectedPath);
                        FileUtilities.CreateJunction(redirectedPath, realPath);
                    }
                    else
                    {
                        maybeCreated = FileUtilities.TryCreateSymbolicLink(redirectedPath, realPath, isTargetFile: false);
                    }

                    if (!maybeCreated.Succeeded)
                    {
                        throw maybeCreated.Failure.Throw();
                    }

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
