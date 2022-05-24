// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Execution.Analyzer;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;

namespace Test.Tool.Analyzers
{
    public class DevAnalyzerTests : TemporaryStorageTestBase
    {
        public DevAnalyzerTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Tests basic functionality with any pip to ensure that is dumped by the analyzer.
        /// </summary>
        [Fact]
        public void RunAnalyzer()
        {
            
            Program.Main(new string[]
            {
                @"/gd:E:\bin\Domino.zip",
                "/m:Dev",
                $"/o:{TestOutputDirectory}/output.txt"
            });
        }
    }
}
