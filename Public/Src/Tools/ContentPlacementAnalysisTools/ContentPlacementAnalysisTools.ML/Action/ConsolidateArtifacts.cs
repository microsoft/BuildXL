using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core;
using ContentPlamentAnalysisTools.Core;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action takes a set of artifacts (from a single build) and groups them in an accumulator
    /// </summary>
    public class ConsolidateArtifacts : TimedAction<ParseBuildArtifactsOutput, ConsolidateArtifactsOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private MultiValueDictionary<string, ArtifactWithBuild> m_artifactsByHash = null;

        /// <summary>
        /// The muti dict is used to accumulate artifacts
        /// </summary>
        public ConsolidateArtifacts(MultiValueDictionary<string, ArtifactWithBuild> arts)
        {
            m_artifactsByHash = arts;
        }

        /// <inheritdoc />
        protected override void CleanUp(ParseBuildArtifactsOutput input, ConsolidateArtifactsOutput output){}

        /// <summary>
        /// This action takes a set of artifacts (from a single build) and groups them in an accumulator
        /// </summary>
        protected override ConsolidateArtifactsOutput Perform(ParseBuildArtifactsOutput input)
        {
            s_logger.Debug($"ConsolidateArtifacts starts");
            try
            {
                // so in here, lets accumulate what we got as input
                foreach(var entry in input.ArtifactsByHash)
                {
                    foreach(var value in entry.Value)
                    {
                        m_artifactsByHash.Add(entry.Key, value);
                    }
                    
                }
                // done
                return new ConsolidateArtifactsOutput();
            }
            finally
            {
                s_logger.Debug($"ConsolidateArtifacts ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        /// <inheritdoc />
        protected override void Setup(ParseBuildArtifactsOutput input){}
    }

    /// <summary>
    /// Placeholder for this type of item, this action is not meant to return a value
    /// </summary>
    public class ConsolidateArtifactsOutput {}
}
