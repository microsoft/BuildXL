// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.FrontEnd.Ninja.Infrastructure;

namespace Test.BuildXL.FrontEnd.MsBuild.Infrastructure
{
    /// <summary>
    /// Allows for adding project files and scheduling using <see cref="NinjaPipSchedulingTestBase"/> in a fluent-like style
    /// </summary>
    public sealed class NinjaSchedulingProjectBuilder
    {
        private readonly IList<NinjaNode> m_projects;
        private readonly NinjaPipSchedulingTestBase m_test;
        private readonly NinjaResolverSettings m_resolverSettings;
        private readonly QualifierId m_qualifierId;

        /// <nodoc/>
        public NinjaSchedulingProjectBuilder(NinjaPipSchedulingTestBase test, NinjaResolverSettings resolverSettings, QualifierId qualifierId)
        {
            Contract.Requires(test != null);
            Contract.Requires(resolverSettings != null);

            m_projects = new List<NinjaNode>();
            m_test = test;
            m_resolverSettings = resolverSettings;
            m_qualifierId = qualifierId;
        }

        /// <summary>
        /// Projects should be added in topological order
        /// </summary>
        public NinjaSchedulingProjectBuilder Add(NinjaNode node)
        {
            Contract.Requires(node != null);
            m_projects.Add(node);
            return this;
        }

        /// <nodoc/>
        internal NinjaSchedulingResult ScheduleAll()
        {
            return m_test.ScheduleAll(m_resolverSettings, m_projects, m_qualifierId);
        }

        public NinjaSchedulingProjectBuilder AddAll(params NinjaNode[] ninjaNodes)
        {
            foreach (var node in ninjaNodes)
            {
                Add(node);
            }

            return this;
        }
    }
}
