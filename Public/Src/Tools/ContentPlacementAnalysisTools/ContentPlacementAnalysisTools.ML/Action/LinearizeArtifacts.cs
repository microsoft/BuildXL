using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core.ML;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlamentAnalysisTools.Core.Analyzer;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action takes a directory that contains all the outputs for an artifact an "linearize" it,
    /// so it will write a single row for an artifact in a csv file
    /// </summary>
    public class LinearizeArtifacts : TimedAction<LinearizeArtifactsInput, LinearizeArtifactsOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private TextWriter m_database = null;

        /// <inheritdoc />
        protected override void CleanUp(LinearizeArtifactsInput input, LinearizeArtifactsOutput output){}

        /// <summary>
        /// This action takes a streamwriter pointing to a csv file
        /// </summary>
        public LinearizeArtifacts(TextWriter database)
        {
            m_database = database;
        }
        /// <summary>
        /// This version does not write results to any file
        /// </summary>
        public LinearizeArtifacts(){}

        /// <summary>
        /// Linearize artifacts one by one
        /// </summary>
        protected override LinearizeArtifactsOutput Perform(LinearizeArtifactsInput input)
        {
            string currentFile = null;
            try
            {
                var totalIpips = 0;
                var totalOpips = 0;
                var linear = new MLArtifact();
                // we are supposed to have a set of files here, or a list
                if(input.ArtifactsForHash == null)
                {
                    foreach (var file in Directory.EnumerateFiles(input.ArtifactFolder, "*.json"))
                    {
                        currentFile = file;
                        // so in here we will read a single input file and classify its artifacts
                        var artifact = new JsonSerializer().Deserialize<ArtifactWithBuildMeta>(
                            new JsonTextReader(
                                new StreamReader(file)
                            )
                        );
                        // so we have it in here, lets start accumulating
                        var linearResult = LinearizeTo(linear, artifact);
                        totalIpips += linearResult.Item1;
                        totalOpips += linearResult.Item2;
                        linear.ReportedPaths.Add(artifact.Artifact.ReportedFile);
                    }
                    // set the hash
                    linear.Hash = Path.GetDirectoryName(input.ArtifactFolder);
                }
                else
                {
                    foreach (var artifact in input.ArtifactsForHash)
                    {
                        // so we have it in here, lets start accumulating
                        var linearResult = LinearizeTo(linear, artifact);
                        totalIpips += linearResult.Item1;
                        totalOpips += linearResult.Item2;
                        // and the paths
                        linear.ReportedPaths.Add(artifact.Artifact.ReportedFile);
                    }
                    // set the hash
                    linear.Hash = input.Hash;
                }
                // and when we are done, we calculate the avgs
                AdjustAverages(linear, totalIpips, totalOpips);
                // and write
                if(m_database != null)
                {
                    linear.WriteToCsvStream(m_database);
                }
                // done...
                return new LinearizeArtifactsOutput(linear.Queues.Count, linear);
            }
            catch(Exception)
            {
                s_logger.Error($"Artifact [{currentFile}] reported an exception");
                throw;
            }
            finally
            {
                // this guy will not log, this task is too small and that will hurt performance
            }
        }

        private void AdjustAverages(MLArtifact linear, double inputAdjust, double outputAdjust)
        {
            
            if(linear.AvgInputPips > 0)
            {
                // input
                linear.AvgDepsForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgInputsForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgOutputsForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgPriorityForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgWeightForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgTagCountForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgSemaphoreCountForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgPositionForInputPips /= inputAdjust > 0 ? inputAdjust : 1.0;
                linear.AvgInputPips /= linear.Builds.Count;
            }
            else
            {
                // not present
                linear.AvgDepsForInputPips = -1;
                linear.AvgInputsForInputPips = -1;
                linear.AvgOutputsForInputPips = -1;
                linear.AvgPriorityForInputPips = -1;
                linear.AvgWeightForInputPips = -1;
                linear.AvgTagCountForInputPips = -1;
                linear.AvgSemaphoreCountForInputPips = -1;
                linear.AvgPositionForInputPips = -1;
                linear.AvgInputPips = -1;
            }
            if(linear.AvgOutputPips > 0)
            {
                // output
                linear.AvgDepsForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgInputsForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgOutputsForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgPriorityForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgWeightForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgTagCountForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgSemaphoreCountForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgPositionForOutputPips /= outputAdjust > 0 ? outputAdjust : 1.0;
                linear.AvgOutputPips /= linear.Builds.Count;
            }
            else
            {
                // not present
                linear.AvgDepsForOutputPips = -1;
                linear.AvgInputsForOutputPips = -1;
                linear.AvgOutputsForOutputPips = -1;
                linear.AvgPriorityForOutputPips = -1;
                linear.AvgWeightForOutputPips = -1;
                linear.AvgTagCountForOutputPips = -1;
                linear.AvgSemaphoreCountForOutputPips = -1;
                linear.AvgPositionForOutputPips = -1;
                linear.AvgOutputPips = -1;
            }
           
        }

        private Tuple<int, int> LinearizeTo(MLArtifact linear, ArtifactWithBuildMeta current)
        {
            linear.Queues.Add(current.Meta.BuildQueue);
            linear.Builds.Add(current.Meta.BuidId);
            // same hash == same size
            linear.SizeBytes = current.Artifact.ReportedSize;
            // now, do the pips....
            var totalIpips = LinearizePips(linear, current.Artifact.InputPips, current, true);
            var totalOpips = LinearizePips(linear, current.Artifact.OutputPips, current, false);
            return new Tuple<int, int>(totalIpips, totalOpips);
        }

        private int LinearizePips(MLArtifact linear, List<BxlPipData> pips, ArtifactWithBuildMeta artifact, bool inputPips)
        {
            var pipCount = 0;
            var avgDeps = 0.0;
            var avgIns = 0.0;
            var avgOuts = 0.0;
            var avgPrio = 0.0;
            var avgWeight = 0.0;
            var avgTC = 0.0;
            var avgSC = 0.0;
            var avgPos = 0.0;
            foreach (var pip in pips)
            {
                avgDeps += pip.DependencyCount;
                avgIns += pip.InputCount;
                avgOuts += pip.OutputCount;
                avgPrio += pip.Priority;
                avgWeight += pip.Weight;
                avgTC += pip.TagCount;
                avgSC += pip.SemaphoreCount;
                avgPos += inputPips? CalculateRelativeStartPosition(pip, artifact.Meta) : CalculateRelativeEndPosition(pip, artifact.Meta);
                ++pipCount;
            }
            // now assign depending on type
            if (inputPips)
            {
                linear.AvgDepsForInputPips += avgDeps;
                linear.AvgInputsForInputPips += avgIns;
                linear.AvgOutputsForInputPips += avgOuts;
                linear.AvgPriorityForInputPips += avgPrio;
                linear.AvgWeightForInputPips += avgWeight;
                linear.AvgTagCountForInputPips += avgTC;
                linear.AvgSemaphoreCountForInputPips += avgSC;
                linear.AvgPositionForInputPips += avgPos;
                linear.AvgInputPips += pipCount;
            }
            else
            {
                linear.AvgDepsForOutputPips += avgDeps;
                linear.AvgInputsForOutputPips += avgIns;
                linear.AvgOutputsForOutputPips += avgOuts;
                linear.AvgPriorityForOutputPips += avgPrio;
                linear.AvgWeightForOutputPips += avgWeight;
                linear.AvgTagCountForOutputPips += avgTC;
                linear.AvgSemaphoreCountForOutputPips += avgSC;
                linear.AvgPositionForOutputPips += avgPos;
                linear.AvgOutputPips += pipCount;
            }
            return pipCount;
        }

        private double CalculateRelativeStartPosition(BxlPipData pip, BxlBuildMeta meta)
        {
            var pos = 0.0;
            var buildStart = new DateTime(meta.BuildStartTimeTicks);
            var buildEnd = buildStart.AddMilliseconds(meta.BuildDurationMs);
            var duration = buildEnd - buildStart;
            var pipStart = new DateTime(pip.StartTimeTicks);
            var pipRelativeStart = pipStart - buildStart;
            if (pipStart >= buildStart && pipStart.AddMilliseconds(pip.DurationMs) <= buildEnd && duration.TotalMilliseconds > 0)
            {
                pos = ((1.0 * pipRelativeStart.Ticks) / (1.0 * duration.Ticks));
            }
            else if (pipStart >= buildStart && pipStart.AddMilliseconds(pip.DurationMs) > buildEnd && duration.TotalMilliseconds > 0)
            {
                pos = 1.0;
            }
            return pos;
        }

        private double CalculateRelativeEndPosition(BxlPipData pip, BxlBuildMeta meta)
        {
            var pos = 0.0;
            var buildStart = new DateTime(meta.BuildStartTimeTicks);
            var buildEnd = buildStart.AddMilliseconds(meta.BuildDurationMs);
            var duration = buildEnd - buildStart;
            var pipStart = new DateTime(pip.StartTimeTicks);
            var pipEnd = pipStart.AddMilliseconds(pip.DurationMs);
            if (pipStart >= buildStart && pipEnd <= buildEnd && duration.TotalMilliseconds > 0)
            {
                var pipRelativeEnd = pipEnd - buildStart;
                pos = (1.0 * pipRelativeEnd.Ticks) / (1.0 * duration.Ticks);
            }
            else if (pipStart >= buildStart && pipEnd > buildEnd && duration.TotalMilliseconds > 0)
            {
                pos = 1.0;
            }
            return pos;
        }

        /// <inheritdoc />
        protected override void Setup(LinearizeArtifactsInput input){}
    }

    /// <summary>
    /// The input for this action requires an existing folder or a set of ML artifacts
    /// </summary>
    public class LinearizeArtifactsInput
    {
        /// <summary>
        /// The folder with the json files for this artifact
        /// </summary>
        public string ArtifactFolder { get; set; }
        /// <summary>
        /// The list of artifacts to linearize in case they are already in memory
        /// </summary>
        public IReadOnlyList<ArtifactWithBuildMeta> ArtifactsForHash { get; set; } = null;
        /// <summary>
        /// The hash, used when the artifacts are in memory
        /// </summary>
        public string Hash { get; set; }
        /// <summary>
        /// Constructor
        /// </summary>
        public LinearizeArtifactsInput(string dir)
        {
            ArtifactFolder = dir;
        }
        /// <summary>
        /// Constructor
        /// </summary>
        public LinearizeArtifactsInput(string hash, IReadOnlyList<ArtifactWithBuildMeta> ar)
        {
            ArtifactsForHash = ar;
            Hash = hash;
        }
    }

    /// <summary>
    /// Placeholder for this type of item, this action is not meant to return a value
    /// </summary>
    public class LinearizeArtifactsOutput
    {
        /// <summary>
        /// The number of queues in which the outputis present
        /// </summary>
        public int NumQueues { get; }
        /// <summary>
        /// The linearized artifact
        /// </summary>
        public MLArtifact Linear { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        public LinearizeArtifactsOutput(int nq, MLArtifact lin) 
        {
            NumQueues = nq;
            Linear = lin;
        }
    }
}
