// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Evaluates the subset of pip filters that can be decided for a single pip as it is admitted to a growing graph.
    /// </summary>
    /// <remarks>
    /// Every supported filter only depends on the admitted pip itself and only selects that pip's artifacts, so
    /// evaluating a pip in isolation gives the same result as the finalized-graph evaluation of the same filter.
    /// Constructs that need other pips (closures, dependents, negation) are rejected by <see cref="TryValidate"/>.
    /// </remarks>
    public sealed class DynamicFilterEvaluator
    {
        private readonly IDynamicGraph m_graph;
        private readonly PathTable m_pathTable;
        private readonly PipFilter m_root;

        /// <summary>
        /// Creates an evaluator for a filter previously accepted by <see cref="TryValidate"/>.
        /// </summary>
        public DynamicFilterEvaluator(RootFilter filter, IDynamicGraph graph, PathTable pathTable)
        {
            Contract.Requires(filter != null);
            Contract.Requires(!filter.IsEmpty);
            Contract.Requires(graph != null);
            Contract.Requires(pathTable != null);

            m_graph = graph;
            m_pathTable = pathTable;
            m_root = filter.PipFilter;
        }

        /// <summary>
        /// Rejects constructs whose result cannot be decided from a single admitted pip.
        /// </summary>
        public static bool TryValidate(PipFilter filter, out string unsupportedConstruct)
        {
            Contract.Requires(filter != null);

            switch (filter)
            {
                case EmptyFilter:
                case TagFilter:
                case MultiTagsOrFilter:
                case InputFileFilter:
                case OutputFileFilter:
                case ModuleFilter:
                case PipIdFilter:
                    unsupportedConstruct = null;
                    return true;
                case ValueFilter valueFilter when !valueFilter.IsTransitive:
                    unsupportedConstruct = null;
                    return true;
                case ValueFilter:
                    unsupportedConstruct = "valuetransitive";
                    return false;
                case SpecFileFilter specFilter when specFilter.IsValueTransitive:
                    unsupportedConstruct = "spec_valuetransitive";
                    return false;
                case SpecFileFilter specFilter when specFilter.IncludesReferencedSpecs:
                    unsupportedConstruct = "specref";
                    return false;
                case SpecFileFilter:
                    unsupportedConstruct = null;
                    return true;
                case NegatingFilter:
                    unsupportedConstruct = "negation (~)";
                    return false;
                case DependentsFilter:
                    unsupportedConstruct = "dpt";
                    return false;
                case CopyDependentsFilter:
                    unsupportedConstruct = "copydpt";
                    return false;
                case DependenciesFilter dependenciesFilter:
                    // The closure of a pip includes pips admitted before the match, so deciding a pip requires
                    // evaluating the inner filter across admissions. This might be supported in the future.
                    unsupportedConstruct = dependenciesFilter.ClosureMode == ClosureMode.TransitiveIncludingSelf
                        ? "dpc"
                        : "requiredfor";
                    return false;
                case BinaryFilter binaryFilter:
                    if (!TryValidate(binaryFilter.Left, out unsupportedConstruct))
                    {
                        return false;
                    }

                    return TryValidate(binaryFilter.Right, out unsupportedConstruct);
                default:
                    unsupportedConstruct = filter.GetType().Name;
                    return false;
            }
        }

        /// <summary>
        /// Evaluates one newly admitted pip and returns the artifacts of that pip that satisfy the filter.
        /// </summary>
        public HashSet<FileOrDirectoryArtifact> Admit(Pip pip)
        {
            Contract.Requires(pip != null);
            return Evaluate(m_root, pip);
        }

        private HashSet<FileOrDirectoryArtifact> Evaluate(PipFilter filter, Pip pip)
        {
            if (filter is not BinaryFilter binary)
            {
                return EvaluateLeaf(filter, pip);
            }

            var result = Evaluate(binary.Left, pip);
            var right = Evaluate(binary.Right, pip);

            if (binary.FilterOperator == FilterOperator.Or)
            {
                result.UnionWith(right);
            }
            else
            {
                result.IntersectWith(right);
            }

            return result;
        }

        private HashSet<FileOrDirectoryArtifact> EvaluateLeaf(PipFilter filter, Pip pip)
        {
            var candidates = new HashSet<FileOrDirectoryArtifact>();

            switch (filter)
            {
                case TagFilter tagFilter when tagFilter.Matches(pip):
                case MultiTagsOrFilter multiTagsFilter when multiTagsFilter.Matches(pip):
                case PipIdFilter pipIdFilter when pipIdFilter.Matches(pip):
                case ModuleFilter moduleFilter when moduleFilter.Matches(pip):
                case SpecFileFilter specFilter when specFilter.Matches(pip, m_pathTable):
                case ValueFilter valueFilter when valueFilter.Matches(pip):
                    AddOutputs(pip, candidates);
                    break;
                case InputFileFilter inputFilter when MatchesInput(inputFilter, pip):
                    AddOutputs(pip, candidates);
                    break;
                case OutputFileFilter outputFilter:
                    AddMatchingOutputs(outputFilter, pip, candidates);
                    break;
            }

            return candidates;
        }

        private bool MatchesInput(InputFileFilter filter, Pip pip)
        {
            switch (pip)
            {
                case CopyFile copyFile:
                    return filter.PathMatches(copyFile.Source, m_pathTable);
                case Process process:
                    if (filter.PathMatches(process.Executable, m_pathTable))
                    {
                        return true;
                    }

                    foreach (var dependency in process.Dependencies)
                    {
                        if (filter.PathMatches(dependency.Path, m_pathTable))
                        {
                            return true;
                        }
                    }

                    foreach (var directoryDependency in process.DirectoryDependencies)
                    {
                        var seal = (SealDirectory)m_graph.GetPipFromPipId(
                            m_graph.GetSealedDirectoryNode(directoryDependency).ToPipId());
                        foreach (var member in seal.Contents)
                        {
                            if (filter.PathMatches(member.Path, m_pathTable))
                            {
                                return true;
                            }
                        }

                        bool topLevelOnly =
                            seal.Kind != SealDirectoryKind.SourceAllDirectories &&
                            seal.Kind != SealDirectoryKind.Opaque &&
                            seal.Kind != SealDirectoryKind.SharedOpaque;
                        if (filter.DirectoryPathMatches(seal.DirectoryRoot, topLevelOnly, m_pathTable) &&
                            (!seal.Kind.IsSourceSeal() || filter.MatchWithSourceSealPatterns(seal, m_pathTable)))
                        {
                            return true;
                        }
                    }

                    break;
            }

            return false;
        }

        private void AddMatchingOutputs(
            OutputFileFilter filter,
            Pip pip,
            HashSet<FileOrDirectoryArtifact> outputs)
        {
            ForEachOutput(
                pip,
                output =>
                {
                    if ((output.IsFile && filter.PathMatches(output.Path, m_pathTable)) ||
                        (output.IsDirectory &&
                         output.DirectoryArtifact.IsOutputDirectory() &&
                         filter.DirectoryPathMatches(output.Path, topLevelOnly: false, m_pathTable)))
                    {
                        outputs.Add(output);
                    }
                });
        }

        private static void AddOutputs(Pip pip, HashSet<FileOrDirectoryArtifact> outputs)
        {
            ForEachOutput(pip, output => outputs.Add(output));
        }

        private static void ForEachOutput(Pip pip, Action<FileOrDirectoryArtifact> action)
        {
            PipArtifacts.ForEachOutput(
                pip,
                output =>
                {
                    action(output);
                    return true;
                },
                getExistenceAssertionsUnderOpaqueDirectory: _ => CollectionUtilities.EmptySet<FileArtifact>(),
                includeUncacheable: true);
        }
    }
}
