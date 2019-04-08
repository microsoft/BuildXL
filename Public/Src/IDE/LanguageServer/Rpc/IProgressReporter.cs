// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Interface for reporting operation's progress like workspace construction or other long-running operation.
    /// </summary>
    public interface IProgressReporter
    {
        /// <summary>
        /// Report that initialization just began.
        /// </summary>
        void ReportWorkspaceInit();

        /// <summary>
        /// Report the progress of the workspace construction.
        /// </summary>
        void ReportWorkspaceInProgress(WorkspaceLoadingParams workspcaeLoading);

        /// <summary>
        /// Report that the workspace construction is failed.
        /// </summary>
        void ReportWorkspaceFailure(string logFileName, Action openLogFileAction);

        /// <summary>
        /// Report that the worksapce construction succeeded.
        /// </summary>
        void ReportWorkspaceSuccess();

        /// <summary>
        /// Report progress for find references operation.
        /// </summary>
        void ReportFindReferencesProgress(FindReferenceProgressParams findReferenceProgress);
    }
}
