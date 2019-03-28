// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
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
            Contract.Requires(primaryConfiguration != null);

            return m_configuration;
        }

        /// <inheritdoc />
        public void Initialize(FrontEndHost host, FrontEndContext context)
        {
            Contract.Requires(host != null);
            Contract.Requires(context != null);
        }

        /// <inheritdoc />
        public IWorkspace PrimaryConfigurationWorkspace => null;

        /// <inheritdoc />
        public IConfigurationStatistics GetConfigurationStatistics() => new LoadConfigStatistics();
    }
}
