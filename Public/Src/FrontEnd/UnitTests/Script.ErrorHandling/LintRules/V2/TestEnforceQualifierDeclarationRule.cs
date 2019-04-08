// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceQualifierDeclarationRule : SemanticBasedTests
    {
        public TestEnforceQualifierDeclarationRule(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void QualifierShouldBeAlone()
        {
            string code = "export declare const qualifier : {}, myVar : number;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierDeclarationShouldBeAloneInTheStatement);
        }

        [Fact]
        public void QualifierShouldBeNamespaceOrTopLevelDeclaration()
        {
            string code = @"
function foo() {
    const qualifier : {} = {};
}";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierDeclarationShouldBeTopLevel);
        }

        [Theory]
        [InlineData("const qualifier: {} = {};")]
        [InlineData("declare const qualifier: {};")]
        [InlineData("export const qualifier: {} = {};")]
        public void QualifierShouldHaveTheRightModifiers(string code)
        {
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierDeclarationShouldBeConstExportAmbient);
        }

        [Fact]
        public void QualifierTypeShouldBeDeclared()
        {
            string code = "export declare const qualifier;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierTypeShouldBePresent);
        }

        [Fact]
        public void 
            QualifierTypeCanBeEmpty()
        {
            string code = "export declare const qualifier : {};";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void QualifierTypeCanBeImported()
        {
            string spec1 = @"
@@public
export interface QualifierType extends Qualifier {};
export declare const qualifier : QualifierType;";

            string spec2 = @"
import * as spec1 from 'MyPackage';
namespace X {
  export declare const qualifier : spec1.QualifierType;
}";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec("A/package.config.dsc", V2Module("MyPackage"))
                .AddSpec("A/spec1.dsc", spec1)
                .AddSpec("rootSpec.dsc", spec2)
                .RootSpec("rootSpec.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void QualifierTypeCanBeTypeLiteralWithStringLiteralMembers()
        {
            string code = @"
export declare const qualifier : {
    platform: 'x86' | 'arm';
    configuration: 'release';
};";

            BuildLegacyConfigurationWithPrelude()
                .Qualifier("{platform: 'x86', configuration: 'release'}")
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void QualifierTypeCanReferenceQualifier()
        {
            string code = "export declare const qualifier : Qualifier;";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void QualifierTypeCanReferenceInterfaceWhichReferencesQualifier()
        {
            string code = @"
interface MyQualifier extends Qualifier{
    platform: 'x86' | 'arm';
    configuration: 'release';
}

export declare const qualifier : MyQualifier;";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .Qualifier("{platform: 'x86', configuration: 'release'}")
                .RootSpec("foo.dsc")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void QualifierTypeCanReferenceInterfaceWhichReferencesQualifierAndNonQualifierType()
        {
            string code = @"
interface Foo {
}

interface MyQualifier extends Foo, Qualifier {}

export declare const qualifier : MyQualifier;";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithNoErrors();
        }

        [Theory]
        [InlineData("number")]
        [InlineData("string")]
        [InlineData("boolean")]
        [InlineData("string[]")]
        [InlineData("Object")]
        [InlineData("any")]
        [InlineData("[number, string]")]
        [InlineData("string | number")]
        public void QualifierTypeMemberCanOnlyBeStringLiteral(string memberType)
        {
            string code = $@"
export declare const qualifier : {{
    platform: {memberType};
}};";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierLiteralTypeMemberShouldHaveStringLiteralType);
        }

        [Theory]
        [InlineData("number")]
        [InlineData("string")]
        [InlineData("boolean")]
        [InlineData("any")]
        [InlineData("[number, string]")]
        [InlineData("string | number")]
        public void QualifierTypeShouldOnlyBeTypeLiteralOrInterface(string type)
        {
            string code = $"export declare const qualifier : {type};";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierTypeShouldBeAnInterfaceOrTypeLiteral);
        }

        [Fact]
        public void QualifierTypeMembersShouldNotBeOptionalOnInterfaces()
        {
            string code = @"
interface MyQualifier extends Qualifier {
    platform?: 'x86' | 'arm';
}

export declare const qualifier : MyQualifier;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierOptionalMembersAreNotAllowed);
        }

        [Fact]
        public void QualifierTypeMembersShouldNotBeOptionalOnLiteralTypes()
        {
            string code = @"
export declare const qualifier : {platform?: 'x86' | 'arm'};";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierOptionalMembersAreNotAllowed);
        }

        [Fact]
        public void QualifierTypeReferenceShouldReferenceQualifierDirectly()
        {
            string code = @"
interface MyOtherQualifier extends Qualifier {
}

interface MyQualifier extends MyOtherQualifier {
}

export declare const qualifier : MyQualifier;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierInterfaceTypeShouldBeOrInheritFromQualifier);
        }

        [Theory]
        [InlineData("number")]
        [InlineData("string")]
        [InlineData("boolean")]
        [InlineData("string[]")]
        [InlineData("Object")]
        [InlineData("any")]
        [InlineData("[number, string]")]
        [InlineData("string | number")]
        public void QualifierTypeReferenceMemberShouldOnlyBeTypeLiteral(string memberType)
        {
            string code = $@"
interface MyQualifier extends Qualifier {{
    platform: {memberType};
}}

export declare const qualifier : MyQualifier;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierLiteralTypeMemberShouldHaveStringLiteralType);
        }

        [Fact]
        public void InheritedTypesAreAlsoValidated()
        {
            string code = @"
interface Foo {
    platform: number;
}

interface MyQualifier extends Foo, Qualifier {}

export declare const qualifier : MyQualifier;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.QualifierLiteralTypeMemberShouldHaveStringLiteralType);
        }
    }
}
