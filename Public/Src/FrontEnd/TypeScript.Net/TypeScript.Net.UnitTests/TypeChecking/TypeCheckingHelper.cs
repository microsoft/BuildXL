// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.TypeChecking;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace TypeScript.Net.UnitTests.TypeChecking
{
    public sealed class TypeCheckingHelper
    {
        private static string s_libFile;

        private static ISourceFile s_parsedLibFile;

        public static List<Diagnostic> ParseAndCheck(TestFile tsInputFile, ParsingOptions parsingOptions = null)
        {
            var diagnostics = GetSemanticDiagnostics(tsInputFiles: tsInputFile, parsingOptions: parsingOptions);

            return diagnostics;
        }

        public static List<Diagnostic> GetSemanticDiagnostics(
            ParsingOptions parsingOptions = null,
            bool implicitReferenceModule = false, params string[] codes)
        {
            var testFiles = GenerateTestFileNames("dsc", codes);
            return GetSemanticDiagnostics(
                useCachedVersion: false,
                parsingOptions: parsingOptions,
                implicitReferenceModule: implicitReferenceModule,
                tsInputFiles: testFiles);
        }

        public static List<Diagnostic> GetSemanticDiagnostics(bool useCachedVersion = false, ParsingOptions parsingOptions = null, params string[] codes)
        {
            var testFiles = GenerateTestFileNames("ts", codes);
            return GetSemanticDiagnostics(
                useCachedVersion: useCachedVersion,
                parsingOptions: parsingOptions,
                tsInputFiles: testFiles);
        }

        private static string LibFileContent => LazyInitializer.EnsureInitialized(ref s_libFile, () => File.ReadAllText("Libs/lib.core.d.ts"));

        internal static ISourceFile ParseLib() => ParsingHelper.ParsePotentiallyBrokenSourceFile(LibFileContent, A("c", "lib.core.d.ts"));

        /// <summary>
        /// All input files will be grouped under the same module
        /// </summary>
        public static List<Diagnostic> GetSemanticDiagnostics(
            bool useCachedVersion = false,
            ParsingOptions parsingOptions = null, bool implicitReferenceModule = false, params TestFile[] tsInputFiles)
        {
            var sourceFiles = Analyze(out var checker, useCachedVersion, parsingOptions, implicitReferenceModule, tsInputFiles);

            var result = sourceFiles.SelectMany(sourceFile => sourceFile.ParseDiagnostics).ToList();
            result.AddRange(sourceFiles.SelectMany(sourceFile => sourceFile.BindDiagnostics));
            result.AddRange(sourceFiles.SelectMany(sourceFile => checker.GetDiagnostics(sourceFile)));

            return result;
        }

        public static List<ISourceFile> Analyze(
            out ITypeChecker checker,
            ParsingOptions parsingOptions = null,
            bool implicitReferenceModule = false, params string[] codes)
        {
            var testFiles = GenerateTestFileNames("dsc", codes);
            return Analyze(
                out checker,
                useCachedVersion: false,
                parsingOptions: parsingOptions,
                implicitReferenceModule: implicitReferenceModule,
                tsInputFiles: testFiles);
        }

        /// <summary>
        /// Analyze all source files.
        /// </summary>
        public static List<ISourceFile> Analyze(
            out ITypeChecker checker,
            bool useCachedVersion = false,
            ParsingOptions parsingOptions = null, bool implicitReferenceModule = false, params TestFile[] tsInputFiles)
        {
            LazyInitializer.EnsureInitialized(ref s_libFile, () => File.ReadAllText("Libs/lib.core.d.ts"));

            // Need to parse lib.d.ts file each time, because checker mutates ISourcefile
            // var parsedLibFile = s_parsedSourceFile;//threadLocalSource.Value;
            var parsedLibFile = LazyInitializer.EnsureInitialized(ref s_parsedLibFile, ParseLib);

            if (!useCachedVersion)
            {
                // See comment in StringBasedCheckerTests.CheckAnalysisOrderIssue for more details!
                parsedLibFile = ParsingHelper.ParsePotentiallyBrokenSourceFile(s_libFile, "lib.core.d.ts", parsingOptions);
            }

            var sourceFiles =
                tsInputFiles.Select(
                    tsInputFile =>
                        ParsingHelper.ParsePotentiallyBrokenSourceFile(tsInputFile.Content, tsInputFile.UnitName, parsingOptions)).ToList();

            var sourceFilesWithLib = new List<ISourceFile>(sourceFiles) { parsedLibFile };

            // Need to parse lib.d.ts file every time, because checker mutates it and this could lead to failure
            // when multiple checking processes are running in parallel.
            // var parsedLibFile = ParseOrDeserialize(s_libFile);// ParsingHelper.ParseSourceFile(s_libFile, "lib.d.ts");
            ModuleName? fakeModule = new ModuleName("fakeModule", implicitReferenceModule);
            var host = new TypeCheckerHostFake(fakeModule, sourceFilesWithLib.ToArray());
            checker = Checker.CreateTypeChecker(host, true, degreeOfParallelism: 1);

            return sourceFiles;
        }

        private static TestFile[] GenerateTestFileNames(string extension, string[] codes)
        {
            var result = new TestFile[codes.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new TestFile("FakeSourceFile" + i + "." + extension, codes[i]);
            }

            return result;
        }
    }
}
