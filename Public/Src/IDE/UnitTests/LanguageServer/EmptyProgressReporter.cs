// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Ide.JsonRpc;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    internal sealed class EmptyProgressReporter : IProgressReporter
    {
        public void ReportWorkspaceInit()
        {
        }

        public void ReportWorkspaceInProgress(WorkspaceLoadingParams workspcaeLoading)
        {
        }

        public void ReportWorkspaceFailure(string logFileName, Action openLogFileAction)
        {
        }

        public void ReportWorkspaceSuccess()
        {
        }

        public void ReportFindReferencesProgress(FindReferenceProgressParams findReferenceProgress)
        {
        }
    }
}
