// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Implementations of this interface are run in parallel against a single evaluated MSBuild Project
    /// file to predict, prior to execution of a build, file, directory/folder, and glob patterns for
    /// build inputs, and output directories written by the project.
    ///
    /// The resulting inputs, if any, are intended to feed into build caching algorithms to provide an
    /// initial hash for cache lookups. Inputs need not be 100% complete on a per-project basis, but
    /// more accuracy and completeness leads to better cache performance.The output directories provide
    /// guidance to build execution sandboxing to allow better static analysis of the effects of
    /// executing the Project.
    /// </summary>
    public interface IProjectStaticPredictor
    {
        /// <summary>
        /// Performs static prediction of build inputs and outputs for use by caching and sandboxing.
        /// This method may be executing on multiple threads simultaneously and should act as a
        /// pure method transforming its inputs into zero or more predictions in a thread-safe
        /// and idempotent fashion.
        /// </summary>
        /// <param name="project">The MSBuild <see cref="Microsoft.Build.Evaluation.Project"/> to use for predictions.</param>
        /// <param name="projectInstance">
        /// A <see cref="Microsoft.Build.Execution.ProjectInstance"/> derived from the the Project.
        /// </param>
        /// <param name="repositoryRootDirectory">
        /// The filesystem directory containing the source code of the repository. For Git submodules
        /// this is typically the directory of the outermost containing repository.
        /// </param>
        /// <param name="predictions">
        /// A <see cref="StaticPredictions"/> instance, whose collections can be empty. This value is allowed
        /// to be null which indicates an empty set result. This value should be null when returning false
        /// from this method.
        /// </param>
        /// <returns>
        /// True if the predictor found inputs or outputs to predict. When false,
        /// <paramref name="predictions"/> should be null.
        /// </returns>
        /// <remarks>
        /// Non-async since this should not require I/O, just CPU when examining the Project.
        /// </remarks>
        bool TryPredictInputsAndOutputs(
            Project project,
            ProjectInstance projectInstance,
            string repositoryRootDirectory,
            out StaticPredictions predictions);
    }
}
