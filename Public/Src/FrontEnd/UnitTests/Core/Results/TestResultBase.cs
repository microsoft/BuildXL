// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Sdk.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// A wrapper for a list of DScript errors produced as a result of
    /// parsing or interpreting a DScript specification.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public abstract class TestResultBase
    {
        /// <nodoc />
        protected TestResultBase(IReadOnlyList<Diagnostic> diagnostics)
        {
            Contract.Requires(diagnostics != null);

            Diagnostics = diagnostics;
        }

        /// <nodoc />
        protected TestResultBase()
        {
            Diagnostics = CollectionUtilities.EmptyArray<Diagnostic>();
        }

        /// <nodoc />
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <nodoc />
        public IReadOnlyList<Diagnostic> Errors => Diagnostics.Where(d => d.Level.IsError()).ToList();

        /// <nodoc />
        public IReadOnlyList<Diagnostic> Warnings => Diagnostics.Where(d => d.Level == EventLevel.Warning).ToList();

        /// <summary>
        /// Returns true if parsing/evaluation had any errors.
        /// </summary>
        public bool HasError => Errors.Any();

        /// <summary>
        /// Returns true if parsing/evaluation had any warnings.
        /// </summary>
        public bool HasWarnings => Warnings.Any();

        /// <nodoc />
        public int ErrorCount => Errors.Count;

        /// <nodoc />
        public int WarningCount => Warnings.Count;

        /// <nodoc />
        public override string ToString()
        {
            if (!HasError)
            {
                return "CORRECT";
            }

            var result = new StringBuilder();
            if (Diagnostics.Any())
            {
                result.AppendLine(I($"Diagnostics ({Diagnostics.Count}):"));
                foreach (var d in Diagnostics)
                {
                    result.AppendLine(I($"\t{d}"));
                }
            }

            return result.ToString();
        }
    }
}
