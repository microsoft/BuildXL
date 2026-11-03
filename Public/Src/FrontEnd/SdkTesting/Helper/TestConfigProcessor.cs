// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.FrontEnd.Sdk.Workspaces;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <summary>
    /// InMemroy Config processor
    /// </summary>
    public sealed class TestConfigProcessor : IConfigurationProcessor
    {
        private AbsolutePath m_primaryConfiguration;
        private IConfiguration m_configuration;
        private bool m_primaryConfigurationWorkspaceReleased;

        /// <nodoc />
        public TestConfigProcessor(ICommandLineConfiguration configuration)
        {
            m_primaryConfiguration = configuration.Startup.ConfigFile;
            m_configuration = configuration;
        }

        /// <inheritdoc />
        public AbsolutePath FindPrimaryConfiguration(IStartupConfiguration startupConfiguration)
        {
            return m_primaryConfiguration;
        }

        /// <inheritdoc />
        public IConfiguration InterpretConfiguration(AbsolutePath primaryConfiguration, ICommandLineConfiguration startupConfiguration)
        {
            Contract.Requires(primaryConfiguration.IsValid);

            m_primaryConfigurationWorkspaceReleased = false;
            return m_configuration;
        }

        /// <inheritdoc />
        public void Initialize(FrontEndHost host, FrontEndContext context)
        {
            Contract.Requires(host != null);
            Contract.Requires(context != null);
        }

        /// <inheritdoc />
        [MaybeNull]
        public IWorkspace PrimaryConfigurationWorkspace
        {
            get
            {
                if (m_primaryConfigurationWorkspaceReleased)
                {
                    throw new InvalidOperationException("The primary configuration workspace has been released.");
                }

                return null;
            }
        }

        /// <inheritdoc />
        public void ReleasePrimaryConfigurationWorkspace()
        {
            m_primaryConfigurationWorkspaceReleased = true;
        }

        /// <inheritdoc />
        public IConfigurationStatistics GetConfigurationStatistics() => new LoadConfigStatistics();
    }
}
