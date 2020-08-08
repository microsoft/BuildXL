// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.Cache.Roxis.Common
{
    /// <summary>
    /// Batch of commands to be processed by a Roxis service
    /// </summary>
    public class CommandRequest
    {
        public IReadOnlyList<Command> Commands { get; }

        public CommandRequest(IReadOnlyList<Command> commands)
        {
            Commands = commands;
        }

        public static CommandRequest Deserialize(BuildXLReader reader)
        {
            var commands = reader.ReadReadOnlyList(r => Command.Deserialize(r));
            return new CommandRequest(commands);
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteReadOnlyList(Commands, (w, c) => c.Serialize(w));
        }
    }
}
