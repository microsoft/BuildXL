// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Ninja.Serialization
{
    /// <summary>
    /// A Ninja top-level target (build goal)
    /// </summary>
    /// <remarks>
    /// This class is designed to be JSON serializable.
    /// </remarks>
    public sealed class NinjaTarget
    {
        /// <summary>
        /// The node that we have to schedule (with all its dependents) to end up building this target.
        /// This is usually a phony node. TODO: Remove phony nodes
        /// </summary>
        [JsonProperty(PropertyName = "producer_node")]
        public NinjaNode ProducerNode;

        /// <summary>
        /// The name of this target (all, clean, etc). 
        /// This corresponds to the name of a build goal in Ninja
        /// </summary>
        public string Name { get; }
    }
}
