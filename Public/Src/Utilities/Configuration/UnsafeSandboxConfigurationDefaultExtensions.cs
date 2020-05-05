// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Set of extension methods for <see cref="IUnsafeSandboxConfiguration"/>.
    /// </summary>
    public static class UnsafeSandboxConfigurationDefaultExtensions
    {
        // Defaults

        /// <nodoc/>
        public const DoubleWritePolicy DefaultDoubleWritePolicy = Configuration.DoubleWritePolicy.DoubleWritesAreErrors;
        
        /// <nodoc/>
        public const bool DefaultProcessSymlinkedAccesses = false;

        // Extension methods

        /// <nodoc/>
        public static DoubleWritePolicy DoubleWritePolicy(this IUnsafeSandboxConfiguration configuration) => 
            configuration.DoubleWritePolicy ?? DefaultDoubleWritePolicy;

        /// <summary>
        /// Whether <see cref="IUnsafeSandboxConfiguration.ProcessSymlinkedAccesses"/> is enabled and we are in a Windows-based OS
        /// </summary>
        public static bool ProcessSymlinkedAccesses(this IUnsafeSandboxConfiguration configuration) =>
            (configuration.ProcessSymlinkedAccesses ?? DefaultProcessSymlinkedAccesses) && !OperatingSystemHelper.IsMacOS;

    }
}
