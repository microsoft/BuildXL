using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core;
using ContentPlamentAnalysisTools.Core;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action takes a directory that contains all the outputs for an artifact an "linearize" it,
    /// so it will write a single row for an artifact in a csv file
    /// </summary>
    public class SampleArtifacts : TimedAction<SampleArtifactsInput, SampleArtifactsOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        /// <inheritdoc />
        protected override void CleanUp(SampleArtifactsInput input, SampleArtifactsOutput output){}        

        /// <summary>
        /// This action takes a set of artifacts (from a single build) and groups them in an accumulator
        /// </summary>
        protected override SampleArtifactsOutput Perform(SampleArtifactsInput input)
        {
            try
            {
                // start by scaling...
                return new SampleArtifactsOutput();
            }
            finally
            {
                // this guy will not log, this task is too small and that will hurt performance
            }
        }

        private Dictionary<int, int> Scale(MultiValueDictionary<int, MLArtifact> samples, int sampleSize)
        {
            var output = new Dictionary<int, int>();
            foreach(var entry in samples)
            {
                var queueCount = entry.Key;
                var entryCount = entry.Value.Count;
                var proportion = 1.0 * (entryCount * sampleSize) / (1.0 * entryCount);
                output[queueCount] = (int)Math.Ceiling(proportion);
            }
            return output;
        }

        /// <inheritdoc />
        protected override void Setup(SampleArtifactsInput input){}
    }

    /// <summary>
    /// The input for this action requires all the artifacts (linearized)
    /// </summary>
    public class SampleArtifactsInput
    {
        /// <summary>
        /// The number of artifacts per queue size to take
        /// </summary>
        public Dictionary<int, int> Scale { get; set; }
        /// <summary>
        /// The sampled artifacts
        /// </summary>
        public MultiValueDictionary<int, MLArtifact> Artifacts { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public SampleArtifactsInput(Dictionary<int, int> scale, MultiValueDictionary<int, MLArtifact> a)
        {
            Scale = scale;
            Artifacts = a;
        }
    }

    /// <summary>
    /// Placeholder for this type of item, this action is not meant to return a value
    /// </summary>
    public class SampleArtifactsOutput
    {
       
    }
}
