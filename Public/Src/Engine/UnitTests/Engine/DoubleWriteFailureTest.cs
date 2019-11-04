// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Engine;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.EngineTests
{

    public sealed class DoubleWriteFailureTests : BaseEngineTest
    {
        public DoubleWriteFailureTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void DoubleWriteFailure()
        {
            string spec = @"
import {Cmd, Transformer} from 'Sdk.Transformers';

const step1 = Transformer.writeFile(p`obj/a.txt`, 'A');
const step2 = Transformer.execute({
    tool: {" +
        $"exe: f`{(OperatingSystemHelper.IsUnixOS ? "/bin/sh" : @"${Environment.getPathValue(""COMSPEC"")}")}`"
    + @"},
    workingDirectory: d`.`,
    arguments: [ Cmd.rawArgument('" + $"{(OperatingSystemHelper.IsUnixOS ? "-c echo step2 > obj/a.txt" : @"/d /c echo step2 > obj\a.txt")}" + @"'),
    ],
    outputs: [
        p`obj/a.txt`,
    ],
});
";
            AddModule("TestModule", ("test.dsc", spec), placeInRoot: true );
            RunEngine(expectSuccess: false);

            AssertErrorEventLogged(EventId.InvalidOutputDueToSimpleDoubleWrite);
        }
    }
}
