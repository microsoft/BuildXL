// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// An immutable and simplified version of ProjectInstance decorated with StaticPredictions
    /// </summary>
    /// <remarks>
    /// The main purpose of this class is to represent an MsBuild node to be scheduled by BuildXL.
    /// This class is designed to be JSON serializable.
    /// The type for the path is parametric since, on the graph serialization process, this is just a string. On the BuildXL side, this becomes an AbsolutePath
    /// </remarks>
    public sealed class ProjectWithPredictions<TPathType>
    {
        [JsonProperty]
        private IReadOnlyCollection<ProjectWithPredictions<TPathType>> m_projectReferences;

        [JsonProperty]
        private PredictedTargetsToExecute m_predictedTargetsToExecute;

        /// <nodoc/>
        [JsonProperty(IsReference = false)]
        public TPathType FullPath { get; }

        /// <nodoc/>
        [JsonProperty(IsReference = false)]
        public GlobalProperties GlobalProperties { get; }

        /// <summary>
        /// Files predicted to be inputs
        /// </summary>
        public IReadOnlyCollection<TPathType> PredictedInputFiles { get; }

        /// <summary>
        /// Folders predicted to be outputs
        /// </summary>
        public IReadOnlyCollection<TPathType> PredictedOutputFolders{ get; }

        /// <summary>
        /// Whether the project implements the target protocol
        /// </summary>
        /// <remarks>
        /// See https://github.com/Microsoft/msbuild/blob/master/documentation/specs/static-graph.md
        /// </remarks>
        public bool ImplementsTargetProtocol { get; }

        /// <summary>
        /// Collection of targets to be executed on the project (based on the initial targets for the entry point project)
        /// </summary>
        [JsonIgnore()]
        public PredictedTargetsToExecute PredictedTargetsToExecute
        {
            get
            {
                Contract.Assert(m_predictedTargetsToExecute != null, "Predicted targets to execute are not set");
                return m_predictedTargetsToExecute;
            }
            private set => m_predictedTargetsToExecute = value;
        }

        /// <nodoc/>
        [JsonIgnore()]
        public IReadOnlyCollection<ProjectWithPredictions<TPathType>> ProjectReferences
        {
            get
            {
                Contract.Assert(m_projectReferences != null, "References are not set");
                return m_projectReferences;
            }
            private set => m_projectReferences = value;
        }

        /// <nodoc/>
        public ProjectWithPredictions(
            TPathType fullPath, 
            bool implementsTargetProtocol,
            GlobalProperties globalProperties,
            IReadOnlyCollection<TPathType> predictedInputFiles,
            IReadOnlyCollection<TPathType> predictedOutputFolders,
            PredictedTargetsToExecute predictedTargetsToExecute = null,
            IReadOnlyCollection<ProjectWithPredictions<TPathType>> projectReferences = null)
        {
            Contract.Requires(globalProperties != null);
            Contract.Requires(predictedInputFiles != null);
            Contract.Requires(predictedOutputFolders != null);

            FullPath = fullPath;
            ImplementsTargetProtocol = implementsTargetProtocol;
            GlobalProperties = globalProperties;
            PredictedInputFiles = predictedInputFiles;
            PredictedOutputFolders = predictedOutputFolders;
            PredictedTargetsToExecute = predictedTargetsToExecute;
            m_projectReferences = projectReferences;
        }

        /// <summary>
        /// When constructing the graph, instances of this class are created without knowing the references yet, so this allows for a way to set them after the fact.
        /// </summary>
        /// <remarks>
        /// This method should be called only once per instance
        /// </remarks>
        public void SetReferences(IReadOnlyCollection<ProjectWithPredictions<TPathType>> projectReferences)
        {
            Contract.Assert(projectReferences != null);
            Contract.Assert(m_projectReferences == null, "Project references can be set only once");

            m_projectReferences = projectReferences;
        }

        /// <summary>
        /// When constructing the graph under particular scenarios some instances of this class are created without knowing the targets to execute yet, so this allows for a way to set them after the fact.
        /// </summary>
        /// <remarks>
        /// This method should be called only once per instance
        /// </remarks>
        public void SetTargetsToExecute(PredictedTargetsToExecute predictedTargetsToExecute)
        {
            Contract.Assert(predictedTargetsToExecute != null);
            Contract.Assert(m_predictedTargetsToExecute == null, "Predicted targets to execute can be set only once");

            m_predictedTargetsToExecute = predictedTargetsToExecute;
        }
    }
}
