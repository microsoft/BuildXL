// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Consumers.Office
{
    [Trait("Category", "Office")]
    public class OfficeImportExportTests : DsTest
    {
        public OfficeImportExportTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var result = base.GetFrontEndConfiguration(isDebugged);
            result.UseLegacyOfficeLogic = true;
            return result;
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ExternalPackageEvaluationViaOrphanedProjectShouldBeOnDemand()
        {
            var testWriter = CreateTestWriter(@"ImportExportTest\MySolution");

            const string Config = @"
// Any change will break Office.
config({
    resolvers: [
        {
            kind: ""SourceResolver"",
            root: d`../EP`
        }
    ]
});";
            testWriter.ConfigWriter.SetConfigContent(Names.ConfigDsc, Config);

            // Create an orphaned project.
            testWriter.ConfigWriter.AddBuildSpec(
                @"MyFolder\build.dsc",
                @"
// Any change will break Office.
import * as S from 'SomeonePackage';
export const x = S.doSomething();
");

            // Create an external package "SomeonePackage".
            testWriter.AddExtraFile(
                @"..\EP\P\package.config.dsc",
                @"
// Any change will break Office.
module({ name: ""SomeonePackage""});");

            testWriter.AddExtraFile(
                @"..\EP\P\package.dsc",
                @"
// Any change will break Office.
export function doSomething() { return 0; }
export const assertFalse = (() => Contract.assert(false))(); // Should not be evaluated.
");

            var result = Evaluate(testWriter, Names.ConfigDsc, new string[0]);
            result.ExpectNoError();
        }
    }
}
