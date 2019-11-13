using System.IO;
using BuildXL.Utilities.Collections;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlamentAnalysisTools.Core.Analyzer;
using Newtonsoft.Json;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// An action that reads a single build files and groups artifacts by hash
    /// </summary>
    public class ParseBuildArtifacts : TimedAction<ParseBuildArtifactsInput, ParseBuildArtifactsOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        /// <inheritdoc />
        protected override void CleanUp(ParseBuildArtifactsInput input, ParseBuildArtifactsOutput output){}

        /// <summary>
        /// Reads a single build files and groups artifacts by hash
        /// </summary>
        protected override ParseBuildArtifactsOutput Perform(ParseBuildArtifactsInput input)
        {
            var arts = new MultiValueDictionary<string, ArtifactWithBuildMeta>();
            var totalArtifactsRead = 0;
            s_logger.Debug($"ParseBuildArtifacts starts");
            try
            {
                // so in here we will read a single input file and classify its artifacts
                var bxlBuild = new JsonSerializer().Deserialize<BxlBuild>(
                    new JsonTextReader(
                        new StreamReader(input.BuildArtifactsFile)
                    )
                );
                foreach (var artifact in bxlBuild.Artifacts)
                {
                    // i will group them in levels. The key here has to be the hash, using that hash
                    // we will see how many artifacts are referring to it
                    if(artifact.NumInputPips > 0 || artifact.NumOutputPips > 0)
                    {
                        arts.Add(artifact.Hash, new ArtifactWithBuildMeta(bxlBuild.Meta, artifact));
                        ++totalArtifactsRead;
                    }
                }
                return new ParseBuildArtifactsOutput(arts, totalArtifactsRead);
            }
            finally
            {
                s_logger.Debug($"ParseBuildArtifacts (read={totalArtifactsRead}, grouped={arts.Count}) ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        /// <inheritdoc />
        protected override void Setup(ParseBuildArtifactsInput input){}
    }

    /// <summary>
    /// The input for this action contains a path to a json file (produced by ContentPlacementAnalyzer)
    /// </summary>
    public class ParseBuildArtifactsInput
    {
        /// <summary>
        /// The file where the artifacts are stored (json) following BXLBuild model
        /// </summary>
        public string BuildArtifactsFile { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        public ParseBuildArtifactsInput(string file)
        {
            BuildArtifactsFile = file;
        }
    }

    /// <summary>
    /// The output for this action is a map of artifacts, using hash as the key
    /// </summary>
    public class ParseBuildArtifactsOutput
    {
        /// <summary>
        /// The tptal number of artifacts that wehere read
        /// </summary>
        public int TotalArtifactsRead { get; }
        /// <summary>
        /// MultiDict of artifacts. Hash is the key
        /// </summary>
        public MultiValueDictionary<string, ArtifactWithBuildMeta> ArtifactsByHash { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        public ParseBuildArtifactsOutput(MultiValueDictionary<string, ArtifactWithBuildMeta> arts, int tar)
        {
            ArtifactsByHash = arts;
            TotalArtifactsRead = tar;
        }
    }

    /// <summary>
    /// Used to keep a reference to the build an artifact belongs to
    /// </summary>
    public class ArtifactWithBuildMeta
    {
        /// <summary>
        /// The referenced artifact
        /// </summary>
        public BxlArtifact Artifact { get; }
        /// <summary>
        /// The referenced build where the artifact belongs to
        /// </summary>
        public BxlBuildMeta Meta { get; }
        /// <summary>
        /// Constructor
        /// </summary>
        public ArtifactWithBuildMeta(BxlBuildMeta meta, BxlArtifact artifact)
        {
            Meta = meta;
            Artifact = artifact;
        }
        /// <summary>
        /// Writes an artifact to a json file using a stream
        /// </summary>
        public void WriteToJsonStream(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            Meta.WriteToJsonStream(writer, "Meta");
            writer.WritePropertyName("Artifact");
            Artifact.WriteToJsonStream(writer);
            writer.WriteEndObject();
        }
    }
}
