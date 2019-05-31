// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceAmbientAccessInConfig : DsTest
    {
        public TestEnforceAmbientAccessInConfig(ITestOutputHelper output) : base(output)
        { }

        [Theory]
        [InlineData("Context.getNewOutputDirectory(\"foo\")")]
        [InlineData("Context.getLastActiveUseModuleName()")]
        [InlineData("Context.getTempDirectory(\"foo\")")]
        [InlineData("Context.getMount(\"foo\")")]
        [InlineData("Context.getTemplate(\"foo\")")]
        [InlineData("Context.getBuildEngineDirectory()")]
        [InlineData("Context.hasMount(\"foo\")")]
        [InlineData("Context.getTemplate()")]
        [InlineData("Context.getBuildEngineDirectory(\"foo\")")]
        [InlineData("File.readAllText(f`package.dsc`)")]
        [InlineData("File.exists(f`package.dsc`)")]
        [InlineData("File.fromPath(p`.`)")]
        [InlineData("Directory.fromPath(f`package.dsc`)")]
        [InlineData("Directory.exists(f`package.dsc`)")]
        [InlineData("Transformer.createService(<any>undefined)")]
        [InlineData("Transformer.getIpcServerMoniker()")]
        [InlineData("Transformer.copyFile(f`foo`, p`bar`)")]
        [InlineData("Transformer.execute({ tool: { exe: f`dummy.exe` }, arguments: [], workingDirectory: d`.`, serviceShutdownCmd: { tool: { exe: f`dummy.exe` }, arguments: [], workingDirectory: d`.` } })")]
        [InlineData("Transformer.readPipGraphFragment(\"foo\", f`fragment.bin`, [])")]
        [InlineData("Transformer.writeFile(p`foo`, \"hello\", [], \"\")")]
        [InlineData("Transformer.writeData(p`foo`, \"hello\")")]
        [InlineData("Transformer.writeAllLines(p`foo`, [])")]
        [InlineData("Transformer.writeAllText(p`foo`, \"hello\")")]
        [InlineData("Transformer.sealDirectory(d`.`, [])")]
        [InlineData("Transformer.sealSourceDirectory(d`.`, Transformer.SealSourceDirectoryOption.allDirectories)")]
        [InlineData("Transformer.sealPartialDirectory(p`.`, [])")]
        [InlineData("Contract.precondition(true)")]
        [InlineData("Contract.requires(true)")]
        [InlineData("Contract.fail(\"test\")")]
        [InlineData("Contract.warn(\"test\")")]
        [InlineData("Contract.assert(true)")]
        public void ValidateDisallowedAmbientAccessInConfigString(string configCode)
        {
            var result = Build()
                .LegacyConfiguration($"config({{ qualifiers: {{ defaultQualifier: {{ platform: {configCode}.toString(), configuration: \"foo\", targetFramework: \"foo\" }}, }} }});")
                .EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.AmbientAccessInConfig, result.ErrorCode);
        }

        [Theory]
        [InlineData("Context.getLastActiveUseName()")]
        [InlineData("Context.getLastActiveUseNamespace()")]
        [InlineData("Context.getSpecFileDirectory()")]
        [InlineData("Context.getUserHomeDirectory()")]
        [InlineData("Context.getLastActiveUsePath()")]
        [InlineData("Context.getSpecFile()")]
        [InlineData("MutableSet.empty()")]
        [InlineData("Set.empty()")]
        [InlineData("Map.empty()")]
        [InlineData("Map.emptyCaseInsensitive()")]
        [InlineData("Math.max(1)")]
        public void ValidateAllowedAmbientAccessInConfigString(string configCode)
        {
            Build()
                .LegacyConfiguration($"config({{ qualifiers: {{ defaultQualifier: {{ platform: {configCode}.toString(), configuration: \"foo\", targetFramework: \"foo\" }}, }} }});")
                .EvaluateWithNoErrors();
        }

        [Theory]
        [InlineData("Context.isWindowsOS()")]
        public void ValidateAllowedAmbientAccessInConfigBool(string configCode)
        {
            Build()
                .LegacyConfiguration($"config({{ frontEnd: {{ constructAndSaveBindingFingerprint: {configCode} }}, }});")
                .EvaluateWithNoErrors();
        }

        [Fact]
        public void ValidateCompleteness()
        {
            // TODO: Dig into whether ipcSend should be allowed in config
            HashSet<string> ignoredMethods = new HashSet<string>()
            {
                "ScheduleProcessPip",
                "WriteDataCore",
                "GetBuildXLBinDirectoryToBeDeprecated",
                "GetNewIpcMoniker"
            };

            // Extract string parameter for each InlineDataAttribute for each method.
            string[] inlineDataContent = this.GetType().GetMethods()
                .SelectMany(mi => mi.GetCustomAttributes<InlineDataAttribute>().Select(attr => (string)attr?.GetData(null)?.FirstOrDefault()[0]))
                .Select(s => s.Substring(0, s.IndexOf("(")))
                .ToArray();

            Assert.All(GetAmbientMethods(AmbientContext.ContextName, typeof(AmbientContext))
                .Concat(GetAmbientMethods(AmbientTransformerOriginal.Name, typeof(AmbientTransformerOriginal)))
                .Concat(GetAmbientMethods(AmbientContract.ContractName, typeof(AmbientContract)))
                .Concat(GetAmbientMethods(AmbientFile.FileName, typeof(AmbientFile))),
                funcName => inlineDataContent.Contains(funcName));

            IEnumerable<string> GetAmbientMethods(string name, System.Type type)
            {
                return type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(mi => mi.ReturnType == typeof(EvaluationResult) && !ignoredMethods.Contains(mi.Name))
                    .Select(mi => $"{name}.{mi.Name}");
            }
        }
    }
}
