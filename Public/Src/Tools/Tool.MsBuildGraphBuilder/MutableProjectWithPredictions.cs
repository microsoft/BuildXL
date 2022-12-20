// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.MsBuild.Serialization;

namespace ProjectGraphBuilder
{
    /// <summary>
    /// Mutable version of <see cref="ProjectWithPredictions{TPathType}"/>.
    /// </summary>
    internal class MutableProjectWithPredictions
    {
        private readonly HashSet<MutableProjectWithPredictions> m_dependencies;
        private readonly HashSet<MutableProjectWithPredictions> m_dependents;
        private readonly HashSet<string> m_predictedInputFiles;
        private readonly HashSet<string> m_predictedOutputFolders;
        private PredictedTargetsToExecute m_predictedTargetsToExecute;

        /// <nodoc/>
        public string FullPath { get; }
        
        /// <nodoc/>
        public GlobalProperties GlobalProperties { get; set; }

        /// <nodoc/>
        public bool ImplementsTargetProtocol { get; set; }

        /// <nodoc/>
        public IEnumerable<string> PredictedInputFiles => m_predictedInputFiles;

        /// <nodoc/>
        public IEnumerable<string> PredictedOutputFolders => m_predictedOutputFolders;

        /// <nodoc/>
        public IEnumerable<MutableProjectWithPredictions> Dependencies => m_dependencies;

        /// <nodoc/>
        public IEnumerable<MutableProjectWithPredictions> Dependents => m_dependents;

        /// <nodoc/>
        public PredictedTargetsToExecute PredictedTargetsToExecute
        {
            get => m_predictedTargetsToExecute;
            set
            {
                Contract.Requires(value != null);
                m_predictedTargetsToExecute = value;
            }
        }

        public MutableProjectWithPredictions(
            string fullPath,
            bool implementsTargetProtocol,
            GlobalProperties globalProperties,
            HashSet<string> predictedInputFiles,
            HashSet<string> predictedOutputFolders,
            PredictedTargetsToExecute predictedTargetsToExecute = null,
            HashSet<MutableProjectWithPredictions> projectReferences = null,
            HashSet<MutableProjectWithPredictions> referencingProjects = null)
        {
            Contract.Requires(globalProperties != null);
            Contract.Requires(predictedInputFiles != null);
            Contract.Requires(predictedOutputFolders != null);

            FullPath = fullPath;
            ImplementsTargetProtocol = implementsTargetProtocol;
            GlobalProperties = globalProperties;
            m_predictedInputFiles = predictedInputFiles;
            m_predictedOutputFolders = predictedOutputFolders;
            m_predictedTargetsToExecute = predictedTargetsToExecute ?? PredictedTargetsToExecute.CreateEmpty();
            m_dependencies = projectReferences ?? new HashSet<MutableProjectWithPredictions>();
            m_dependents = referencingProjects ?? new HashSet<MutableProjectWithPredictions>();
        }

        /// <summary>
        /// Adds dependencies.
        /// </summary>
        public void AddDependencies(IEnumerable<MutableProjectWithPredictions> projectDependencies) => m_dependencies.UnionWith(projectDependencies);

        /// <summary>
        /// Adds dependency.
        /// </summary>
        public void AddDependency(MutableProjectWithPredictions projectDependency) => m_dependencies.Add(projectDependency);

        /// <summary>
        /// Removes dependency.
        /// </summary>
        public void RemoveDependency(MutableProjectWithPredictions projectDependency) => m_dependencies.Remove(projectDependency);

        /// <summary>
        /// Adds dependents.
        /// </summary>
        public void AddDependents(IEnumerable<MutableProjectWithPredictions> projectDependents) => m_dependents.UnionWith(projectDependents);

        /// <summary>
        /// Adds dependency.
        /// </summary>
        public void AddDependent(MutableProjectWithPredictions projectDependent) => m_dependents.Add(projectDependent);

        /// <summary>
        /// Removes dependent.
        /// </summary>
        /// <param name="projectDependency"></param>
        public void RemoveDependent(MutableProjectWithPredictions projectDependent) => m_dependents.Remove(projectDependent);

        /// <summary>
        /// Remove all dependencies and dependents.
        /// </summary>
        public void MakeOrphan()
        {
            foreach (var dependent in m_dependents)
            {
                dependent.RemoveDependency(this);
            }

            foreach (var dependency in m_dependencies)
            {
                dependency.RemoveDependent(this);
            }

            m_dependencies.Clear();
            m_dependents.Clear();
        }

        /// <summary>
        /// Merges a project with this project by including the former's predicted inputs or outputs, dependents, and dependencies into this project.
        /// </summary>
        /// <param name="project">Project to be merged with this project.</param>
        /// <remarks>
        /// When merging the dependents, this project is added as a dependency of each dependent of <paramref name="project"/>.
        /// When merging the dependencies, this project is added as a depdendent of each dependency of <paramref name="project"/>.
        /// </remarks>
        public void Merge(MutableProjectWithPredictions project)
        {
            m_predictedInputFiles.UnionWith(project.m_predictedInputFiles);
            m_predictedOutputFolders.UnionWith(project.m_predictedOutputFolders);

            foreach (var dependent in project.m_dependents)
            {
                if (dependent != this)
                {
                    m_dependents.Add(dependent);
                    dependent.AddDependency(this);
                }
            }

            foreach (var dependency in project.m_dependencies)
            {
                m_dependencies.Add(dependency);
                dependency.AddDependent(this);
            }
        }
    }
}
