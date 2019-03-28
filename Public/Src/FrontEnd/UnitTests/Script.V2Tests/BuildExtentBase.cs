// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public class BuildExtentBase : DScriptV2Test
    {
        private readonly TestEnv.TestPipGraph m_pipGraph;

        public BuildExtentBase(ITestOutputHelper output) : base(output)
        {
            m_pipGraph = new TestEnv.TestPipGraph();
        }

        protected override IPipGraph GetPipGraph() => m_pipGraph;

        private const string TemplateWithPackagesBody = @"
        modules: [ f`src/package.config.dsc` ],
        disableDefaultSourceResolver: <<DDSR>>,
        resolvers: [{
            kind: 'SourceResolver',
            modules: [f`lib/package.config.dsc`, f`Sdk.Prelude/package.config.dsc`, f`Sdk.Transformers/package.config.dsc` ]
        }]";

        private const string TemplateWithProjectsBody = @"
        projects: [ f`src/package.dsc` ],
        disableDefaultSourceResolver: <<DDSR>>,
        resolvers: [{
            kind: 'SourceResolver',
            modules: [ f`lib/package.config.dsc`, f`Sdk.Prelude/package.config.dsc`, f`Sdk.Transformers/package.config.dsc` ]
        }]";

        protected const string ConfigTemplateWithPackages = "config({" + TemplateWithPackagesBody + "});";
        protected const string ConfigTemplateWithProjects = "config({" + TemplateWithProjectsBody + "});";

        private const string LibPackageContent = @"
import {Transformer} from 'Sdk.Transformers';

const outDir = Context.getNewOutputDirectory('out-lib');
export const file1 = Transformer.writeFile(p`${outDir}/f1.txt`, 'f1');
export const file2 = Transformer.writeFile(p`${outDir}/f2.txt`, 'f2');";

        private const string SrcPackageContent = @"
import {Transformer} from 'Sdk.Transformers';

export const copy1 = Transformer.copyFile(importFrom('Lib').file1, Context.getNewOutputDirectory('out-src').combine('f1-copied.txt'));";

        protected string[] GetOrderedValuePipValues(Pip[] pips)
        {
            return pips
                .Where(p => p.PipType == PipType.Value)
                .Cast<ValuePip>()
                .Select(p => p.Symbol.ToString(FrontEndContext.SymbolTable))
                .OrderBy(s => s)
                .ToArray();
        }

        protected SpecEvaluationBuilder CreateBuilder(string configTemplate, bool disableDefaultSourceResolver)
        {
            return new SpecEvaluationBuilder(this)
                .LegacyConfiguration(configTemplate.Replace("<<DDSR>>", disableDefaultSourceResolver.ToString().ToLowerInvariant()))
                .AddFullPrelude()
                .AddSpec(@"lib/package.config.dsc", V1Module("Lib"))
                .AddSpec(@"lib/package.dsc", LibPackageContent)
                .AddSpec(@"src/package.dsc", SrcPackageContent)
                .AddSpecIf(configTemplate != ConfigTemplateWithProjects, @"src/package.config.dsc", V1Module("Src"));
        }

        protected static void AssertPipTypeCount(Pip[] pips, PipType pipType, int expected)
        {
            XAssert.AreEqual(
                expected,
                pips.Where(p => p.PipType == pipType).Count(),
                $"Number of <{pipType}> pips doesn't match");
        }

        protected Pip[] GetPipsWithoutModuleAndSpec()
        {
            return GetPipGraph()
                .RetrieveScheduledPips()
                .Where(pip => pip.PipType != PipType.Module && pip.PipType != PipType.SpecFile)
                .ToArray();
        }
    }

    internal static class SpecEvaluationBuilderExtensions
    {
        public static SpecEvaluationBuilder AddSpecIf(this SpecEvaluationBuilder builder, bool condition, string specName, string specContent)
        {
            return condition
                ? builder.AddSpec(specName, specContent)
                : builder;
        }
    }
}
