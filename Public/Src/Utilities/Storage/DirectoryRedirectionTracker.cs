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

            Possible<Unit> tryCreateRedirect() => FileUtilities.TryCreateReparsePointIfTargetsDoNotMatch(
                redirectedPath,
                realPath,
                OperatingSystemHelper.IsWindowsOS ? ReparsePointType.Junction : ReparsePointType.DirectorySymlink,
                out _);

            if (!tryCreateRedirect().Succeeded)
            {
                // Apply fallback logic before retrying
                HandleCreateRedirectionFailure(redirectedPath);
                var secondTry = tryCreateRedirect();
                if (!secondTry.Succeeded)
                {
                    throw new BuildXLException(I($"Failed to create the redirected directory '{redirectedPath}'"), secondTry.Failure.CreateException());
                }
            }

            if (deleteOnDispose)
            {
                m_directoryLinks.Add(redirectedPath);
            }
        }

        /// <summary>
        /// Provide a fallback for issues with CreateRedirection.
        /// </summary>
        private static void HandleCreateRedirectionFailure(string redirectedPath)
        {
            var timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // BXL fails to create redirection if directories retain content from previous builds.
            // TryCreateReparsePointIfTargetsDoNotMatch() handles reparse points, but if a non-empty directory exists (not a reparse point),
            // we rename it with a timestamp (e.g., BuildXLCurrentLog_moved_20240909_0000000) to preserve its contents. This avoids deleting files
            // of unknown origin. The original directory is then recreated for the current build logs.
            var directoryInfo = new DirectoryInfo(redirectedPath);

            try
            {
                if (Directory.Exists(redirectedPath) && ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint))
                {
                    var renamedDir = redirectedPath + "_moved_" + timeStamp;
                    Directory.Move(redirectedPath, renamedDir);
                }
            }
            catch (Exception ex)
            {
                throw new BuildXLException(I($"Failed to move the directory from '{redirectedPath}' to '{redirectedPath + "_moved_" + timeStamp}'"), ex);
            }
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
