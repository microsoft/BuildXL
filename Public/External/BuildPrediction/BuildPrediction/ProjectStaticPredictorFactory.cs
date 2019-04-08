// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Creates instances of <see cref="IProjectStaticPredictor"/> for use with
    /// <see cref="ProjectStaticPredictionExecutor"/>.
    /// </summary>
    public static class ProjectStaticPredictorFactory
    {
        /// <summary>
        /// Creates a collection of all instances of <see cref="IProjectStaticPredictor"/> contained in the Microsoft.Build.Prediction assembly.
        /// </summary>
        /// <param name="msBuildHintPath">
        /// When not null, this path is used to load MSBuild Microsoft.Build.* assemblies if
        /// they have not already been loaded into the appdomain. When null, the fallback
        /// is to look for an installed copy of Visual Studio on Windows.
        /// </param>
        /// <returns>A collection of <see cref="IProjectStaticPredictor"/>.</returns>
        public static IReadOnlyCollection<IProjectStaticPredictor> CreateStandardPredictors(string msBuildHintPath)
        {
            // Ensure the user has MSBuild loaded into the appdomain.
            MsBuildEnvironment.Setup(msBuildHintPath);

            Type[] types = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Contains(typeof(IProjectStaticPredictor)))
                .ToArray();
            var predictors = new IProjectStaticPredictor[types.Length];

            // Use default constructor.
            Parallel.For(0, types.Length, i =>
                predictors[i] = (IProjectStaticPredictor)Activator.CreateInstance(types[i]));

            return predictors;
        }
    }
}
