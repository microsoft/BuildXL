// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.FrontEnd.MsBuild.Serialization;
using Newtonsoft.Json;

namespace Test.ProjectGraphBuilder.Utilities
{
    /// <summary>
    /// Can deserialize a  <see cref="ProjectGraphWithPredictions{string}"/> from a file
    /// </summary>
    public class SimpleDeserializer
    {
        private readonly JsonSerializer m_serializer;

        /// <nodoc/>
        public static SimpleDeserializer Instance = new SimpleDeserializer();

        private SimpleDeserializer()
        {
            m_serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);
        }

        /// <nodoc/>
        public ProjectGraphWithPredictionsResult<string> DeserializeGraph(string outputFile)
        {
            Contract.Requires(!string.IsNullOrEmpty(outputFile));

            using (var sr = new StreamReader(outputFile))
            using (var reader = new JsonTextReader(sr))
            {
                var projectGraphWithPredictionsResult = m_serializer.Deserialize<ProjectGraphWithPredictionsResult<string>>(reader);
                return projectGraphWithPredictionsResult;
            }
        }
    }
}
