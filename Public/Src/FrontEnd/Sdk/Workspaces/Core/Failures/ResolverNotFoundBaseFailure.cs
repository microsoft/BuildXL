// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Abstract failure representing the case of a resolver not being found
    /// </summary>
    public abstract class ResolverNotFoundBaseFailure : WorkspaceFailure
    {
        private const int MaxMessageLength = 200;
        private readonly List<IWorkspaceModuleResolver> m_knownResolvers;

        /// <nodoc/>
        protected ResolverNotFoundBaseFailure(List<IWorkspaceModuleResolver> knownResolvers)
        {
            Contract.Requires(knownResolvers != null);

            m_knownResolvers = knownResolvers;
        }

        /// <nodoc/>
        protected abstract string DescribeResolverNotFound();

        /// <inheritdoc/>
        public override string Describe()
        {
            var resolverExtent = GetKnownResolversDescription();
            if (resolverExtent.Length > MaxMessageLength)
            {
                resolverExtent = resolverExtent.Substring(0, MaxMessageLength) + "...";
            }

            return DescribeResolverNotFound() + " " + resolverExtent;
        }

        private string GetKnownResolversDescription()
        {
            using (var builder = Pools.GetStringBuilder())
            {
                var sb = builder.Instance;

                sb.Append("Known modules are: ");
                foreach (var resolver in m_knownResolvers)
                {
                    sb.Append(I($"[{resolver.DescribeExtent()}], "));
                }

                return sb.ToString().TrimEnd(',', ' ');
            }
        }
    }

    /// <summary>
    /// A resolver was not found for a given module name
    /// </summary>
    public sealed class ResolverNotFoundForModuleNameFailure : ResolverNotFoundBaseFailure
    {
        private readonly ModuleReferenceWithProvenance m_moduleReference;

        /// <nodoc/>
        public ResolverNotFoundForModuleNameFailure(List<IWorkspaceModuleResolver> knownResolvers, ModuleReferenceWithProvenance moduleReference)
            : base(knownResolvers)
        {
            m_moduleReference = moduleReference;
        }

        /// <nodoc/>
        protected override string DescribeResolverNotFound()
        {
            var location = m_moduleReference.ReferencedFrom;
            return I($"{location.File}({location.Line},{location.Position}): No resolver was found that owns module '{m_moduleReference.Name}'.");
        }
    }

    /// <summary>
    /// A resolver was not found for a given module descriptor
    /// </summary>
    public sealed class ResolverNotFoundForModuleDescriptorFailure : ResolverNotFoundBaseFailure
    {
        private readonly ModuleDescriptor m_moduleDescriptor;

        /// <nodoc/>
        public ResolverNotFoundForModuleDescriptorFailure(List<IWorkspaceModuleResolver> knownResolvers, ModuleDescriptor moduleDescriptor)
            : base(knownResolvers)
        {
            m_moduleDescriptor = moduleDescriptor;
        }

        /// <nodoc/>
        protected override string DescribeResolverNotFound()
        {
            return I($"No resolver was found that owns module '{m_moduleDescriptor.Id}-{m_moduleDescriptor.DisplayName}'.");
        }
    }

    /// <summary>
    /// A resolver was not found for a given path to a spec
    /// </summary>
    public sealed class ResolverNotFoundForPathFailure : ResolverNotFoundBaseFailure
    {
        private readonly string m_pathToSpec;

        /// <nodoc/>
        public ResolverNotFoundForPathFailure(List<IWorkspaceModuleResolver> knownResolvers, string pathToSpec)
            : base(knownResolvers)
        {
            m_pathToSpec = pathToSpec;
        }

        /// <nodoc/>
        protected override string DescribeResolverNotFound()
        {
            return I($"No resolver was found that owns specification file '{m_pathToSpec}'.");
        }
    }
}
