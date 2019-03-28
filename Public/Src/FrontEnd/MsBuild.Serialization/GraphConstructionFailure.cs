// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// Represents an error found by the (out of proc) graph construction process
    /// </summary>
    /// <remarks>
    /// This class is serialized via JSON.
    /// </remarks>
    public sealed class GraphConstructionError
    {
        /// <summary>
        /// Only available if <see cref="HasLocation"/> is true
        /// </summary>
        public Location Location { get; }

        /// <summary>
        /// The error message
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Whether <see cref="Location"/> is available
        /// </summary>
        public bool HasLocation { get; }

        /// <nodoc/>
        public static GraphConstructionError CreateFailureWithLocation(Location location, string message)
        {
            return new GraphConstructionError(location, message, hasLocation: true);
        }

        /// <nodoc/>
        public static GraphConstructionError CreateFailureWithoutLocation(string message)
        {
            return new GraphConstructionError(default(Location), message, hasLocation: false);
        }

        [JsonConstructor]
        private GraphConstructionError(Location location, string message, bool hasLocation)
        {
            Location = location;
            Message = message;
            HasLocation = hasLocation;
        }
    }
}
