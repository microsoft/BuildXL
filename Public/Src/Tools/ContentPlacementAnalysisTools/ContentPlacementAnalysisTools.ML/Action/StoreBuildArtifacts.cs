using System;
using System.IO;
using System.Threading;
using ContentPlacementAnalysisTools.Core.Utils;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action takes a set of artifacts (from a single build) and groups them in an accumulator
    /// </summary>
    public class StoreBuildArtifacts : TimedAction<ParseBuildArtifactsOutput, StoreBuildArtifactsOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        /// <inheritdoc />
        protected override void CleanUp(ParseBuildArtifactsOutput input, StoreBuildArtifactsOutput output){}

        private string m_outputDir = null;

        /// <summary>
        /// The constructor takes the output directory as argument
        /// </summary>
        public StoreBuildArtifacts(string outputDir)
        {
            m_outputDir = outputDir;
        }

        /// <summary>
        /// This action takes a set of artifacts (from a single build) and groups them in an accumulator
        /// </summary>
        protected override StoreBuildArtifactsOutput Perform(ParseBuildArtifactsOutput input)
        {
            s_logger.Debug($"StoreBuildArtifacts starts with {input.ArtifactsByHash.Count} artifacts...");
            try
            {
                // so in here, we can save the results. We do this to avoid unnecessary memory preassure.
                // The input contains a hash and a list of artifacts that represent that hash. We can write
                // them one by one...
                foreach(var entry in input.ArtifactsByHash)
                {
                    // we can create a directory for the hash
                    var directoryName = Path.Combine(m_outputDir, entry.Key);
                    Directory.CreateDirectory(directoryName);
                    if (!Directory.Exists(directoryName))
                    {
                        // ommit this guy and warn
                        s_logger.Warn($"Could not create directory [{directoryName}]");
                        continue;
                    }
                    // and write all of its artifacts. This will be io intense
                    foreach (var value in entry.Value)
                    {
                        using(var jsonStream = new JsonTextWriter(new StreamWriter(Path.Combine(directoryName, $"{Thread.CurrentThread.ManagedThreadId}-{Environment.TickCount}.json"))))
                        {
                            value.WriteToJsonStream(jsonStream);
                        }
                    }
                }
                // done
                return new StoreBuildArtifactsOutput();
            }
            finally
            {
                s_logger.Debug($"StoreBuildArtifacts ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        /// <inheritdoc />
        protected override void Setup(ParseBuildArtifactsOutput input){}
    }

    /// <summary>
    /// Placeholder for this type of item, this action is not meant to return a value
    /// </summary>
    public class StoreBuildArtifactsOutput { }
}
