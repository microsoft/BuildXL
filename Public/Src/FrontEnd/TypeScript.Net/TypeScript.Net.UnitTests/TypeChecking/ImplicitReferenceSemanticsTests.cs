// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.TypeChecking
{
    public sealed class ImplicitReferenceSemanticsTests
    {
        private TestChecker CheckerWithPrelude()
        {
            return new TestChecker(defaultModuleHasImplicitSemantics: true).SetDefaultPrelude();
        }

        [Theory]
        [InlineData(@"
export const x = 42;
export const y = x;")]
        [InlineData(@"
@@public
export const x = 42;
export const y = x;")]
        public void InternalOrPublicValuesAreVisibleToLocalFile(string code)
        {
            CheckerWithPrelude()
                .AddSourceFileToDefaultModule(code)
                .RunCheckerWithNoErrors();
        }

        [Theory]
        [InlineData(@"export const x = 42;")]
        [InlineData(@"
@@public
export const x = 42;")]
        public void InternalOrPublicValuesAreVisibleWithinSameModule(string definition)
        {
            var use = @"export const y = x;";

            CheckerWithPrelude()
                .AddSourceFileToDefaultModule(definition)
                .AddSourceFileToDefaultModule(use)
                .RunCheckerWithNoErrors();
        }

        [Fact]
        public void InternalValuesAreNotVisibileAcrossModules()
        {
            var spec1 = @"export const x = 42;";
            var spec2 = @"export const y = x;";

            var diagnostic = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", true), spec1)
                .AddSourceFile(new ModuleName("ModuleB", true), spec2)
                .RunCheckerWithFirstError();

            Assert.True(diagnostic.MessageText.AsString().Contains("Cannot find name 'x'"));
        }

        [Fact]
        public void PublicValuesAreVisibleAcrossModulesWhenImported()
        {
            var spec1 = @"
@@public
export const x = 42;";

            var spec2 = @"
import * as A from ""ModuleA"";
export const y = A.x;";

            CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", true), spec1)
                .AddSourceFile(new ModuleName("ModuleB", true), spec2)
                .RunCheckerWithNoErrors();
        }

        [Fact]
        public void PublicValuesAreNotVisibleAcrossModulesIfNotImported()
        {
            var spec1 = @"
@@public
export const x = 42;";

            var spec2 = @"
export const y = x;";

            var diagnostic = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", true), spec1)
                .AddSourceFile(new ModuleName("ModuleB", true), spec2)
                .RunCheckerWithFirstError();

            Assert.True(diagnostic.MessageText.AsString().Contains("Cannot find name 'x'"));
        }
    }
}
