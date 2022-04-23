// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Processes.Remoting;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Class to test tagged serialization/deserialization when <see cref="RemoteSandboxedProcessData"/> changes.
    /// </summary>
    internal class ModifiedRemoteData
    {
        private static class DataTag
        {
            // Tag 0 is reserved.

            /// <nodoc/>
            public const byte Executable = 1;

            /// <nodoc/>
            public const byte Arguments = 2;

            /// <nodoc/>
            public const byte EnvVars = 4;

            /// <nodoc/>
            public const byte FileDependency = 5;

            /// <nodoc/>
            public const byte UsefulData1 = byte.MaxValue - 1;

            /// <nodoc/>
            public const byte UsefulData2 = byte.MaxValue;
        }

        /// <summary>
        /// File dependencies.
        /// </summary>
        public readonly IReadOnlySet<string> FileDependencies;

        /// <summary>
        /// File dependencies.
        /// </summary>
        public readonly IReadOnlySet<string> UsefulData2;

        /// <summary>
        /// Executable.
        /// </summary>
        public readonly string Executable;

        /// <summary>
        /// Arguments.
        /// </summary>
        public readonly string Arguments;

        /// <summary>
        /// Working directory.
        /// </summary>
        public readonly string UsefulData1;

        /// <summary>
        /// Environment variables.
        /// </summary>
        public readonly IReadOnlyDictionary<string, string> EnvVars;

        /// <summary>
        /// Creates an instance of <see cref="RemoteSandboxedProcessData"/>.
        /// </summary>
        public ModifiedRemoteData(
            string executable,
            string arguments,
            string usefulData1,
            IReadOnlyDictionary<string, string> envVars,
            IReadOnlySet<string> fileDependencies,
            IReadOnlySet<string> usefulData2)
        {
            Executable = executable;
            Arguments = arguments;
            UsefulData1 = usefulData1;
            EnvVars = envVars;
            FileDependencies = fileDependencies;
            UsefulData2 = usefulData2;
        }

        /// <summary>
        /// Serialize this instance with tagging.
        /// </summary>
        public void TaggedSerialize(BuildXLWriter writer)
        {
            Tag.WriteTaggedString(writer, DataTag.Executable, Executable);
            Tag.WriteTaggedString(writer, DataTag.Arguments, Arguments);
            Tag.WriteTaggedString(writer, DataTag.UsefulData1, UsefulData1);
            Tag.WriteTaggedMap(writer, DataTag.EnvVars, EnvVars, Tag.WriteString, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.FileDependency, FileDependencies, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.UsefulData2, UsefulData2, Tag.WriteString);

            // Must end with end marker.
            Tag.EndWrite(writer);
        }

        /// <summary>
        /// Serialize this instance with tagging to a stream.
        /// </summary>
        public void TaggedSerialize(Stream stream)
        {
            using var writer = new BuildXLWriter(false, stream, true, false);
            TaggedSerialize(writer);
        }

        /// <summary>
        /// Deserializes a tagged instance of <see cref="RemoteSandboxedProcessData"/> using an instance of <see cref="BuildXLReader"/>.
        /// </summary>
        public static ModifiedRemoteData TaggedDeserialize(BuildXLReader reader)
        {
            byte tag;
            string executable = null;
            string arguments = null;
            string usefulData1 = null;
            var envVars = new Dictionary<string, string>();
            var fileDependencies = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);
            var usefulData2 = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);

            string s1 = null;
            string s2 = null;
            int i1 = 0;
            int i2 = 0;

            while (!Tag.ReachedEnd(tag = Tag.ReadTag(reader)))
            {
                ReadTypedValue(reader, ref s1, ref i1, ref s2, ref i2);

                switch (tag)
                {
                    case DataTag.Executable:
                        executable = s1;
                        break;
                    case DataTag.Arguments:
                        arguments = s1;
                        break;
                    case DataTag.UsefulData1:
                        usefulData1 = s1;
                        break;
                    case DataTag.EnvVars:
                        envVars[s1!] = s2!;
                        break;
                    case DataTag.FileDependency:
                        fileDependencies.Add(s1!);
                        break;
                    case DataTag.UsefulData2:
                        usefulData2.Add(s1!);
                        break;
                    default:
                        // Ignore unrecognized tag.
                        break;
                }
            }

            return new ModifiedRemoteData(
                executable,
                arguments,
                usefulData1,
                envVars,
                fileDependencies,
                usefulData2);
        }

        private static void ReadTypedValue(BuildXLReader reader, ref string mainStr, ref int mainNum, ref string altStr, ref int altNum)
        {
            byte type = Tag.ReadType(reader);
            switch (type)
            {
                case TagType.String:
                    mainStr = Tag.ReadString(reader);
                    break;
                case TagType.Number:
                    mainNum = Tag.ReadNumber(reader);
                    break;
                case TagType.Map:
                    ReadTypedValue(reader, ref mainStr, ref mainNum, ref altStr, ref altNum);
                    ReadTypedValue(reader, ref altStr, ref altNum, ref mainStr, ref mainNum);
                    break;
            }
        }


        /// <summary>
        /// Deserializes a tagged instance of <see cref="RemoteSandboxedProcessData"/> from a stream.
        /// </summary>
        public static ModifiedRemoteData TaggedDeserialize(Stream stream)
        {
            using var reader = new BuildXLReader(false, stream, true);
            return TaggedDeserialize(reader);
        }
    }
}
