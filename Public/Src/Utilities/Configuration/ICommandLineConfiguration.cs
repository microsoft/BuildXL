// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configuration including all options on the commandline.
    /// </summary>
    public partial interface ICommandLineConfiguration : IConfiguration
    {
        /// <summary>
        /// Whether to show help or not
        /// </summary>
        HelpLevel Help { get; }

        /// <summary>
        /// DX code to show specific help for
        /// </summary>
        int HelpCode { get; }

        /// <summary>
        /// Whether nologo is on or not.
        /// </summary>
        bool NoLogo { get; }

        /// <summary>
        /// Whether to launch the debugger on start. This is useful when either a) you already have a server which 
        /// doesn't respect new environment variables set in the client exe or b) you have many uses of bxl.exe
        /// and only want to debug one specific process of BuildXL.
        /// </summary>
        bool LaunchDebugger { get; }

        /// <summary>
        /// Startup configuration
        /// </summary>
        [NotNull]
        IStartupConfiguration Startup { get; }

        /// <summary>
        /// We have special logic if the filter is from the commandline or if the filter is from configuration.
        /// </summary>
        string Filter { get; }

        /// <summary>
        /// Specifies how server mode will be used
        /// </summary>
        ServerMode Server { get; }

        /// <summary>
        /// Directory where the server process deployment will be created
        /// </summary>
        AbsolutePath ServerDeploymentDirectory { get; }

        /// <summary>
        /// Server maximum idle time in minutes.
        /// </summary>
        int ServerMaxIdleTimeInMinutes { get; }
    }

    /// <summary>
    /// How much help to show
    /// </summary>
    /// <remarks>
    /// CAUTION!!!
    /// This is a duplication from HelpText in BuildXL.ToolSupport for the sake of not adding dependencies. Make sure to
    /// keep it in sync if it is modified
    /// </remarks>
    public enum HelpLevel : byte
    {
        /// <summary>
        /// None
        /// </summary>
        None = 0,

        /// <summary>
        /// Help for a specific DxCode
        /// </summary>
        DxCode = 1,

        /// <summary>
        /// The standard help
        /// </summary>
        Standard = 2,

        /// <summary>
        /// The full help including verbose obscure options
        /// </summary>
        Verbose = 3,
    }
}
