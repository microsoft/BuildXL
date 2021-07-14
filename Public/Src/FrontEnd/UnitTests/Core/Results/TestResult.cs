// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Values;
using static BuildXL.Utilities.FormattableStringEx;
using BuildXL.Utilities.Configuration;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Generic class that holds results of parsing/evaluation with potential set of diagnostics.
    /// </summary>
    public class TestResult<T> : TestResultBase
        where T : class
    {
        /// <nodoc />
        public TestResult(IReadOnlyList<Diagnostic> diagnostics)
            : base(diagnostics)
        {
            Contract.Requires(diagnostics != null);
            Contract.Requires(diagnostics.Count != 0, "Test result should have at least one diagnostic");
        }

        /// <nodoc />
        public TestResult(T result)
        {
            Contract.Requires(result != null);

            Result = result;
        }

        /// <nodoc />
        public TestResult(T result, IReadOnlyList<Diagnostic> diagnostics)
            : base(diagnostics)
        {
            Contract.Requires(result != null || diagnostics?.Count != 0, "Result or diagnostics should be provided");
            Result = result;
        }

        /// <summary>
        /// Result of parsing/evaluation.
        /// </summary>
        public T Result { get; }
    }

    /// <summary>
    /// Result of the parsing or evaluation.
    /// </summary>
    public sealed class TestResult : TestResult<object[]>
    {
        public TestResult(Diagnostic[] diagnostics)
            : base(diagnostics)
        {
        }

        public TestResult(object[] result, Diagnostic[] diagnostics, IConfiguration configuration, FileModuleLiteral fileModuleLiteral = null, SourceFile sourceFile = null)
            : base(result ?? CollectionUtilities.EmptyArray<object>(), diagnostics)
        {
            FileModuleLiteral = fileModuleLiteral;
            SourceFile = sourceFile;
            Configuration = configuration;
        }

        /// <summary>
        /// Returns a list of evaluated values.
        /// </summary>
        public IReadOnlyList<object> Values => Result;

        /// <nodoc />
        public int ValueCount => Values?.Count ?? 0;

        /// <nodoc />
        public FileModuleLiteral FileModuleLiteral { get; }

        /// <nodoc />
        public SourceFile SourceFile { get; }

        /// <nodoc />
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Returns instance of <see cref="TestResult" /> for invocation with potential results and potential failure.
        /// </summary>
        public static TestResult Create(object[] values, IEnumerable<Diagnostic> diagnostics, IConfiguration configuration, FileModuleLiteral moduleLiteral = null, SourceFile sourceFile = null)
        {
            Contract.Requires(diagnostics != null);

            return new TestResult(values, diagnostics.ToArray(), configuration, moduleLiteral, sourceFile);
        }

        /// <summary>
        /// Returns instance of <see cref="TestResult" /> for invocation with failures.
        /// </summary>
        public static TestResult FromErrors(IEnumerable<Diagnostic> errors)
        {
            Contract.Requires(errors != null);
            return new TestResult(errors.ToArray());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string valuesAsString = Values == null ? "empty" : string.Join(", ", Values);
            string diagnosticsAsString = Diagnostics.Count == 0 ? "empty" : string.Join("\r\n", Diagnostics);
            return I($"Values: {valuesAsString}\r\nDiagnostics: {diagnosticsAsString}");
        }
    }
}
