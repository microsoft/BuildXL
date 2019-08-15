using System.Collections.Generic;
using Newtonsoft.Json;

namespace ContentPlamentAnalysisTools.Core
{
    /// <summary>
    /// Represents a single build as a set of file artifacts
    /// </summary>
    public class BxlBuild
    {
        /// <summary>
        /// The Id of the build
        /// </summary>
        public string BuidId { get; set; }
        /// <summary>
        /// The queue that hosted this build
        /// </summary>
        public string BuildQueue { get; set; }
        /// <summary>
        /// When did the build started (ticks)
        /// </summary>
        public long BuildStartTimeTicks { get; set; }
        /// <summary>
        /// Build duration (ms)
        /// </summary>
        public double BuildDurationMs { get; set; }
        /// <summary>
        /// Total number of pips in this build
        /// </summary>
        public int TotalPips { get; set; }
        /// <summary>
        /// Collected file artifacts
        /// </summary>
        public List<BxlArtifact> Artifacts { get; set; } = new List<BxlArtifact>();

        /// <summary>
        /// Utility method to write a json attr to a json stream
        /// </summary>
        public static void WriteJsonPropertyToStream<T>(JsonTextWriter writer, string name, T value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        /// <summary>
        /// Writes a build to a json file using a stream
        /// </summary>
        public void WriteToJsonStream(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteJsonPropertyToStream(writer, "BuidId", BuidId);
            WriteJsonPropertyToStream(writer, "BuildQueue", BuildQueue);
            WriteJsonPropertyToStream(writer, "BuildStartTimeTicks", BuildStartTimeTicks);
            WriteJsonPropertyToStream(writer, "BuildDurationMs", BuildDurationMs);
            WriteJsonPropertyToStream(writer, "TotalPips", TotalPips);
            writer.WritePropertyName("Artifacts");
            writer.WriteStartArray();
            foreach (var artifact in Artifacts)
            {
                artifact.WriteToJsonStream(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Represents a single artifact in a build, containing among other things a set of pips that either use or produce it
    /// </summary>
    public class BxlArtifact
    {
        /// <summary>
        /// The hash for this artifact
        /// </summary>
        public string Hash { get; set; }
        /// <summary>
        /// The file that is represented by this artifact
        /// </summary>
        public string ReportedFile { get; set; }
        /// <summary>
        /// The size of the file associated to this artifact
        /// </summary>
        public long ReportedSize { get; set; }
        /// <summary>
        /// The number of pips that use this artifact
        /// </summary>
        public int NumInputPips => InputPips.Count;
        /// <summary>
        /// The data of all the pips that use this artifact
        /// </summary>
        public List<BxlPipData> InputPips { get; set; } = new List<BxlPipData>();
        /// <summary>
        /// The number of pips that produce this artifact
        /// </summary>
        public int NumOutputPips  =>  OutputPips.Count;
        /// <summary>
        /// The data of all the pips that produce this artifact
        /// </summary>
        public List<BxlPipData> OutputPips { get; set; } = new List<BxlPipData>();

        /// <summary>
        /// Writes a single artifact to a json stream
        /// </summary>
        public void WriteToJsonStream(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            BxlBuild.WriteJsonPropertyToStream(writer, "Hash", Hash);
            BxlBuild.WriteJsonPropertyToStream(writer, "ReportedFile", ReportedFile);
            BxlBuild.WriteJsonPropertyToStream(writer, "ReportedSize", ReportedSize);
            BxlBuild.WriteJsonPropertyToStream(writer, "NumInputPips", NumInputPips);
            BxlBuild.WriteJsonPropertyToStream(writer, "NumOutputPips", NumOutputPips);
            WritePipsToJsonStream(writer, "InputPips", InputPips);
            WritePipsToJsonStream(writer, "OutputPips", OutputPips);
            writer.WriteEndObject();
            // done...
        }

        private void WritePipsToJsonStream(JsonTextWriter writer, string fieldName, List<BxlPipData> pips)
        {
            writer.WritePropertyName(fieldName);
            writer.WriteStartArray();
            foreach (var pip in pips)
            {
                pip.WriteToJsonStream(writer);
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Represents a pip that either consumes or produces one file artifact (BxlArtifact)
    /// </summary>
    public sealed class BxlPipData
    {
        /// <summary>
        /// The semi stable hash of the pip
        /// </summary>
        public string SemiStableHash { get; set; }
        /// <summary>
        /// The priority in case this is a process pip
        /// </summary>
        public int Priority { get; set; }
        /// <summary>
        /// The weight in case this is a process pip
        /// </summary>
        public int Weight { get; set; }
        /// <summary>
        /// The number of tags associated to the pip
        /// </summary>
        public int TagCount { get; set; }
        /// <summary>
        /// The number of semaphores associated to a process pip
        /// </summary>
        public int SemaphoreCount { get; set; }
        /// <summary>
        /// The duration of the pips (ms)
        /// </summary>
        public double DurationMs { get; set; }
        /// <summary>
        /// The pip type
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// The pip execution level
        /// </summary>
        public string ExecutionLevel { get; set; }
        /// <summary>
        /// The number of dependencies of this pip (other pips)
        /// </summary>
        public int DependencyCount { get; set; }
        /// <summary>
        /// The number of inputs of this pip
        /// </summary>
        public int InputCount { get; set; }
        /// <summary>
        /// The number of outputs of this pip
        /// </summary>
        public int OutputCount { get; set; }
        /// <summary>
        /// The start time for this pip (ticks)
        /// </summary>
        public long StartTimeTicks { get; set; }

        /// <summary>
        /// Writes a single pip to a json stream
        /// </summary>
        public void WriteToJsonStream(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            BxlBuild.WriteJsonPropertyToStream(writer, "SemiStableHash", SemiStableHash);
            BxlBuild.WriteJsonPropertyToStream(writer, "Priority", Priority);
            BxlBuild.WriteJsonPropertyToStream(writer, "Weight", Weight);
            BxlBuild.WriteJsonPropertyToStream(writer, "TagCount", TagCount);
            BxlBuild.WriteJsonPropertyToStream(writer, "DependencyCount", DependencyCount);
            BxlBuild.WriteJsonPropertyToStream(writer, "InputCount", InputCount);
            BxlBuild.WriteJsonPropertyToStream(writer, "OutputCount", OutputCount);
            BxlBuild.WriteJsonPropertyToStream(writer, "SemaphoreCount", SemaphoreCount);
            BxlBuild.WriteJsonPropertyToStream(writer, "StartTimeTicks", StartTimeTicks);
            BxlBuild.WriteJsonPropertyToStream(writer, "Type", Type);
            BxlBuild.WriteJsonPropertyToStream(writer, "ExecutionLevel", ExecutionLevel);
            writer.WriteEndObject();
        }

    }
}
