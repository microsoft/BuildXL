using System.Collections.Generic;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.CMake.Serialization
{
    /// <summary>
    /// Configuration of the CMakeRunner tool
    /// </summary>
    [JsonObject(IsReference = false)]
    public struct CMakeRunnerArguments
    {
        /// <summary>
        /// The project root, i.e the location of the root CMakeLists.txt.
        /// </summary>
        public string ProjectRoot;

        /// <summary>
        /// The path to the directory where build files will be generated. 
        /// </summary>
        public string BuildDirectory;

        /// <summary>
        /// If this isn't null or empty, we will save the CMake standard output here. 
        /// </summary>
        public string StandardOutputFile;

        /// <summary>
        /// A list of directories in which to search for cmake.exe. 
        /// </summary>
        public IEnumerable<string> CMakeSearchLocations;

        /// <summary>
        /// The cache entry arguments (-DKey=Value arguments).
        /// If the value is null, the entry will be unset (-UKey)
        /// </summary>
        public IReadOnlyDictionary<string, string> CacheEntries;
    }

}
