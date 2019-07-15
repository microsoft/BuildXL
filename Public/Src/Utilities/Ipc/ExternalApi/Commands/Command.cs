// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Base class for all commands constituting the BuildXL External API.
    ///
    /// This class provides template methods for serialization and deserialization.
    /// </summary>
    /// <remarks>
    /// Every time a new concrete Command class is added, the <see cref="Deserialize"/>
    /// method must be updated.
    /// </remarks>
    public abstract class Command
    {
        /// <summary>
        /// Serializes given command to string.
        /// </summary>
        /// <remarks>
        /// This method creates a <see cref="BinaryWriter"/> and expects subclasses to
        /// perform their specific serialization to it.  That means that the subclasses
        /// are allowed to write binary values, and at the end, this method will encode
        /// the resulting byte array into string (currently using Base64).
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2202:StreamDisposeMultipleTimes", Justification = "False positive")]
        public static string Serialize(Command command)
        {
            Contract.Requires(command != null);

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(command.GetType().Name);
                command.InternalSerialize(writer);
                writer.Flush();

                return Convert.ToBase64String(stream.ToArray());
            }
        }

        /// <summary>
        /// Deserializes a command from a string.
        /// </summary>
        /// <remarks>
        /// Every time a new concrete Command class is added, this method must be updated
        /// by adding a case statement to the switch.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2202:StreamDisposeMultipleTimes", Justification = "False positive")]
        public static Command Deserialize(string value)
        {
            Contract.Requires(value != null);

            using (var stream = new MemoryStream(Convert.FromBase64String(value)))
            using (var reader = new BinaryReader(stream))
            {
                var typeName = reader.ReadString();
                switch (typeName)
                {
                    case nameof(MaterializeFileCommand):
                        return MaterializeFileCommand.InternalDeserialize(reader);
                    case nameof(ReportStatisticsCommand):
                        return ReportStatisticsCommand.InternalDeserialize(reader);
                    case nameof(GetSealedDirectoryContentCommand):
                        return GetSealedDirectoryContentCommand.InternalDeserialize(reader);
                    default:
                        Contract.Assert(false, "unrecognized command type name: " + typeName);
                        return null;
                }
            }
        }

        /// <summary>
        /// Serializes the command to a given binary writer.  Any binary value may be
        /// written to <paramref name="writer"/>, but the output of <see cref="Serialize(Command)"/>
        /// will be fully
        /// </summary>
        internal abstract void InternalSerialize(BinaryWriter writer);
    }

    /// <summary>
    /// Command parameterized by the type of its result.
    /// </summary>
    /// <typeparam name="T">Type of the result of this command.</typeparam>
    public abstract class Command<T> : Command
    {
        /// <summary>
        /// Attempt to parse the result value of this command from a string.
        /// </summary>
        public abstract bool TryParseResult(string result, out T commandResult);

        /// <summary>
        /// Serialize a result of this command to a string.
        /// </summary>
        public abstract string RenderResult(T commandResult);
    }
}
