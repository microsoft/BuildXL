// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Configuration for AST conversion process.
    /// </summary>
    public sealed class AstConversionConfiguration
    {
        /// <nodoc/>
        public AstConversionConfiguration(
            [CanBeNull]IEnumerable<string> policyRules,
            bool disableLanguagePolicies,
            bool disableIsObsoleteCheck,
            bool unsafeOptimized = false)
        {
            PolicyRules = policyRules ?? CollectionUtilities.EmptyArray<string>();
            UnsafeOptions = UnsafeConversionConfiguration.GetConfigurationFromEnvironmentVariables();

            if (unsafeOptimized)
            {
                UnsafeOptions.DisableAnalysis = true;
                UnsafeOptions.DisableDeclarationBeforeUseCheck = true;
                UnsafeOptions.SkipTypeConversion = true;
            }

            DegreeOfParalellism = 1;
            DisableLanguagePolicies = disableLanguagePolicies;
            DisableIsObsoleteCheck = disableIsObsoleteCheck;
        }

        /// <nodoc />
        public static AstConversionConfiguration FromConfiguration(IFrontEndConfiguration configuration)
        {
            return new AstConversionConfiguration(
                policyRules: configuration.EnabledPolicyRules,
                disableLanguagePolicies: configuration.DisableLanguagePolicyAnalysis(),
                disableIsObsoleteCheck: configuration.DisableIsObsoleteCheckDuringConversion(),
                unsafeOptimized: configuration.UnsafeOptimizedAstConversion)
            {
                PreserveFullNameSymbols = configuration.PreserveFullNames(),
            };
        }

        /// <summary>
        /// Retruns configuration used for converting config file.
        /// </summary>
        public static AstConversionConfiguration ForConfiguration(IFrontEndConfiguration configuration)
        {
            return new AstConversionConfiguration(
                policyRules: configuration.EnabledPolicyRules,
                disableLanguagePolicies: configuration.DisableLanguagePolicyAnalysis(),
                disableIsObsoleteCheck: configuration.DisableIsObsoleteCheckDuringConversion(),
                unsafeOptimized: configuration.UnsafeOptimizedAstConversion)
            {
                PreserveFullNameSymbols = configuration.PreserveFullNames(),
            };
        }

        /// <nodoc/>
        [NotNull]
        public IEnumerable<string> PolicyRules { get; }

        /// <summary>
        /// If true, additional table will be used in a module literal that allows to resolve entries by a full name.
        /// </summary>
        /// <remarks>
        /// DScript V2 feature.
        /// True only for tests, because there is no other cases when this information is required.
        /// </remarks>
        public bool PreserveFullNameSymbols { get; set; }

        /// <nodoc/>
        [NotNull]
        public UnsafeConversionConfiguration UnsafeOptions { get; }

        /// <nodoc/>
        public bool ConvertInParallel => DegreeOfParalellism > 1;

        /// <nodoc/>
        public int DegreeOfParalellism { get; }

        /// <summary>
        /// Returns true if optional language policies (like required semicolons) should be disabled.
        /// </summary>
        public bool DisableLanguagePolicies { get; }

        /// <summary>
        /// If true the check that a member is obsolete is disabled.
        /// </summary>
        /// <remarks>
        /// The check for obsolete members can be very expensive and for some customers (like Cosine) is not quite useful.
        /// Setting this flag drastically improves performance of the conversion stage.
        /// </remarks>
        public bool DisableIsObsoleteCheck { get; }
    }
}
