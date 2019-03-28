// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TypeScript.Net.Binding;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Represents an entire test suite
    /// </summary>
    public sealed class TestSuite
    {
        /// <summary>
        /// The test classes in this suite
        /// </summary>
        public IReadOnlyList<TestClass> Classes { get; }

        /// <nodoc />
        public TestSuite(params TestClass[] classes)
        {
            Classes = classes;
        }

        /// <summary>
        /// Tries to construct the TestSuite from the testFiles.
        /// </summary>
        public static bool TryCreateTestSuite(Logger logger, IEnumerable<string> testFiles, IDictionary<string, string> lkgFiles, out TestSuite testSuite)
        {
            bool success = true;

            var testClasses = new Dictionary<string, TestClass>(StringComparer.OrdinalIgnoreCase);
            var lkgFileMap = new Dictionary<string, string>(lkgFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var testFile in testFiles)
            {
                ISourceFile sourceFile;
                if (!TryParseTestFile(logger, testFile, out sourceFile))
                {
                    success = false;
                    continue;
                }

                TestClass testClass;
                if (!TestClass.TryExtractTestClass(logger, sourceFile, lkgFileMap, out testClass))
                {
                    success = false;
                    continue;
                }

                TestClass existingTestClass;
                if (testClasses.TryGetValue(testClass.Name, out existingTestClass))
                {
                    logger.LogError(C($"Duplicate test name: '{testClass.Name}'. Both file '{existingTestClass.TestFilePath}' and file '{testClass.TestFilePath}' share the same name."));
                    success = false;
                    continue;
                }

                testClasses.Add(testClass.Name, testClass);
            }

            if (testClasses.Count == 0)
            {
                logger.LogError("No test classes added to suite.");
                testSuite = null;
                return false;
            }

            if (lkgFileMap.Count > 0)
            {
                foreach (var kv in lkgFileMap)
                {
                    logger.LogError(C($"Discovered lkg file without a matching test: {kv.Value}"));
                }

                testSuite = null;
                return false;
            }

            testSuite = new TestSuite(testClasses.Values.ToArray());
            return success;
        }

        /// <summary>
        /// Parses a file from disk
        /// </summary>
        public static bool TryParseTestFile(Logger logger, string filePath, out ISourceFile sourceFile)
        {
            sourceFile = null;
            string code;
            try
            {
                if (!File.Exists(filePath))
                {
                    logger.LogError(C($"Can't parse file '{filePath}', the file does not exist."));
                    return false;
                }

                code = File.ReadAllText(filePath);
            }
            catch (IOException e)
            {
                logger.LogError(C($"Error reading file '{filePath}': ${e.Message}"));
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                logger.LogError(C($"Error reading file '{filePath}': ${e.Message}"));
                return false;
            }

            return TryParseTestFile(logger, filePath, code, out sourceFile);
        }

        /// <summary>
        /// Parses and validates code into an ISourceFile.
        /// </summary>
        public static bool TryParseTestFile(Logger logger, string filePath, string code, out ISourceFile sourceFile)
        {
            var parser = new TypeScript.Net.Parsing.Parser();
            sourceFile = parser.ParseSourceFileContent(filePath, code, ParsingOptions.DefaultParsingOptions);
            if (sourceFile.ParseDiagnostics.Count > 0)
            {
                foreach (var diagnostic in sourceFile.ParseDiagnostics)
                {
                    var lineAndColumn = diagnostic.GetLineAndColumn(sourceFile);
                    logger.LogError(filePath, lineAndColumn.Character, lineAndColumn.Character, diagnostic.MessageText.ToString());
                }

                return false;
            }

            var binder = new Binder();
            binder.BindSourceFile(sourceFile, CompilerOptions.Empty);
            if (sourceFile.BindDiagnostics.Count > 0)
            {
                foreach (var diagnostic in sourceFile.BindDiagnostics)
                {
                    var lineAndColumn = diagnostic.GetLineAndColumn(sourceFile);
                    logger.LogError(filePath, lineAndColumn.Character, lineAndColumn.Character, diagnostic.MessageText.ToString());
                }

                return false;
            }

            return true;
        }
    }
}
