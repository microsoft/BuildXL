// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Ninja.Serialization
{
    /// <summary>
    /// Configuration of the Ninja Specs-to-JSON generation tool
    /// </summary>
    [JsonObject(IsReference = false)]
    public struct NinjaGraphToolArguments
    {
        /// <summary>
        /// Where to save the JSON output
        /// </summary>
        public string OutputFile;

        /// <summary>
        /// The project root. This should also be the location of the build file (that is, build.ninja).
        /// </summary>
        public string ProjectRoot;

        /// <summary>
        /// The build file name -- if null, build.ninja. 
        /// </summary>
        public string BuildFileName;
       
        /// <summary>
        /// Build goals to target. If empty, the ninja default (all outputs which aren't inputs) will be used
        /// </summary>
        public string[] Targets;
    }

}
