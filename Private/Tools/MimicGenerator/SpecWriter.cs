// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Writes a spec
    /// </summary>
    public abstract class SpecWriter : BuildXLFileWriter
    {
        /// <summary>
        /// Constructor
        /// </summary>
        protected SpecWriter(string absolutePath)
            : base(absolutePath) { }

        /// <summary>
        /// Adds a SealPartialDirectory runner
        /// </summary>
        /// <param name="valueName">Name of the resulting value</param>
        /// <param name="relativeDir">the relative path to the directory root</param>
        /// <param name="relativePaths">the relative paths of the files within the directory</param>
        public abstract void AddSealDirectory(string valueName, string relativeDir, IEnumerable<string> relativePaths);

        /// <summary>
        /// Data for a Mimic.exe output file
        /// </summary>
        public sealed class MimicFileOutput
        {
            /// <summary>
            /// Path of the file
            /// </summary>
            public string Path;

            /// <summary>
            /// File content to be repeated up to LengthInBytes
            /// </summary>
            public string RepeatingContent;

            /// <summary>
            /// Length of file that should be created
            /// </summary>
            public int LengthInBytes;

            /// <summary>
            /// The ID of the file
            /// </summary>
            public int FileId;
        }

        /// <summary>
        /// Adds a Mimic processes
        /// </summary>
        /// <param name="outputValue">Name of the output value</param>
        /// <param name="sealDirectoryInputs">SealDirectory inputs. Should already be in the form of a expression</param>
        /// <param name="pathInputs">inputs</param>
        /// <param name="outputs">outputs</param>
        /// <param name="observedAccessesPath">Path to file containing observed file accesses</param>
        /// <param name="semaphores">semaphores</param>
        /// <param name="runTimeInMs">diration for the mimic invocation</param>
        /// <param name="isLongestProcess">true if this is the longest running process in a spec</param>
        public abstract void AddMimicInvocation(
            string outputValue,
            IEnumerable<string> sealDirectoryInputs,
            IEnumerable<string> pathInputs,
            IEnumerable<MimicFileOutput> outputs,
            string observedAccessesPath,
            IEnumerable<SemaphoreInfo> semaphores,
            int runTimeInMs,
            bool isLongestProcess = false);

        public abstract void AddWriteFile(string valueName, string relativeDestination);

        public abstract void AddCopyFile(string valueName, string relativeSource, string relativeDestination);

        public abstract string GetProcessInputName(string variableName, string specPath, int depId);

        public abstract string GetSealCopyWriteInputName(string variableName, string specPath);

        /// <summary>
        /// Writes the observedAccesses to a file next to the generated spec. This file may be consumed by the Mimic.exe
        /// pip to more faithfully reply file accesses
        /// </summary>
        /// <returns>Absolute path to the ObservedAccesses file</returns>
        public string WriteObservedAccessesFile(ObservedAccess[] accesses, int lookupId)
        {
            if (lookupId == -1 || accesses == null)
            {
                return null;
            }

            // Write the observed accesses in a file next to the
            string destinationDir = Path.Combine(Path.GetDirectoryName(AbsolutePath), "observedAccesses");
            Directory.CreateDirectory(destinationDir);
            string destination = Path.Combine(destinationDir, "Pip_" + lookupId.ToString(CultureInfo.InvariantCulture) + ".txt");

            using (StreamWriter writer = new StreamWriter(destination))
            {
                writer.WriteLine(ObservedAccessesVersion);

                foreach (var access in accesses)
                {
                    // Observed inputs are full paths like c:\foo\bar. Strip out the ':' to make them relative paths to
                    // where input files are generated.
                    writer.WriteLine(access.Path.Replace(":", string.Empty));
                    switch (access.ObservedAccessType)
                    {
                        case ObservedAccessType.AbsentPathProbe:
                            writer.WriteLine("A");
                            break;
                        case ObservedAccessType.DirectoryEnumeration:
                            writer.WriteLine("D");
                            break;
                        case ObservedAccessType.FileContentRead:
                            writer.WriteLine("F");
                            break;
                        default:
                            throw new MimicGeneratorException("Unknown observed access type {0}", access.ObservedAccessType);
                    }
                }
            }

            return destination;
        }

        private const int ObservedAccessesVersion = 1;
    }
}
