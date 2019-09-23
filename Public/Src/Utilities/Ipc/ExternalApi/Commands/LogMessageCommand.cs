// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.LogMessage"/> API operation.
    /// </summary>
    public sealed class LogMessageCommand : Command<bool>
    {
        /// <summary>Nessage to be logged.</summary>
        public string Message { get; }

        /// <summary>Whether the message is to be logged as a warning or verbose.</summary>
        public bool IsWarning { get; }

        /// <nodoc />
        public LogMessageCommand(string message, bool isWarning)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(message));

            Message = message;
            IsWarning = isWarning;
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out bool commandResult)
        {
            commandResult = false;
            return bool.TryParse(result, out commandResult);
        }

        /// <inheritdoc />
        public override string RenderResult(bool commandResult)
        {
            return commandResult.ToString();
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(Message);
            writer.Write(IsWarning);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var message = reader.ReadString();
            bool isWarning = reader.ReadBoolean();
            return new LogMessageCommand(message, isWarning);
        }
    }
}
