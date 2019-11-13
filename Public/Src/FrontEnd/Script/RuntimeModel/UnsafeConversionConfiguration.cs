// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Unsafe options for for AST conversion process.
    /// </summary>
    /// <remarks>
    /// This configuration enables to tune performance of the AST conversion phase by disabling some important but expensive steps.
    /// Using non-default version of this configuration can significantly improve performance, but can drastically degrade error diagnostic.
    /// </remarks>
    public sealed class UnsafeConversionConfiguration
    {
        /// <summary>
        /// If true, then analysis is disabled.
        /// </summary>
        public bool DisableAnalysis { get; set; } = false;

        /// <summary>
        /// If true than analysis for prohibiting definition before use is disabled.
        /// </summary>
        public bool DisableDeclarationBeforeUseCheck { get; set; } = false;

        /// <summary>
        /// If true then expensive line translation is disabled.
        /// </summary>
        public bool DisableLineInfoConversion { get; set; } = false;

        /// <summary>
        /// If true, skip converting types for variable declarations.
        /// </summary>
        public bool SkipTypeConversion { get; set; } = false;

        /// <nodoc />
        public static UnsafeConversionConfiguration GetConfigurationFromEnvironmentVariables()
        {
            var result = new UnsafeConversionConfiguration();

            if (Environment.GetEnvironmentVariable("BUILDXL_DISABLE_DECLARE_BEFORE_USE_CHECK") != null)
            {
                result.DisableDeclarationBeforeUseCheck = true;
            }

            if (Environment.GetEnvironmentVariable("BUILDXL_DISABLE_BINDING_AND_ANALYSIS") != null)
            {
                result.DisableAnalysis = true;
            }

            if (Environment.GetEnvironmentVariable("BUILDXL_DISABLE_LINE_INFO_CONVERSION") != null)
            {
                result.DisableLineInfoConversion = true;
            }

            return result;
        }
    }
}
