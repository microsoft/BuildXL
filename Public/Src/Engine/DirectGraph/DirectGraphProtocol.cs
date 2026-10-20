// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Engine.DirectGraph
{
    /// <summary>
    /// Identity of the DirectGraph wire contract.
    /// </summary>
    public static class DirectGraphProtocol
    {
        /// <summary>
        /// Version of the contract defined in DirectGraph.proto.
        /// </summary>
        /// <remarks>
        /// Bump this for every change to DirectGraph.proto, additive fields included. Protobuf cannot detect skew on
        /// its own - an unknown field is skipped and an absent one reads as its default - so an emitter that sets a
        /// field this receiver does not know would otherwise have it dropped silently and build a subtly different
        /// graph. The receiver requires an exact match, so emitters and BuildXL move together.
        /// </remarks>
        public const uint Version = 1;
    }
}
