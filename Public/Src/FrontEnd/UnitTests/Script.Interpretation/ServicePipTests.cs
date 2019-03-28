// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.TestEnv;

namespace Test.DScript.Ast.Interpretation
{
    public class ServicePipTests : DsTest
    {
        public ServicePipTests(ITestOutputHelper output) : base(output) { }

        protected override IPipGraph GetPipGraph() => new TestPipGraph();

        [Fact]
        public void TestServiceId()
        {
            string code = @"
import {Transformer} from 'Sdk.Transformers';

const tool = { exe: f`dummy.exe` };

const out = Context.getNewOutputDirectory('d1');

const shutdownCmd: Transformer.ExecuteArguments = {
    tool: tool,
    arguments: [],
    workingDirectory: out
};

const servicePip = Transformer.createService({
    tool: tool,
    arguments: [],
    workingDirectory: out,
    serviceShutdownCmd: shutdownCmd 
});

export const result = servicePip.serviceId;
";
            var result = EvaluateExpressionWithNoErrors(code, "result");
            Assert.NotEqual(UndefinedValue.Instance, result);
        }

        [Fact]
        public void TestServicePipWithServiceDependencies()
        {
            string code = @"
import {Transformer} from 'Sdk.Transformers';

const tool = { exe: f`dummy.exe` };

const out = Context.getNewOutputDirectory('d1');

const shutdownCmd: Transformer.ExecuteArguments = {
    tool: tool,
    arguments: [],
    workingDirectory: out
};

const servicePip1 = Transformer.createService({
    tool: tool,
    arguments: [],
    workingDirectory: out,
    serviceShutdownCmd: shutdownCmd
});

const servicePip2 = Transformer.createService(<any>{
    tool: tool,
    arguments: [],
    workingDirectory: out,
    serviceShutdownCmd: shutdownCmd, 
    // the line below doesn't typecheck, so here we are testing that when the 
    // typechecker is off we don't fail with a Contract exception
    servicePipDependencies: [ servicePip1.serviceId ] 
});
";
            EvaluateExpressionWithNoErrors(code, "servicePip2");
        }

        [Fact]
        public void TestFailServiceWithNoShutdownCmd()
        {
            string code = @"
        import {Transformer} from 'Sdk.Transformers';

        const servicePip = Transformer.createService({
            tool: { exe: f`dummy.exe` },
            arguments: [],
            workingDirectory: Context.getNewOutputDirectory('d1'),
        });
        ";
            EvaluateWithDiagnosticId(code, LogEventId.UnexpectedValueTypeOnConversion);
        }

        [Fact]
        public void TestExplicitEmptyServicePipDependencies()
        {
            string code = @"
        import {Transformer} from 'Sdk.Transformers';

        const pip = Transformer.execute({
            tool: { exe: f`dummy.exe` },
            arguments: [],
            servicePipDependencies: [],
            workingDirectory: Context.getNewOutputDirectory('d1'),
        });
        ";
            EvaluateExpressionWithNoErrors(code, "pip");
        }

        [Fact]
        public void TestServicePipWithExplicitEmptyServicePipDependencies()
        {
            string code = @"
        import {Transformer} from 'Sdk.Transformers';

        const tool = { exe: f`dummy.exe` };        

        const out = Context.getNewOutputDirectory('d1');

        const shutdownCmd: Transformer.ExecuteArguments = {
            tool: tool,
            arguments: [],
            workingDirectory: out
        };

        const servicePip = Transformer.createService(<any>{
            tool: tool,
            arguments: [],
            workingDirectory: out,
            serviceShutdownCmd: shutdownCmd,
            // the line below doesn't typecheck, so here we are testing that when the 
            // typechecker is off we don't fail with a Contract exception
            servicePipDependencies: []
        });

        export const result = servicePip.serviceId;
        ";
            EvaluateExpressionWithNoErrors(code, "result");
        }
    }
}
