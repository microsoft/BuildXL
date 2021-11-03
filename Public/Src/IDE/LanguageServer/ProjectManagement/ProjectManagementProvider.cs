// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
            
            var options = new StreamJsonRpc.JsonRpcTargetOptions { AllowNonPublicInvocation = true };
            rpcChannel.AddLocalRpcTarget(m_moduleInformationProvider, options);

            m_addSourceFileToProjectProvider = new AddSourceFileToProjectProvider(getAppState);
            rpcChannel.AddLocalRpcTarget(m_addSourceFileToProjectProvider, options);
        }
    }
}
