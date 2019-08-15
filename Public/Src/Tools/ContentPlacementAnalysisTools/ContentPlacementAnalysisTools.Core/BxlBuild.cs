using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        /// Total number of artifacts in this build
        /// </summary>
        public int TotalArtifacts { get; set; }
        /// <summary>
        /// Total number of artifacts in this sample
        /// </summary>
        public int SampledArtifacts { get; set; }
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
        /// Utility method to read a property from a json reader
        /// </summary>
        public static Tuple<string, string> ReadNextProperty(JsonTextReader reader) => new Tuple<string, string>(reader.ReadAsString(), reader.ReadAsString());

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
            WriteJsonPropertyToStream(writer, "TotalArtifacts", TotalArtifacts);
            WriteJsonPropertyToStream(writer, "SampledArtifacts", SampledArtifacts);
            writer.WritePropertyName("Artifacts");
            writer.WriteStartArray();
            foreach (var artifact in Artifacts)
            {
                artifact.WriteToJsonStream(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>
        /// Utility method to create a bxl build from a json file. The attrs are read in
        /// the same order they are writen
        /// </summary>
        public static BxlBuild ReadFromJsonStream(JsonTextReader reader)
        {
            BxlBuild output = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.StartObject)
                {
                    // here does this guy starts. This guy has several properties that we will read,
                    // but also has two lists inside. We have to respect the ordering here.
                    var bi = ReadNextProperty(reader);
                    var bq = ReadNextProperty(reader);
                    var bst = ReadNextProperty(reader);
                    var bd = ReadNextProperty(reader);
                    var tp = ReadNextProperty(reader);
                    var ta = ReadNextProperty(reader);
                    var sa = ReadNextProperty(reader);
                    output = new BxlBuild()
                    {
                        BuidId = bi.Item2,
                        BuildQueue = bq.Item2,
                        BuildStartTimeTicks = Convert.ToInt64(bst.Item2),
                        BuildDurationMs = Convert.ToDouble(bd.Item2),
                        TotalPips = Convert.ToInt32(tp.Item2),
                        TotalArtifacts = Convert.ToInt32(ta.Item2),
                        SampledArtifacts = Convert.ToInt32(sa.Item2)
                    };
                    // now, read another property name, an array start and the the artifacts
                    reader.Read();
                    reader.Read();
                    output.Artifacts = BxlArtifact.ReadFromJsonStream(reader, output.SampledArtifacts);
                    // read an array end
                    reader.Read();
                }
                if (reader.TokenType == JsonToken.EndObject)
                {
                    break;
                }
            }
            return output;
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

        /// <summary>
        /// Utility method to create a bxl pip artifact from a json file. The attrs are read in
        /// the same order they are writen
        /// </summary>
        public static BxlArtifact ReadFromJsonStream(JsonTextReader reader)
        {
            var output = new BxlArtifact();
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.StartObject)
                {
                    // here does this guy starts. This guy has several properties that we will read,
                    // but also has two lists inside. We have to respect the ordering here.
                    var ha = BxlBuild.ReadNextProperty(reader);
                    var rf = BxlBuild.ReadNextProperty(reader);
                    var rs = BxlBuild.ReadNextProperty(reader);
                    // read these two, but no assignment since its a computed property...
                    var nip = BxlBuild.ReadNextProperty(reader);
                    var nop = BxlBuild.ReadNextProperty(reader);
                    output.Hash = ha.Item2;
                    output.ReportedFile = rf.Item2;
                    output.ReportedSize = Convert.ToInt64(rs.Item2);
                    var ipips = Convert.ToInt32(nip.Item2);
                    var opips = Convert.ToInt32(nop.Item2);
                    // we need to read a name and then start array token
                    reader.Read();
                    reader.Read();
                    output.InputPips = BxlPipData.ReadFromJsonStream(reader, ipips);
                    // we need to read an end array token
                    reader.Read();
                    // we need to read a name and then start array token
                    reader.Read();
                    reader.Read();
                    output.OutputPips = BxlPipData.ReadFromJsonStream(reader, opips);
                    // we need to read an end array token
                    reader.Read();
                }
                if (reader.TokenType == JsonToken.EndObject)
                {
                    break;
                }
            }
            return output;
        }

        /// <summary>
        /// Utility method to create a lost of bxl pip artifact from a json file. The attrs are read in
        /// the same order they are writen
        /// </summary>
        public static List<BxlArtifact> ReadFromJsonStream(JsonTextReader reader, int count)
        {
            var output = new List<BxlArtifact>();
            for(int i = 0; i < count; ++i)
            {
                output.Add(ReadFromJsonStream(reader));
            }
            return output;
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

        /// <summary>
        /// Utility method to create a bxl pip data from a json file. The attrs are read in
        /// the same order they are writen
        /// </summary>
        public static BxlPipData ReadFromJsonStream(JsonTextReader reader)
        {
            BxlPipData output = null;
            while (reader.Read())
            {
                if(reader.TokenType == JsonToken.StartObject)
                {
                    // here does this guy starts. This guy has several properties that we will read
                    var ssh = BxlBuild.ReadNextProperty(reader);
                    var pr = BxlBuild.ReadNextProperty(reader);
                    var we = BxlBuild.ReadNextProperty(reader);
                    var tc = BxlBuild.ReadNextProperty(reader);
                    var dc = BxlBuild.ReadNextProperty(reader);
                    var ic = BxlBuild.ReadNextProperty(reader);
                    var oc = BxlBuild.ReadNextProperty(reader);
                    var sc = BxlBuild.ReadNextProperty(reader);
                    var stt = BxlBuild.ReadNextProperty(reader);
                    var ty = BxlBuild.ReadNextProperty(reader);
                    var el = BxlBuild.ReadNextProperty(reader);
                    output = new BxlPipData()
                    {
                        SemiStableHash = ssh.Item2,
                        Priority = Convert.ToInt32(pr.Item2),
                        Weight = Convert.ToInt32(we.Item2),
                        TagCount = Convert.ToInt32(tc.Item2),
                        DependencyCount = Convert.ToInt32(dc.Item2),
                        InputCount = Convert.ToInt32(ic.Item2),
                        OutputCount = Convert.ToInt32(oc.Item2),
                        SemaphoreCount = Convert.ToInt32(sc.Item2),
                        StartTimeTicks = Convert.ToInt64(stt.Item2),
                        Type = ty.Item2,
                        ExecutionLevel =el.Item2
                    };
                }
                if (reader.TokenType == JsonToken.EndObject)
                {
                    break;
                }
            }
            return output;
        }

        /// <summary>
        /// Utility method to create a list of bxl pip data from a json file. The attrs are read in
        /// the same order they are writen, and you have to specify how many you will read
        /// </summary>
        public static List<BxlPipData> ReadFromJsonStream(JsonTextReader reader, int count)
        {
            var output = new List<BxlPipData>();
            for(int i = 0; i < count; ++i)
            {
                output.Add(ReadFromJsonStream(reader));
            }
            return output;
        }
    }
}
