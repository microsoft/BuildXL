// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using Test.BuildXL.TestUtilities.Xunit;
using TypeScript.Net.Parsing;
using Xunit;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.DScript.Workspaces
{
    public sealed class ModuleReferenceResolverTest
    {
        private static readonly PathTable PathTable = new PathTable();
        private readonly AbsolutePath m_fakeMainFile = AbsolutePath.Create(PathTable, A("c", "FakeMainFile.dsc"));
        private readonly AbsolutePath m_fakeModuleConfigFile = AbsolutePath.Create(PathTable, A("c", "module.config.dsc"));

        [Theory]
        [InlineData(@"import * as Foo from ""Foo""")]
        [InlineData(@"export * from ""Foo""")]
        [InlineData(@"const x = importFrom(""Foo"").value")]
        public void TestImportExportsAreCollected(string content)
        {
            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(content);
            var referenceResolver = new ModuleReferenceResolver(PathTable);
            var references = referenceResolver.GetExternalModuleReferences(sourceFile).ToArray();

            XAssert.AreEqual(1, references.Length);
            XAssert.AreEqual("Foo", references[0].Name);
        }

        [Theory]
        [InlineData(@"import * as Foo from ""./Foo""")]
        [InlineData(@"import * as Foo from ""/Foo""")]

        public void TestRelativeReferenceIsNotCollected(string content)
        {
            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(content);
            var referenceResolver = new ModuleReferenceResolver(PathTable);
            var references = referenceResolver.GetExternalModuleReferences(sourceFile).ToArray();

            XAssert.AreEqual(0, references.Length);
        }

        [Fact]
        public void TestUpdateExternalModuleReference()
        {
            var content = @"const x = 42";

            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(content);
            var referenceResolver = new ModuleReferenceResolver(PathTable);

            var module =
                ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                    ModuleDescriptor.CreateForTesting("Test"), m_fakeMainFile, m_fakeModuleConfigFile, new[] { m_fakeMainFile }, PathTable);
            Failure failure;
            var result = referenceResolver.TryUpdateExternalModuleReference(sourceFile, module, out failure);

            XAssert.IsTrue(result);
            XAssert.IsTrue(sourceFile.ResolvedModules.ContainsKey("Test"));
        }

        [Theory]
        [InlineData(@"/FakeMainFile.dsc")]
        [InlineData(@"../FakeMainFile.dsc")]
        [InlineData(@"./A/../../FakeMainFile.dsc")]
        public void TestUpdateInternalModuleReferences(string internalReference)
        {
            var content = "import * as Foo from \"" + internalReference + "\";";

            // We create a module with a fake main file at the root and one project under SubDir
            var projectDir = AbsolutePath.Create(PathTable, A("c", "SubDir", "project.dsc"));

            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(projectDir.ToString(PathTable), content);
            var referenceResolver = new ModuleReferenceResolver(PathTable);

            var module =
                ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                    ModuleDescriptor.CreateForTesting("Test"), m_fakeMainFile, m_fakeModuleConfigFile, new[] { m_fakeMainFile, projectDir },
                    PathTable);
            Failure[] failures;
            var result = referenceResolver.TryUpdateAllInternalModuleReferences(sourceFile, module, out failures);

            XAssert.IsTrue(result);
            XAssert.IsTrue(sourceFile.ResolvedModules.ContainsKey(internalReference));
        }

        public void TestInternalReferencesOutsideModuleAreNotAllowed()
        {
            var content = "import * as Foo from \"./DoesNotExist.dsc\";";

            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(m_fakeMainFile.ToString(PathTable), content);
            var referenceResolver = new ModuleReferenceResolver(PathTable);

            var module = ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                ModuleDescriptor.CreateForTesting("Test"),
                m_fakeMainFile,
                m_fakeModuleConfigFile,
                new[] { m_fakeMainFile },
                PathTable);

            Failure[] failures;
            var result = referenceResolver.TryUpdateAllInternalModuleReferences(sourceFile, module, out failures);

            XAssert.IsFalse(result);
            XAssert.AreEqual(1, failures.Length);
            XAssert.IsTrue(failures[0] is SpecNotUnderAModuleFailure);
        }
    }
}
