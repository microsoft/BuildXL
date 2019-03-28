// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// Provider that gives file and module dependencies for a given spec.
    /// </summary>
    /// <remarks>
    /// File dependencies could be stored on disk or could be computed on the fly.
    /// This interface abstracts away the actual implementation.
    /// </remarks>
    public interface ISpecDependencyProvider
    {
        /// <summary>
        /// Gets a set of file that the given spec depends on.
        /// </summary>
        HashSet<AbsolutePath> GetFileDependenciesOf(AbsolutePath specPath);

        /// <summary>
        /// Gets a set of file that depends on the given spec.
        /// </summary>
        HashSet<AbsolutePath> GetFileDependentsOf(AbsolutePath specPath);

        /// <summary>
        /// Gets a set of module that the given spec depends on.
        /// </summary>
        HashSet<string> GetModuleDependenciesOf(AbsolutePath specPath);
    }

    /// <nodoc/>
    public static class DepedencyProviderExtensions
    {
        /// <summary>
        /// Functional variant of <see cref="ComputeReflectiveClosureOfDependentFiles(ISpecDependencyProvider, IEnumerable{AbsolutePath}, HashSet{AbsolutePath})"/>.
        /// </summary>
        public static HashSet<AbsolutePath> ComputeReflectiveClosureOfDependentFiles(this ISpecDependencyProvider provider, IEnumerable<AbsolutePath> roots)
        {
            var result = new HashSet<AbsolutePath>();
            ComputeReflectiveClosureOfDependentFiles(provider, roots, result);
            return result;
        }

        /// <summary>
        /// Computes reflective transitive closure all dependent files starting from <paramref name="roots"/>
        /// </summary>
        public static void ComputeReflectiveClosureOfDependentFiles(this ISpecDependencyProvider provider, IEnumerable<AbsolutePath> roots, HashSet<AbsolutePath> result)
        {
            ComputeReflectiveClosure(result, roots, provider, (provider2, path) => provider2.GetFileDependentsOf(path));
        }

        /// <summary>
        /// Functional variant of <see cref="ComputeReflectiveClosureOfDependencyFiles(ISpecDependencyProvider, IEnumerable{AbsolutePath}, HashSet{AbsolutePath})"/>.
        /// </summary>
        public static HashSet<AbsolutePath> ComputeReflectiveClosureOfDependencyFiles(this ISpecDependencyProvider provider, IEnumerable<AbsolutePath> roots)
        {
            var result = new HashSet<AbsolutePath>();
            ComputeReflectiveClosureOfDependencyFiles(provider, roots, result);
            return result;
        }

        /// <summary>
        /// Computes reflective transitive closure all dependency files starting from <paramref name="roots"/>
        /// </summary>
        public static HashSet<AbsolutePath> ComputeReflectiveClosureOfDependencyFiles(this ISpecDependencyProvider provider, IEnumerable<AbsolutePath> roots, HashSet<AbsolutePath> closureSoFar)
        {
            return ComputeReflectiveClosure(closureSoFar, roots, provider, (provider2, path) => provider2.GetFileDependenciesOf(path));
        }

        private static HashSet<TElem> ComputeReflectiveClosure<TElem, TEnv>(
            HashSet<TElem> closure,
            IEnumerable<TElem> roots,
            TEnv env,
            Func<TEnv, TElem, HashSet<TElem>> relation)
        {
            Contract.Requires(closure != null);
            Contract.Requires(relation != null);
            Contract.Requires(roots != null);

            var workList = new Stack<TElem>();

            AddMissing(roots, workList, closure);

            // TODO: parallelize
            while (workList.Any())
            {
                // remove first from work list
                var elem = workList.Pop();

                // get successors
                var successors = relation(env, elem);

                // add each successor that hasn't been visited to work list
                AddMissing(successors, workList, closure);
            }

            return closure;
        }

        private static void AddMissing<TElem>(IEnumerable<TElem> elems, Stack<TElem> workList, HashSet<TElem> closure)
        {
            foreach (var elem in elems)
            {
                if (closure.Add(elem))
                {
                    workList.Push(elem);
                }
            }
        }
    }
}
