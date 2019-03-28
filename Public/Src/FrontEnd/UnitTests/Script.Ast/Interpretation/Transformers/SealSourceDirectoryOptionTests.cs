// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation.Transformers
{
    [Trait("Category", "Transformers")]
    public sealed class SealSourceDirectoryOptionTests : DsTest
    {
        public SealSourceDirectoryOptionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void PropagateNestedNamespaceAndEnumWhenDoingImport()
        {
            const string Spec = @"
import {Transformer} from 'Sdk.Transformers';

const all = Transformer.SealSourceDirectoryOption.allDirectories;
const top = Transformer.SealSourceDirectoryOption.topDirectoryOnly;";

            var result = Build()
                .AddSpec("spec.dsc", Spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("all", "top");

            Assert.Equal(SealSourceDirectoryOption.AllDirectories, (SealSourceDirectoryOption)((EnumValue) result["all"]).Value);
            Assert.Equal(SealSourceDirectoryOption.TopDirectoryOnly, (SealSourceDirectoryOption)((EnumValue) result["top"]).Value);
        }
    }
}
