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
        public const RewritePolicy DefaultDoubleWritePolicy = Configuration.RewritePolicy.DoubleWritesAreErrors;

        /// <nodoc/>
        public const RewritePolicy DefaultSourceRewritePolicy = Configuration.RewritePolicy.SourceRewritesAreErrors;

        /// <nodoc/>
        public const bool DefaultProcessSymlinkedAccesses = false;

        /// <nodoc/>
        public const bool DefaultEnableFullReparsePointResolving = false;

        /// <nodoc/>
        public const bool DefaultSkipFlaggingSharedOpaqueOutputs = false;

        // Extension methods

        /// <nodoc/>
        public static RewritePolicy DoubleWritePolicy(this IUnsafeSandboxConfiguration configuration) =>
            configuration.DoubleWritePolicy ?? DefaultDoubleWritePolicy;

        /// <nodoc/>
        public static RewritePolicy SourceWritePolicy(this IUnsafeSandboxConfiguration configuration) =>
            configuration.DoubleWritePolicy ?? DefaultSourceRewritePolicy;

        /// <summary>
        /// Whether <see cref="IUnsafeSandboxConfiguration.ProcessSymlinkedAccesses"/> is enabled and we are in a Windows-based OS
        /// </summary>
        public static bool ProcessSymlinkedAccesses(this IUnsafeSandboxConfiguration configuration) =>
            (configuration.ProcessSymlinkedAccesses ?? DefaultProcessSymlinkedAccesses) && !OperatingSystemHelper.IsUnixOS;

        /// <summary>
        /// Whether <see cref="IUnsafeSandboxConfiguration.EnableFullReparsePointResolving"/> is enabled and we are in a Windows-based OS
        /// </summary>
        /// <remarks>
        /// This option is always enabled by default in Unix-like OSs.
        /// </remarks>
        public static bool EnableFullReparsePointResolving(this IUnsafeSandboxConfiguration configuration) =>
            ((configuration.EnableFullReparsePointResolving ?? DefaultEnableFullReparsePointResolving) || !configuration.IgnoreFullReparsePointResolving || OperatingSystemHelper.IsUnixOS);

        /// <nodoc/>
        public static bool SkipFlaggingSharedOpaqueOutputs(this IUnsafeSandboxConfiguration configuration) =>
            (configuration.SkipFlaggingSharedOpaqueOutputs ?? DefaultSkipFlaggingSharedOpaqueOutputs);

    }
}
