// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.CodeGenerationHelper;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Emits a TestSuite
    /// </summary>
    public static class TestEmitter
    {
        /// <summary>
        /// Emits an entire TestSuite to the target folder
        /// </summary>
        public static bool WriteTestSuite(Logger logger, TestSuite testSuite, string outputFolder, IEnumerable<string> sdkFoldersUnderTest)
        {
            try
            {
                Directory.CreateDirectory(outputFolder);
            }
            catch (IOException e)
            {
                logger.LogError(I($"Failed to prepare output directory '{outputFolder}': {e.Message}"));
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                logger.LogError(I($"Failed to prepare output directory '{outputFolder}': {e.Message}"));
                return false;
            }

            foreach (var testClass in testSuite.Classes)
            {
                var targetFile = Path.Combine(outputFolder, testClass.Name + ".g.cs");
                try
                {
                    using (var fs = File.Open(targetFile, FileMode.Create))
                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        CodeGenerator gen = new CodeGenerator((c) => writer.Write(c));
                        WriteTestClass(gen, testClass, sdkFoldersUnderTest);
                    }
                }
                catch (IOException e)
                {
                    logger.LogError(I($"Failed to write output file '{targetFile}': {e.Message}"));
                    return false;
                }
                catch (UnauthorizedAccessException e)
                {
                    logger.LogError(I($"Failed to write output file '{targetFile}': {e.Message}"));
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Generates C# for a TestClass
        /// </summary>
        public static void WriteTestClass(CodeGenerator gen, TestClass testClass, IEnumerable<string> sdkFoldersUnderTest)
        {
            var className = testClass.Name.Replace('.', '_');

            gen.GenHeader(I($"C# wrapper for DScript test: {testClass.Name}"));
            gen.Ln();
            gen.Ln("using BuildXL.FrontEnd.Script.Testing.Helper;");
            gen.Ln("using Xunit;");
            gen.Ln("using Xunit.Abstractions;");
            gen.Ln();
            gen.GenerateNoDoc();
            gen.Ln(I($"public sealed class @{className} : UnitTestBase"));
            using (gen.Br)
            {
                gen.GenerateNoDoc();
                gen.Ln(I($"public @{className}(ITestOutputHelper output)"));
                using (gen.Indent)
                {
                    gen.Ln(": base(output)");
                }

                using (gen.Br)
                {
                }

                gen.Ln();
                gen.GenerateInheritDoc();
                gen.Ln(I($"protected override string FileUnderTest => {ToCSharpLiteral(testClass.TestFilePath)};"));

                gen.Ln();
                gen.GenerateInheritDoc();
                gen.Ln("protected override string[] SdkFoldersUnderTest => new string[] {");
                using (gen.Indent)
                {
                    foreach (var sdkFolderUnderTest in sdkFoldersUnderTest)
                    {
                        gen.Ln(I($"{ToCSharpLiteral(sdkFolderUnderTest)},"));
                    }
                }

                gen.Ln("};");

                foreach (var testFunction in testClass.Functions)
                {
                    var lkgFileReference = testFunction.LkgFilePath != null
                        ? ", " + ToCSharpLiteral(testFunction.LkgFilePath)
                        : string.Empty;

                    gen.Ln();
                    gen.Ln("[Fact]");
                    gen.Ln(I($"#line {testFunction.OriginalLineAndColumn.Line} {ToCSharpLiteral(testClass.TestFilePath, allowAt: false)}"));
                    gen.Ln(I($"public void {testFunction.ShortName}() => RunSpecTest({ToCSharpLiteral(testFunction.FullIdentifier)}, {ToCSharpLiteral(testFunction.ShortName)}{lkgFileReference});"));
                }
            }
        }

        private static string ToCSharpLiteral(string value, bool allowAt = true)
        {
            if (value.Contains("\\"))
            {
                if (allowAt)
                {
                    return "@\"" + value.Replace("\"", "\"\"") + "\"";
                }
                else
                {
                    return "\"" + value.Replace("\\", "\\\\") + "\"";
                }
            }
            else
            {
                return "\"" + value + "\"";
            }
        }
    }
}
