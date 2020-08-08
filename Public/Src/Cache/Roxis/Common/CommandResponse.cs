// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.Cache.Roxis.Common
{
    /// <summary>
    /// Responses to a batch of commands (i.e. <see cref="CommandRequest"/>)
    /// </summary>
    public class CommandResponse
    {
        public IReadOnlyList<CommandResult> Results { get; }

        public CommandResponse(IReadOnlyList<CommandResult> results)
        {
            Results = results;
        }

        public static CommandResponse Deserialize(BuildXLReader reader)
        {
            var results = reader.ReadReadOnlyList(r => CommandResult.Deserialize(r));
            return new CommandResponse(results);
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteReadOnlyList(Results, (w, c) => c.Serialize(w));
        }
    }
}
