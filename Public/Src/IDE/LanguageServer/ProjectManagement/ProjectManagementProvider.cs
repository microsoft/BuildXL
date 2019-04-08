// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using StreamJsonRpc;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Handles project management functions such as add source file, and create new project
    /// </summary>
    public class ProjectManagementProvider
    {
        private readonly ModuleInformationProvider m_moduleInformationProvider;
        private readonly AddSourceFileToProjectProvider m_addSourceFileToProjectProvider;

        /// <summary>
        /// Creates the providers for project management and adds them as targets
        /// to the JSON-RPC layer.
        /// </summary>
        public ProjectManagementProvider(GetAppState getAppState, StreamJsonRpc.JsonRpc rpcChannel)
        {
            m_moduleInformationProvider = new ModuleInformationProvider(getAppState);
            rpcChannel.AddLocalRpcTarget(m_moduleInformationProvider);

            m_addSourceFileToProjectProvider = new AddSourceFileToProjectProvider(getAppState);
            rpcChannel.AddLocalRpcTarget(m_addSourceFileToProjectProvider);
        }
    }
}
