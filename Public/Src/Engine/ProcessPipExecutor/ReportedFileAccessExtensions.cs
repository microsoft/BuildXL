// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Logic to provide default file locations for stdout, stderr, and a trace file
    /// </summary>
    public static class ReportedFileAccessExtensions
    {
        /// <summary>
        /// Checks if this is a special device type of path for which we should not report a warning.
        /// Make it a verbose message, so it appears in the log (for diagnosability if there are problems with such access).
        /// </summary>
        /// <returns>true if the Path reperesents a special path. Otherwise false.</returns>
        private static bool IsSpecialDevicePath(string path)
        {
            if (!OperatingSystemHelper.IsLinuxOS && path != null)
            {
                // Add more special device paths here if needed.
                if (path.StartsWith("\\\\.\\pipe", OperatingSystemHelper.PathComparison))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the path contains wildcard characters, for which we should not report a warning.
        /// Make it a verbose message, so it appears in the log (for diagnosability if there are problems with such access).
        /// </summary>
        /// <returns>true if the Path contains wildcard characters. Otherwise false.</returns>
        /// <remarks>
        /// We can get access message to such file if an app is probbing fot existence of files with wildcard characters.
        /// </remarks>
        public static bool DoesPathContainsWildcards(string path)
        {
            if (path != null)
            {
#if !NETCOREAPP
                // Get the last part of the file name
                int lastSlash = path.LastIndexOf('\\');
                string lastComponent = lastSlash != -1 ? path.Substring(lastSlash) : path;
                if (lastComponent != null)
                {
                    // Add more special device paths here if needed.
                    if (lastComponent.Contains("?") || lastComponent.Contains("*"))
                    {
                        return true;
                    }
                }
#else
                ReadOnlySpan<char> filename = Path.GetFileName(path.AsSpan());
                return filename.Contains('?') || filename.Contains('*');
#endif
            }

            return false;
        }

        /// <summary>
        /// Attempts to parse the full path accessed to an <see cref="AbsolutePath"/>.
        /// When this succeeds, the returned path is equivalent to <see cref="ReportedFileAccess.GetPath"/>.
        /// In the event of parse failure that is not attributable to <c>ERROR_INVALID_NAME</c>,
        /// an event is logged to attribute the unknown path to the reporting <paramref name="pip"/>.
        /// </summary>
        public static bool TryParseAbsolutePath(this ReportedFileAccess access, PipExecutionContext context, LoggingContext loggingContext, Process pip, out AbsolutePath parsedPath)
        {
            Contract.Requires(context != null);
            Contract.Requires(pip != null);

            const int ErrorInvalidName = 0x7B; // ERROR_INVALID_NAME

            if (access.Path == null)
            {
                parsedPath = access.ManifestPath;
                return parsedPath.IsValid;
            }
            else
            {
                // Here we try to parse the path, but may fail gracefully. Sometimes tools try to open invalid paths.
                // For example, 'for /R dir %f in (*) do echo %f' in cmd may have GetFileAttributesEx("dir\*") called.
                bool parsed = AbsolutePath.TryCreate(context.PathTable, access.Path, out parsedPath);
                if (!parsed)
                {
                    if (access.Error != ErrorInvalidName)
                    {
                        // If this is opening a special (device type) path, just report it as a verbose message, so we don't lose it completely.
                        if (IsSpecialDevicePath(access.Path))
                        {
                            Tracing.Logger.Log.PipProcessIgnoringPathOfSpecialDeviceFileAccess(
                                loggingContext,
                                pip.SemiStableHash,
                                pip.GetDescription(context),
                                access.Describe(),
                                access.Path);
                        }
                        else if (DoesPathContainsWildcards(access.Path))
                        {
                            Tracing.Logger.Log.PipProcessIgnoringPathWithWildcardsFileAccess(
                                loggingContext,
                                pip.SemiStableHash,
                                pip.GetDescription(context),
                                access.Describe(),
                                access.Path);
                        }
                        else
                        {
                            Tracing.Logger.Log.PipProcessFailedToParsePathOfFileAccess(
                                loggingContext,
                                pip.SemiStableHash,
                                pip.GetDescription(context),
                                access.Describe(),
                                access.Path);
                        }
                    }
                }

                return parsed;
            }
        }
    }
}
