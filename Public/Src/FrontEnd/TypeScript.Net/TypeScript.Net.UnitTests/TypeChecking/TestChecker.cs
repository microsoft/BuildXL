// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.TypeChecking;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.TypeChecking;
using Xunit;

namespace Test.DScript.TypeChecking
{
    /// <summary>
    /// A TypCheckerHost for testing purposes. Specs can be added into specific modules and checker can be run against them.
    /// </summary>
    public sealed class TestChecker : TypeCheckerHost
    {
        private readonly Dictionary<string, ISourceFile> m_sourceFilesByName;
        private readonly Dictionary<string, ModuleName> m_modulesByName;
        private ISourceFile m_preludeFile;

        private static int s_nextId;
        private static readonly ModuleName s_preludeModuleName = new ModuleName("Sdk.Prelude", false);

        /// <summary>
        /// The default module where specs with no specific module are added to
        /// </summary>
        public ModuleName DefaultModule { get; }

        /// <nodoc/>
        public TestChecker(bool defaultModuleHasImplicitSemantics = false)
        {
            m_sourceFilesByName = new Dictionary<string, ISourceFile>();
            m_modulesByName = new Dictionary<string, ModuleName>();
            m_preludeFile = null;
            DefaultModule = new ModuleName("DefaultModuleName", defaultModuleHasImplicitSemantics);
        }

        /// <summary>
        /// Adds source file content to the default module making up a name for it.
        /// </summary>
        public TestChecker AddSourceFileToDefaultModule(string sourceFileContent, string fileName = null)
        {
            Contract.Requires(sourceFileContent != null);

            return AddSourceFile(DefaultModule, sourceFileContent, fileName);
        }

        /// <summary>
        /// Adds a source file content to the specified module making up a name for it.
        /// </summary>
        public TestChecker AddSourceFile(ModuleName moduleName, string sourceFileContent, string fileName = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(sourceFileContent));
            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(
                fileName ?? GetUniqueFileName(),
                sourceFileContent,
                new ParsingOptions(
                    namespacesAreAutomaticallyExported: true,
                    generateWithQualifierFunctionForEveryNamespace: false,
                    preserveTrivia: false,
                    allowBackslashesInPathInterpolation: false,
                    useSpecPublicFacadeAndAstWhenAvailable: false,
                    escapeIdentifiers: true));

            return AddSourceFile(moduleName, sourceFile);
        }

        /// <summary>
        /// Adds a source file to the specified module
        /// </summary>
        public TestChecker AddSourceFile(ModuleName moduleName, ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);
            m_sourceFilesByName.Add(sourceFile.FileName, sourceFile);
            m_modulesByName.Add(sourceFile.FileName, moduleName);

            return this;
        }

        /// <summary>
        /// Loads lib.core.d.ts and sets it as the prelude
        /// </summary>
        public TestChecker SetDefaultPrelude()
        {
            return SetPrelude(TypeCheckingHelper.ParseLib());
        }

        /// <summary>
        /// Sets a given source file as the prelude
        /// </summary>
        public TestChecker SetPrelude(ISourceFile prelude)
        {
            Contract.Requires(prelude != null);
            m_preludeFile = prelude;

            return AddSourceFile(s_preludeModuleName, m_preludeFile);
        }

        /// <summary>
        /// Runs the checker against the current state of this host and returns all diagnostics
        /// </summary>
        public List<Diagnostic> RunChecker(bool trackFileToFileDependencies = false, int degreeOfParallelism = 1)
        {
            TypeChecker = Checker.CreateTypeChecker(this, true, degreeOfParallelism, trackFileToFileDependencies: trackFileToFileDependencies);

            return TypeChecker.GetDiagnostics();
        }

        /// <summary>
        /// Runs <see cref="RunChecker"/> and asserts there are no errors
        /// </summary>
        public void RunCheckerWithNoErrors()
        {
            var diagnostics = RunChecker();
            Assert.Empty(diagnostics);
        }

        /// <summary>
        /// Runs <see cref="RunChecker"/>, asserts there is at least one error and returns it
        /// </summary>
        public Diagnostic RunCheckerWithFirstError()
        {
            var diagnostics = RunChecker();
            Assert.NotEmpty(diagnostics);

            return diagnostics.First();
        }

        public ITypeChecker TypeChecker { get; private set; }

        /// <inheritdoc/>
        public override ICompilerOptions GetCompilerOptions()
        {
            return new CompilerOptions();
        }

        /// <inheritdoc/>
        public override ISourceFile[] GetSourceFiles()
        {
            return m_sourceFilesByName.Values.ToArray();
        }

        /// <inheritdoc/>
        public override ISourceFile GetSourceFile(string fileName)
        {
            return m_sourceFilesByName[fileName];
        }

        /// <inheritdoc/>
        public override bool TryGetOwningModule(string fileName, out ModuleName moduleName)
        {
            return m_modulesByName.TryGetValue(fileName, out moduleName);
        }

        /// <inheritdoc/>
        public override bool TryGetPreludeModuleName(out ModuleName preludeName)
        {
            if (m_preludeFile != null)
            {
                preludeName = s_preludeModuleName;
                return true;
            }

            preludeName = ModuleName.Invalid;
            return false;
        }

        /// <inheritdoc/>
        /// <remarks>Does nothing</remarks>
        public override void ReportSpecTypeCheckingCompleted(ISourceFile node, TimeSpan elapsed)
        {
        }

        private static string GetUniqueFileName()
        {
            return string.Format(CultureInfo.InvariantCulture, "file{0}.dsc", s_nextId++);
        }
    }
}
