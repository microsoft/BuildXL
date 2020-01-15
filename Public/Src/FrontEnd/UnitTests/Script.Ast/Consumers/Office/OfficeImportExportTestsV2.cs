// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Consumers.Office
{
    [Trait("Category", "Office")]
    public sealed class OfficeImportExportTestsV2 : OfficeImportExportTests
    {
        public OfficeImportExportTestsV2(ITestOutputHelper output) : base(output)
        {
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var result = DScriptV2Test.CreateV2FrontEndConfiguration(isDebugged);
            result.UseLegacyOfficeLogic = true;
            return result;
        }
    }
}
