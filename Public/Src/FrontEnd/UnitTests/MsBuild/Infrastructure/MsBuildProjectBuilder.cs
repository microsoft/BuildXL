// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.AbsolutePath>;

namespace Test.BuildXL.FrontEnd.MsBuild.Infrastructure
{
    /// <summary>
    /// Allows for adding project files and scheduling using <see cref="MsBuildPipSchedulingTestBase"/> in a fluent-like style
    /// </summary>
    public sealed class MsBuildProjectBuilder
    {
        private readonly HashSet<ProjectWithPredictions> m_projects;
        private readonly MsBuildPipSchedulingTestBase m_testBase;
        private readonly MsBuildResolverSettings m_resolverSettings;
        private readonly QualifierId m_qualifierId;

        /// <nodoc/>
        public MsBuildProjectBuilder(MsBuildPipSchedulingTestBase testBase, MsBuildResolverSettings resolverSettings, QualifierId qualifierId)
        {
            Contract.Requires(testBase != null);
            Contract.Requires(resolverSettings != null);
            Contract.Requires(qualifierId != QualifierId.Invalid);

            m_projects = new HashSet<ProjectWithPredictions>();
            m_testBase = testBase;
            m_resolverSettings = resolverSettings;
            m_qualifierId = qualifierId;
        }

        /// <summary>
        /// Projects should be added in orded, where all dependencies have to be added before dependents
        /// </summary>
        public MsBuildProjectBuilder Add(ProjectWithPredictions project)
        {
            Contract.Requires(project != null);
            m_projects.Add(project);
            return this;
        }

        /// <nodoc/>
        internal MsBuildSchedulingResult ScheduleAll()
        {
            return m_testBase.ScheduleAll(m_resolverSettings, m_projects, m_qualifierId);
        }
    }
}
