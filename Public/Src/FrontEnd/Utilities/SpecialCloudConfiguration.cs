// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Configuration helper for running CMake and Ninja resolvers in the cloud while we're missing dependencies
    /// </summary>
    public class SpecialCloudConfiguration
    {
        /// <summary>
        /// The CMake and Ninja resolvers have dependencies that are not yet in place in the cloud.
        /// This means that we have to manually place some files ourselves in the drop and then point the processes
        /// to them via environment variables. This method returns that 'artificial' environment.
        /// We try to not modify the current variables, appending to the existing ones instead of overriding them.
        /// </summary>
        public static IDictionary<string, string> OverrideEnvironmentForCloud(IDictionary<string, string> environmentDict, AbsolutePath pathToDependencies, FrontEndContext context)
        {

            var pathTable = context.PathTable;
            var stringTable = context.StringTable;

            var previousValue = "";
            if (environmentDict.ContainsKey("INCLUDE"))
            {
                previousValue = environmentDict["INCLUDE"] + ";";
            }

            var includeDir = pathToDependencies
                                .Combine(pathTable, RelativePath.Create(stringTable, "include"))
                                .ToString(pathTable);

            environmentDict["INCLUDE"] = previousValue + includeDir;
            var pathToLib = pathToDependencies.Combine(pathTable, "lib").ToString(pathTable);

            previousValue = "";
            if (environmentDict.ContainsKey("LIB"))
            {
                previousValue = environmentDict["LIB"] + ";";
            }

            environmentDict["LIB"] = previousValue + pathToLib;

            var pathToCppTools = pathToDependencies.Combine(pathTable, "cpptools").ToString(pathTable);
            var pathToSdk = pathToDependencies.Combine(pathTable,  "sdk").ToString(pathTable);
            var pathToPython = pathToDependencies.Combine(pathTable, "python").ToString(pathTable);
            var pathToCmake = pathToDependencies.Combine(pathTable, "cmake").Combine(pathTable, "bin").ToString(pathTable);
            var pathToNinja= pathToDependencies.Combine(pathTable, "ninja").ToString(pathTable);

            previousValue = "";
            if (environmentDict.ContainsKey("PATH"))
            {
                previousValue = environmentDict["PATH"] + ";";
            }

            environmentDict["PATH"] = $"{previousValue}{pathToCppTools};{pathToSdk};{pathToPython};{pathToCmake};{pathToNinja}";
            environmentDict["Path"] = environmentDict["PATH"];

            return environmentDict;
        }
    }
}
