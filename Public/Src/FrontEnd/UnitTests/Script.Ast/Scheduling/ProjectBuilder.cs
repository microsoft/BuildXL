// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;

namespace Test.DScript.Ast.Scheduling
{
    /// <summary>
    /// Allows for adding project files and scheduling using <see cref="PipSchedulingTestBase{TProject, TResolverSettings}"/> in a fluent-like style
    /// </summary>
    public sealed class ProjectBuilder<TProject, TResolverSettings> 
        where TProject : IProjectWithDependencies<TProject>
        where TResolverSettings : class, IProjectGraphResolverSettings
    {
        private readonly HashSet<TProject> m_projects;
        private readonly PipSchedulingTestBase<TProject, TResolverSettings> m_testBase;
        private readonly TResolverSettings m_resolverSettings;
        private readonly QualifierId m_qualifierId;
        private readonly QualifierId[] m_requestedQualifiers;

        /// <nodoc/>
        public ProjectBuilder(PipSchedulingTestBase<TProject, TResolverSettings> testBase, TResolverSettings resolverSettings, QualifierId currentQualifier, QualifierId[] requestedQualifiers)
        {
            Contract.Requires(testBase != null);
            Contract.Requires(resolverSettings != null);
            Contract.Requires(currentQualifier != QualifierId.Invalid);
            Contract.Requires(requestedQualifiers?.Length > 0);

            m_projects = new HashSet<TProject>();
            m_testBase = testBase;
            m_resolverSettings = resolverSettings;
            m_qualifierId = currentQualifier;
            m_requestedQualifiers = requestedQualifiers;
        }

        /// <summary>
        /// Projects should be added in orded, where all dependencies have to be added before dependents
        /// </summary>
        public ProjectBuilder<TProject, TResolverSettings> Add(TProject project)
        {
            Contract.Requires(project != null);
            m_projects.Add(project);
            return this;
        }

        /// <nodoc/>
        public SchedulingResult<TProject> ScheduleAll()
        {
            return m_testBase.ScheduleAll(m_resolverSettings, m_projects, m_qualifierId, m_requestedQualifiers);
        }
    }
}
