using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Ninja.Serialization
{
    /// <summary>
    /// This class wraps a serialization of a Ninja graph, together with meta-information resulting
    /// from its generation (success vs error, etc.)
    /// </summary>
    [JsonObject(IsReference = false)]
    public readonly struct NinjaGraphResult
    {
        /// <nodoc />
        [JsonProperty]
        public readonly NinjaGraph Graph;

        /// <summary>
        /// When true this wrapper contains a NinjaGraph in the Graph property.
        /// When false, something happened while constructing the graph. Error messages are indicated in FailureReason
        /// </summary>
        [JsonIgnore]
        public bool Succeeded => Graph != null;

        /// <summary>
        /// If Succeeded is false, this contains the reason for failure. If not, it is null or empty
        /// </summary>
        public string FailureReason { get; }


        /// <summary>
        /// Creates a NinjaGraphResult indicating some error, with the reason provided as FailureReason
        /// </summary>
        public static NinjaGraphResult CreateFailure(string reason)
        {
            return new NinjaGraphResult(default, reason);
        }

        /// <summary>
        /// Creates a NinjaGraph indicating success and with the given graph as a result
        /// </summary>
        /// <param name="graph"></param>
        /// <returns></returns>
        public static NinjaGraphResult CreateSuccess(NinjaGraph graph)
        {
            Contract.Assert(graph != null);
            return new NinjaGraphResult(graph, "");
        }

        [JsonConstructor]
        private NinjaGraphResult(NinjaGraph graph, string failureReason)
        {
            Graph = graph;
            FailureReason = failureReason;
        }


    }

    /// <summary>
    /// A serialization of the dependency graph that Ninja (roughly) generates from the specs.
    /// We say roughly because the phony rules are 'resolved' before serializing the graph.
    /// </summary>
    [JsonObject(IsReference = false)]
    public sealed class NinjaGraph
    {

        /// <summary>
        /// The build goals specified as targets to the ninja tool,
        /// that is, the ones that would build if we called 'ninja {targets}'
        /// </summary>
        [JsonProperty]
        public IReadOnlyCollection<NinjaTarget> Targets;

        /// <summary>
        /// Every build goal that will have to run if one wishes to build all the targets.
        /// Every one of these nodes is actually an "Edge" in ninja lingo, 
        /// but we call them Nodes BuildXL-side because they correspond to Pips 
        /// (a command execution => a process running in a pip)
        /// </summary>
        [JsonProperty]
        public IReadOnlyCollection<NinjaNode> Nodes;

        
    }
}
