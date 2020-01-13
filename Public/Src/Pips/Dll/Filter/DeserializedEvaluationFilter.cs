// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Deserialized version of evaluation filter.
    /// </summary>
    /// <remarks>
    /// Filter comparison is happening before the symbol table is available. To work around this issue, the filter is serialized as text and compared using string comparison.
    /// </remarks>
    [DebuggerDisplay("{ToDisplayString(),nq}")]
    internal sealed class DeserializedEvaluationFilter : IEvaluationFilter
    {
        public DeserializedEvaluationFilter(IReadOnlyList<string> namesToResolve, IReadOnlyList<string> valueDefinitionRootsToResolve, IReadOnlyList<string> modulesToResolve)
        {
            ValueNamesToResolveAsStrings = namesToResolve;
            ValueDefinitionRootsToResolveAsStrings = valueDefinitionRootsToResolve;
            ModulesToResolveAsStrings = modulesToResolve;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ValueNamesToResolveAsStrings { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> ValueDefinitionRootsToResolveAsStrings { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> ModulesToResolveAsStrings { get; }

        /// <inheritdoc />
        public bool IsSubSetOf(IEvaluationFilter supersetCandidateFilter) => EvaluationFilter.IsSubSetOf(this, supersetCandidateFilter);

        /// <inheritdoc />
        public void Serialize(BinaryWriter writer) => EvaluationFilter.Serialize(this, writer);

        /// <inheritdoc />
        public string ToDisplayString()
        {
            return $"[{ModulesToResolveAsStrings.Count} module(s), {ValueDefinitionRootsToResolveAsStrings.Count} spec(s), {ValueNamesToResolveAsStrings.Count} value(s)]";
        }

        /// <inheritdoc />
        public IEvaluationFilter GetDeserializedFilter() => this;

        /// <inheritdoc />
        public bool CanPerformPartialEvaluation => ValueNamesToResolveAsStrings.Count > 0 || ValueDefinitionRootsToResolveAsStrings.Count > 0 || ModulesToResolveAsStrings.Count > 0;
    }
}
