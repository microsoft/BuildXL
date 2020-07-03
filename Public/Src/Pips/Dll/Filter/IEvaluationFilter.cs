// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Data used to perform filtering at Evaluation time.
    /// </summary>
    public interface IEvaluationFilter
    {
        /// <summary>
        /// Value names that can be resolved in a string form. If empty, all values must be resolved.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ValueNamesToResolveAsStrings { get; }

        /// <summary>
        /// Value definition roots to resolve in a string form. If empty, all definition sites must be resolved.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ValueDefinitionRootsToResolveAsStrings { get; }

        /// <summary>
        /// Module to resolve in a string form.  If empty, all modules must be resolved.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ModulesToResolveAsStrings { get; }

        /// <summary>
        /// Returns true if a current filter produces the graph that is a subset of the graph produced by the <paramref name="supersetCandidateFilter"/>.
        /// </summary>
        bool IsSubSetOf(IEvaluationFilter supersetCandidateFilter);

        /// <summary>
        /// Write the content of the filter to a writer.
        /// </summary>
        void Serialize(BinaryWriter writer);

        /// <summary>
        /// Returns a string representation of a filter.
        /// </summary>
        string ToDisplayString();

        /// <summary>
        /// Returns true if the evaluation filter can be used for partial evalaution.
        /// </summary>
        bool CanPerformPartialEvaluation { get; }

        /// <summary>
        /// Gets a deserialized filter that only has names, values, and modules as string.
        /// </summary>
        IEvaluationFilter GetDeserializedFilter();
    }
}
