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
        public const bool DefaultEnableFullReparsePointResolving = false;

        /// <nodoc/>
        public const bool DefaultSkipFlaggingSharedOpaqueOutputs = false;

        /// <nodoc/>
        public const bool DefaultsDoNotApplyAllowListToDynamicOutputs = true;

        // Extension methods

        /// <nodoc/>
        public static RewritePolicy DoubleWritePolicy(this IUnsafeSandboxConfiguration configuration) =>
            configuration.DoubleWritePolicy ?? DefaultDoubleWritePolicy;

        /// <nodoc/>
        public static RewritePolicy SourceWritePolicy(this IUnsafeSandboxConfiguration configuration) =>
            configuration.DoubleWritePolicy ?? DefaultSourceRewritePolicy;

        /// <summary>
        /// Whether <see cref="IUnsafeSandboxConfiguration.EnableFullReparsePointResolving"/> is enabled and we are in a Windows-based OS
        /// </summary>
        /// <remarks>
        /// Mac already resolves all reparse point in its sandbox, and doesn't need post-processing since MacLookup operations are just ignored.
        /// </remarks>
        public static bool EnableFullReparsePointResolving(this IUnsafeSandboxConfiguration configuration) =>
            ((configuration.EnableFullReparsePointResolving ?? DefaultEnableFullReparsePointResolving) || !configuration.IgnoreFullReparsePointResolving);

        /// <nodoc/>
        public static bool SkipFlaggingSharedOpaqueOutputs(this IUnsafeSandboxConfiguration configuration) =>
            (configuration.SkipFlaggingSharedOpaqueOutputs ?? DefaultSkipFlaggingSharedOpaqueOutputs);

        /// <nodoc/>
        public static bool DoNotApplyAllowListToDynamicOutputs(this IUnsafeSandboxConfiguration configuration) =>
            (configuration.DoNotApplyAllowListToDynamicOutputs ?? DefaultsDoNotApplyAllowListToDynamicOutputs);
    }
}
