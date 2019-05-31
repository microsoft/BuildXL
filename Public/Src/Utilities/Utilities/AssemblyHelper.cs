// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A helper class to get Assembly locations
    /// </summary>
    public static class AssemblyHelper
    {
        private static readonly ConcurrentDictionary<string, string> s_assemblyLocationCache = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Returns the location of a given assembly trying several strategies to obtain the correct path
        /// </summary>
        /// <param name="assembly">The assembly to get the location for</param>
        /// <param name="computeAssemblyLocation">If true, then <see cref="Assembly.Location"/> is not used directly and the location is computed based on the executable's current path.</param>
        /// <remarks>
        /// The CoreCLR has changed the way the Assembly.Location works, thus for assemblies that are loaded via the
        /// <see cref="Assembly.GetManifestResourceStream(string)"/> APIs the location by default returns String.Empty. If we can get the location
        /// the standard way, we return it - otherwise we use process inspection."/>
        /// </remarks>
        public static string GetAssemblyLocation(Assembly assembly, bool computeAssemblyLocation = false)
        {
            var location = assembly.Location;
            var an = assembly.GetName();

            // Default, if the .NET CLR can resolve the assembly location at runtime
            if (!computeAssemblyLocation && !string.IsNullOrEmpty(location))
            {
                return location;
            }
            
            // This only applies to the CoreCLR or when computeAssemblyLocation is specified (for instance, for the front end).
            if (location.Length == 0 || computeAssemblyLocation)
            {
                if(s_assemblyLocationCache.TryGetValue(an.Name, out location))
                {
                    return location;
                }

                var runningUnitTests = AppDomain.CurrentDomain.GetAssemblies().Any(entry => 
                    entry.FullName.IndexOf("xunit", StringComparison.OrdinalIgnoreCase) >= 0 || 
                    entry.FullName.IndexOf("vstest", StringComparison.OrdinalIgnoreCase) >= 0);

                if (runningUnitTests)
                {
                    // We rely on the fact that the PipBuilder injects the current working directory into every process it creates
                    location = Path.Combine(System.Environment.CurrentDirectory, assembly.GetModules()[0].ScopeName);
                    s_assemblyLocationCache.TryAdd(an.Name, location);
                    
                    return location;
                }

                // Just return the fully qualified path of the current process, as it yields the same value as the 'Location' property
                // on assembly objects received from Assembly.GetEntryAssembly() or Assembly.GetCallingAssembly()
                var process = Process.GetCurrentProcess();
                location = process.MainModule.FileName;
                s_assemblyLocationCache.TryAdd(an.Name, location);
                
                return location;
            }

            var description = string.Format("Failed to infer the assembly location for (Name={0}, Version={1}, Culture={2}, PublicKey token={3})", an.Name, an.Version, an.CultureInfo.Name, BitConverter.ToString(an.GetPublicKeyToken()));
            throw new BuildXLException(description, ExceptionRootCause.MissingRuntimeDependency);
        }

        /// <summary>
        /// Same as <see cref="GetAssemblyLocation(Assembly, bool)"/>
        /// </summary>
        public static string GetLocation(this Assembly assembly, bool computeAssemblyLocation = false)
        {
            return GetAssemblyLocation(assembly, computeAssemblyLocation);
        }

        /// <summary>
        /// Calls <see cref="AdjustExeExtension(string)"/> for the location of the entry assembly
        /// </summary>
        public static string GetThisProgramExeLocation() => AdjustExeExtension(Assembly.GetEntryAssembly().GetLocation());

        /// <summary>
        /// Adjusts the extension of the supplied path:
        ///   - if the extension is .dll and we are running on Windows --> change extension to .exe
        ///   - if the extension is .dll and we are running on non-Windows --> drop the extension
        ///   - else --> return as is
        /// </summary>
        /// <param name="entryAssemblyLocation">Absolute path the the executing assembly</param>
        public static string AdjustExeExtension(string entryAssemblyLocation)
        {
            if (entryAssemblyLocation.EndsWith(".dll"))
            {
                return OperatingSystemHelper.IsUnixOS
                    ? entryAssemblyLocation.Substring(0, entryAssemblyLocation.Length - 4)
                    : Path.ChangeExtension(entryAssemblyLocation, "exe");
            }
            else
            {
                return entryAssemblyLocation;
            }
        }

        /// <summary>
        /// Returns the same as <see cref="Environment.GetCommandLineArgs"/> except that the first
        /// element (which is the name of the executable) is adjusted by calling
        /// <see cref="AdjustExeExtension(string)"/> on it.
        /// </summary>
        public static string[] GetCommandLineArgs()
        {
            var cmdLineArgs = Environment.GetCommandLineArgs();
            return new[] { AdjustExeExtension(cmdLineArgs[0]) }
                .Concat(cmdLineArgs.Skip(1))
                .ToArray();
        }
    }
}
