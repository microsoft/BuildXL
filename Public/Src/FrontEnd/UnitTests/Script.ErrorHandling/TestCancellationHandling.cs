// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using BuildXL.FrontEnd.Core.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Pips.Filter;
using BuildXL.Utilities;
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
