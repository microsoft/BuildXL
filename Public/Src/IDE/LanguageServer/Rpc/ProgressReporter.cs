// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Ide.LanguageServer;
using JetBrains.Annotations;
using StreamJsonRpc;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Reports the progress of a workspace computation to the language service's client.
    /// </summary>
    internal sealed class ProgressReporter : IProgressReporter
    {
        private readonly TestContext? m_testContext;

        [JetBrains.Annotations.NotNull]
        private readonly StreamJsonRpc.JsonRpc m_mainRpcChannel;

        /// <nodoc />
        public ProgressReporter([JetBrains.Annotations.NotNull]StreamJsonRpc.JsonRpc mainRpcChannel, TestContext? testContext)
        {
            Contract.Requires(mainRpcChannel != null);

            m_testContext = testContext;
            m_mainRpcChannel = mainRpcChannel;
        }

        /// <inheritdoc />
        public void ReportWorkspaceFailure(string logFileName, Action openLogFileAction)
        {
            Analysis.IgnoreResult(m_mainRpcChannel.NotifyWithParameterObjectAsync("dscript/workspaceLoading", WorkspaceLoadingParams.Fail()), "Fire and forget");
        }

        /// <inheritdoc />
        public void ReportWorkspaceInit()
        {
            Analysis.IgnoreResult(m_mainRpcChannel.NotifyWithParameterObjectAsync("dscript/workspaceLoading", WorkspaceLoadingParams.Init()), "Fire and forget");
        }

        /// <inheritdoc />
        public void ReportWorkspaceInProgress(WorkspaceLoadingParams workspaceLoadingParams)
        {
            // This functionality is supported only via the special progress RPC channel.
            Analysis.IgnoreResult(m_mainRpcChannel.NotifyWithParameterObjectAsync("dscript/workspaceLoading", workspaceLoadingParams), "Fire and forget");
        }

        /// <inheritdoc />
        public void ReportWorkspaceSuccess()
        {
            Analysis.IgnoreResult(m_mainRpcChannel.NotifyWithParameterObjectAsync("dscript/workspaceLoading", WorkspaceLoadingParams.Success()), "Fire and forget");
        }

        /// <inheritdoc />
        public void ReportFindReferencesProgress(FindReferenceProgressParams findReferenceProgress)
        {
            Analysis.IgnoreResult(m_mainRpcChannel?.NotifyWithParameterObjectAsync("dscript/findReferenceProgress", findReferenceProgress), "Fire and forget");
        }
    }
}
