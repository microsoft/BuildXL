// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Scheduler.Filter;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Core.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;
using DsTracing = BuildXL.FrontEnd.Script.Tracing;

namespace Test.DScript.Ast.ErrorHandling
{
    public abstract class TestCancellationHandling : DScriptV2Test
    {
        private const string SpecContent = "export const x = 42;";

        protected CancellationTokenSource TokenSource { get; } = new CancellationTokenSource();

        protected abstract int ExpectedDiagnosticId { get; }

        public TestCancellationHandling(ITestOutputHelper output) : base(output)
        {
        }

        protected override FrontEndContext CreateFrontEndContext(PathTable pathTable, IFileSystem fileSystem)
        {
            return FrontEndContext.CreateInstanceForTesting(pathTable: pathTable, fileSystem: fileSystem, cancellationToken: TokenSource.Token);
        }

        [Fact]
        public void TestCancellation()
        {
            BuildWithPrelude()
                .AddSpec(SpecContent)
                .EvaluateWithDiagnosticId(ExpectedDiagnosticId);
        }
    }

    public sealed class TestCancellationBeforeParse : TestCancellationHandling
    {
        public TestCancellationBeforeParse(ITestOutputHelper output) : base(output) { }

        protected override void BeforeBuildWorkspaceHook() => TokenSource.Cancel();

        protected override int ExpectedDiagnosticId => (int)LogEventId.FrontEndBuildWorkspacePhaseCanceled;
    }

    public sealed class TestCancellationBeforeAnalyze : TestCancellationHandling
    {
        public TestCancellationBeforeAnalyze(ITestOutputHelper output) : base(output) { }

        protected override void BeforeAnalyzeHook() => TokenSource.Cancel();

        protected override int ExpectedDiagnosticId => (int)LogEventId.FrontEndWorkspaceAnalysisPhaseCanceled;
    }

    public sealed class TestCancellationBeforeConvert : TestCancellationHandling
    {
        public TestCancellationBeforeConvert(ITestOutputHelper output) : base(output) { }

        protected override void BeforeConvertHook() => TokenSource.Cancel();

        protected override int ExpectedDiagnosticId => (int)LogEventId.FrontEndConvertPhaseCanceled;
    }

    public sealed class TestCancellationBeforeEvaluate : TestCancellationHandling
    {
        public TestCancellationBeforeEvaluate(ITestOutputHelper output) : base(output) { }

        protected override void BeforeEvaluateHook() => TokenSource.Cancel();
        
        protected override int ExpectedDiagnosticId => (int)DsTracing.LogEventId.EvaluationCanceled;
    }
}
