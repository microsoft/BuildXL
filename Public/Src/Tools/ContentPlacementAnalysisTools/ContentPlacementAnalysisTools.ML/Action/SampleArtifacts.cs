using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core;
namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action takes a set of linearized artifacts and creates a sample of size input.SampleSize.
    /// The sample has the same CDF (or similar) that the universe in terms of the number of queues each
    /// hash has been seen
    /// </summary>
    public class SampleArtifacts : TimedAction<SampleArtifactsInput, SampleArtifactsOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly Random m_random = new Random(Thread.CurrentThread.GetHashCode());
        private StreamWriter m_writer = null;

        /// <inheritdoc />
        protected override void CleanUp(SampleArtifactsInput input, SampleArtifactsOutput output)
        {
            if(m_writer != null)
            {
                m_writer.Dispose();
            }
        }        

        /// <summary>
        /// Write the samples in this specific format. 
        /// </summary>
        protected override SampleArtifactsOutput Perform(SampleArtifactsInput input)
        {
            s_logger.Debug("SampleArtifacts starts...");
            var written = 0;
            try
            {
                // write the headers
                var sampled = new List<MLArtifact>();
                MLArtifact.WriteColumnsToStream(m_writer);
                // we have the scale, now we can just randomly choose
                foreach (var scaled in input.Scale)
                {
                    var nq = scaled.Key;
                    var linearized = input.Artifacts[nq];
                    if (linearized.Count <= scaled.Value)
                    {
                        // write all
                        foreach(var linear in linearized)
                        {
                            linear.WriteToCsvStream(m_writer);
                            sampled.Add(linear);
                            ++written;
                        }
                    }
                    else
                    {
                        // take a random set
                        var randomIds = new HashSet<int>();
                        while(randomIds.Count < scaled.Value)
                        {
                            randomIds.Add(m_random.Next(input.Artifacts[nq].Count));
                        }
                        // now take them
                        foreach(var pos in randomIds)
                        {
                            linearized[pos].WriteToCsvStream(m_writer);
                            sampled.Add(linearized[pos]);
                            ++written;
                        }
                    }
                }
                return new SampleArtifactsOutput(sampled, input.SampleFileName);
            }
            finally
            {
                s_logger.Debug($"SampleArtifacts ends in {Stopwatch.ElapsedMilliseconds}ms, sample [{input.SampleFileName}] contains {written} artifacts");
            }
        }

        

        /// <inheritdoc />
        protected override void Setup(SampleArtifactsInput input)
        {
            m_writer = new StreamWriter(input.SampleFileName);
            Contract.Requires(File.Exists(input.SampleFileName), "Could not create output file for sample");
        }
    }

    /// <summary>
    /// The input for this action requires all the artifacts (linearized)
    /// </summary>
    public class SampleArtifactsInput
    {
        /// <summary>
        /// The sample file name
        /// </summary>
        public string SampleFileName { get; set; }
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
        public SampleArtifactsInput(string sfName, Dictionary<int, int> scale, MultiValueDictionary<int, MLArtifact> a)
        {
            SampleFileName = sfName;
            Scale = scale;
            Artifacts = a;
        }
    }

    /// <summary>
    /// The output contains the sampled artifacts
    /// </summary>
    public class SampleArtifactsOutput
    {
        /// <summary>
        /// The artifacts belonging to this sample
        /// </summary>
        public List<MLArtifact> Sample { get; set; }
        /// <summary>
        /// The full path of the sample file name
        /// </summary>
        public string SampleFileName { get; set; }

        public SampleArtifactsOutput(List<MLArtifact> arts, string file)
        {
            Sample = arts;
            SampleFileName = file;
        }
    }
}
