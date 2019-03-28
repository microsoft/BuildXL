// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Processes;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Describes a file access reported by BuildXL during the build.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    public sealed class ReportedFileAccessDescriptor
    {
        #region Public properties

        /// <summary>
        /// The pip that launched the process that performed the file access.
        /// </summary>
        public PipDescriptor Pip { get; set; }

        /// <summary>
        /// Data structure the describes the reported file access.
        /// </summary>
        public ReportedFileAccess ReportedFileAccess { get; set; }
        #endregion

        #region Internal properties

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="pip">The pip that executed that process that reported the file access</param>
        /// <param name="reportedFileAccesse">Reported file access descriptor</param>
        /// <param name="pathTable">Path table used to expand path strings</param>
        internal ReportedFileAccessDescriptor(PipDescriptor pip, ref ReportedFileAccess reportedFileAccess, BuildXL.Utilities.PathTable pathTable)
        {
            Pip = pip;
            ReportedFileAccess = new ReportedFileAccess(
                                                        operation: reportedFileAccess.Operation,
                                                        process: reportedFileAccess.Process,
                                                        requestedAccess: reportedFileAccess.RequestedAccess,
                                                        status: reportedFileAccess.Status,
                                                        explicitlyReported: reportedFileAccess.ExplicitlyReported,
                                                        error: reportedFileAccess.Error,
                                                        usn: reportedFileAccess.Usn,
                                                        desiredAccess: reportedFileAccess.DesiredAccess,
                                                        shareMode: reportedFileAccess.ShareMode,
                                                        creationDisposition: reportedFileAccess.CreationDisposition,
                                                        flagsAndAttributes: reportedFileAccess.FlagsAndAttributes,
                                                        manifestPath: reportedFileAccess.ManifestPath,
                                                        path: reportedFileAccess.GetPath(pathTable),
                                                        enumeratePatttern: reportedFileAccess.EnumeratePattern);
        }
        #endregion
    }
}
