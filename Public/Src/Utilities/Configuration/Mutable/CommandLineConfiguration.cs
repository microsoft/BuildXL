// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class CommandLineConfiguration : ConfigurationImpl, ICommandLineConfiguration
    {
        /// <nodoc />
        public CommandLineConfiguration()
        {
            Startup = new StartupConfiguration();
            Help = HelpLevel.None;
            Server = OperatingSystemHelper.IsUnixOS ? ServerMode.Disabled : ServerMode.Enabled;
            ServerMaxIdleTimeInMinutes = 60;
        }

        /// <summary>
        /// Create new mutable instance from template
        /// </summary>
        /// <remarks>
        /// This is the only class with CommandLineConfiguration as the configuration entrypoint where the pathRemapper is an argument with default value.
        /// If the argument was optional everywhere as well there would be no compiler helping us if someone forgot to pass it along.
        /// This is the main entrypoint so we allow a default value here for convenience
        /// </remarks>
        public CommandLineConfiguration(ICommandLineConfiguration template, PathRemapper pathRemapper = null)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);

            pathRemapper = pathRemapper ?? new PathRemapper();

            Help = template.Help;
            NoLogo = template.NoLogo;
            LaunchDebugger = template.LaunchDebugger;
            Startup = new StartupConfiguration(template.Startup, pathRemapper);
            Filter = template.Filter;
            Server = template.Server;
            ServerDeploymentDirectory = pathRemapper.Remap(template.ServerDeploymentDirectory);
            HelpCode = template.HelpCode;
            ServerMaxIdleTimeInMinutes = template.ServerMaxIdleTimeInMinutes;
        }

        /// <inheritdoc />
        public HelpLevel Help { get; set; }

        /// <inheritdoc />
        public int HelpCode { get; set; }

        /// <inheritdoc />
        public bool NoLogo { get; set; }

        /// <nodoc />
        public StartupConfiguration Startup { get; set; }

        /// <inheritdoc />
        IStartupConfiguration ICommandLineConfiguration.Startup => Startup;

        /// <inheritdoc />
        public string Filter { get; set; }

        /// <inheritdoc />
        public bool LaunchDebugger { get; set; }

        /// <inheritdoc />
        public ServerMode Server { get; set; }

        /// <inheritdoc />
        public AbsolutePath ServerDeploymentDirectory { get; set; }

        /// <inheritdoc />
        public int ServerMaxIdleTimeInMinutes { get; set; }
    }
}
