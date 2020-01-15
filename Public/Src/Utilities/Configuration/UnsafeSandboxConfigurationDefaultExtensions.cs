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

        // Extension methods

        /// <nodoc/>
        public static DoubleWritePolicy DoubleWritePolicy(this IUnsafeSandboxConfiguration configuration) => 
            configuration.DoubleWritePolicy ?? DefaultDoubleWritePolicy;

    }
}
