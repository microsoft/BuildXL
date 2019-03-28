// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Sdk;
using Test.BuildXL.FrontEnd.Core;
using Xunit.Abstractions;

#pragma warning disable 1591

namespace Test.DScript.Ast.DScriptV2
{
    public class DScriptV2Test : DsTest
    {
        public DScriptV2Test(ITestOutputHelper output) : base(output)
        {
        }

        public static FrontEndConfiguration CreateV2FrontEndConfiguration(bool isDebugged)
        {
            // We turn on all BuildXL Script V2 related options
            return new FrontEndConfiguration
            {
                DebugScript = isDebugged,
                PreserveFullNames = true,
                MaxFrontEndConcurrency = 1,
                UseSpecPublicFacadeAndAstWhenAvailable = false,
            };
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged) => CreateV2FrontEndConfiguration(isDebugged);

        /// <summary>
        /// Creates an empty legacy configuration file and adds the default prelude package so type checking can work
        /// </summary>
        protected SpecEvaluationBuilder BuildLegacyConfigurationWithPrelude()
        {
            return Build().EmptyLegacyConfiguration();
        }

        /// <summary>
        /// Creates a legacy configuration file and adds the default prelude package so type checking can work
        /// </summary>
        protected SpecEvaluationBuilder BuildLegacyConfigurationWithPrelude(string config)
        {
            return Build().LegacyConfiguration(config);
        }

        /// <summary>
        /// Creates an empty configuration file and adds the default prelude package so type checking can work
        /// </summary>
        protected SpecEvaluationBuilder BuildWithPrelude()
        {
            return Build().EmptyConfiguration();
        }

        /// <summary>
        /// Creates a configuration file and adds the default prelude package so type checking can work
        /// </summary>
        protected SpecEvaluationBuilder BuildWithPrelude(string config)
        {
            return Build().Configuration(config);
        }

        /// <summary>
        /// Adds the prelude module to <see cref="DsTestWriter"/> necessary for V2 evaluations.
        /// </summary>
        internal new static void AddPrelude(DsTestWriter testWriter)
        {
            testWriter.ConfigWriter.AddBuildSpec(R("Sdk.Prelude", "prelude.dsc"),
                File.ReadAllText(R("Libs", "lib.core.d.ts")));
            testWriter.ConfigWriter.AddBuildSpec(R("Sdk.Prelude", "package.config.dsc"), CreatePackageConfig(FrontEndHost.PreludeModuleName));
        }
    }
}
