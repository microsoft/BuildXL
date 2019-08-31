// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Analyzer;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.FrontEnd.Script.Analyzer.Utilities;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Workspaces;
using TypeScript.Net.Types;
using Xunit;
using ScriptAnalyzer = BuildXL.FrontEnd.Script.Analyzer.Analyzer;
using BuildXL.Utilities.Configuration.Mutable;

namespace Test.Tool.DScript.Analyzer
{
    public class AnalyzerTest<TAnalyzer> : TemporaryStorageTestBase
        where TAnalyzer : ScriptAnalyzer, new()
    {
        /// <summary>
        /// Runs the analyzer on the testSouce and expects it to succeed with the given formatted text.
        /// </summary>
        public void TestSuccess(
            string testSource,
            string expectedResult = null,
            string[] extraSources = null,
            Dictionary<string, string> modules = null,
            string[] options = null,
            bool fix = true,
            bool preserveTrivia = false)
        {
            TestHelper(
                testSource,
                extraSources,
                modules,
                options,
                fix,
                (success, logger, sourceFile) =>
                {
                    var errors = string.Join(Environment.NewLine, logger.CapturedDiagnostics.Select(d => d.Message));
                    Assert.True(success, "Expect to have successful run. Encountered:\r\n" + errors);
                    Assert.False(logger.HasErrors, "Expect to have no errors. Encountered:\r\n" + errors);

                    var writer = new ScriptWriter();
                    var visitor = new DScriptPrettyPrintVisitor(writer, attemptToPreserveNewlinesForListMembers: true);
                    if (options != null && options.Contains("/s+"))
                    {
                        visitor.SpecialAddIfFormatting = true;
                    }
                    sourceFile.Cast<IVisitableNode>().Accept(visitor);

                    var actualText = writer.ToString();
                    Assert.Equal(expectedResult ?? testSource, actualText);
                },
                preserveTrivia);
        }

        /// <summary>
        /// Runs the analyzer on the testSouce and one failures to be reported.
        /// </summary>
        public void TestErrorReport(
            string testSource,
            string expectedErrorMessage,
            string[] extraSources = null,
            Dictionary<string, string> modules = null,
            string[] options = null,
            bool fix = false)
        {
            TestErrorReports(
                testSource,
                new[] { expectedErrorMessage },
                extraSources,
                modules,
                options,
                fix);
        }

        /// <summary>
        /// Runs the analyzer on the testSouce and expects some failures to be reported.
        /// </summary>
        public void TestErrorReports(
           string testSource,
           string[] expectedErrorMessages,
           string[] extraSources = null,
           Dictionary<string, string> modules = null,
           string[] options = null,
           bool fix = false,
           bool preserveTrivia = false)
        {
            TestHelper(
                testSource,
                extraSources,
                modules,
                options,
                fix,
                (success, logger, sourceFile) =>
                {
                    Assert.False(success, "Expect to fail");
                    Assert.True(logger.HasErrors, "Expect to have errors");

                    var actualDiagnostics = logger.CapturedDiagnostics.Select(d => d.Message).ToArray();
                    Assert.Equal(expectedErrorMessages, actualDiagnostics);
                },
                preserveTrivia);
        }

        internal void TestHelper(
            string testSource,
            string[] extraSources,
            Dictionary<string, string> modules,
            string[] options,
            bool fix,
            Action<bool, Logger, ISourceFile> handleResult,
            bool preserveTrivia = false)
        {
            var analyzer = new TAnalyzer();
            var pathTable = new PathTable();
            var args = new Args(
                commandLineConfig: new CommandLineConfiguration() { Startup = { ConfigFile = AbsolutePath.Create(pathTable, X("/b/Fake.config.dsc")) } },
                pathTable: pathTable,
                fix: fix,
                help: false,
                analyzers: new List<ScriptAnalyzer> { analyzer },
                args: options ?? new string[0]);

            foreach (var option in args.Options)
            {
                analyzer.HandleOption(option);
            }

            var logger = Logger.CreateLogger(preserveLogEvents: true);
            var context = FrontEndContext.CreateInstanceForTesting();

            Workspace workspace;
            var testModuleFile = LoadAndTypecheckFile(context, testSource, extraSources, modules, out workspace, preserveTrivia);

            analyzer.SetSharedState(args, context, logger, workspace, null);

            var result = analyzer.AnalyzeSourceFile(workspace, testModuleFile.Key, testModuleFile.Value);

            var updatedModule = workspace.SpecModules.FirstOrDefault(m => string.Equals(m.Descriptor.Name, "TestModule", StringComparison.Ordinal));
            var updatedTestFile = updatedModule.Specs.Values.FirstOrDefault();

            analyzer.FinalizeAnalysis();

            handleResult(result, logger, updatedTestFile);
        }

        protected KeyValuePair<AbsolutePath, ISourceFile> LoadAndTypecheckFile(
            FrontEndContext context,
            string testSource,
            string[] extraSources,
            Dictionary<string, string> modules,
            out Workspace workspace,
            bool preserveTrivia = false)
        {
            var wsHelper = new WorkspaceTestBase(pathTable: context.PathTable, preludeName: FrontEndHost.PreludeModuleName, nameResolutionSemantics: NameResolutionSemantics.ImplicitProjectReferences);
            var repo = wsHelper.CreateEmptyContent();

            var testModule = ModuleDescriptor.CreateForTesting("TestModule");
            repo.AddContent(testModule, testSource);
            if (extraSources != null)
            {
                repo.AddContent(testModule, extraSources);
            }

            repo.AddContent(FrontEndHost.PreludeModuleName,
                File.ReadAllText(@"Libs/lib.core.d.ts"),
                File.ReadAllText(@"Libs/Prelude.IO.ts"),
                "namespace Tool {export declare function option(value: string): any;}");
            if (modules != null)
            {
                foreach (var kv in modules)
                {
                    repo.AddContent(ModuleDescriptor.CreateForTesting(kv.Key), kv.Value);
                }
            }

            workspace = wsHelper.CreateSematicWorkspaceFromContent(testModule, preserveTrivia, repo).GetAwaiter().GetResult();
            WorkspaceTestBase.AssertNoWorkspaceFailures(workspace);

            var semanticModel = workspace.GetSemanticModel();
            WorkspaceTestBase.AssertNoSemanticErrors(semanticModel);

            return workspace.Modules.First(m => m.Descriptor.Name == "TestModule").Specs.First(f => f.Key.GetName(context.PathTable).ToString(context.StringTable) == "0.dsc");
        }
    }
}
