// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Roxis.Common;

namespace Roxis
{
    public static class CommandExtensions
    {
        public static Result<Command> FromString(string command, string[] parameters)
        {
            try
            {
                if (!Enum.TryParse<CommandType>(command, ignoreCase: true, out var parsedCommandType))
                {
                    throw new ArgumentException(message: $"Could not parse `{nameof(CommandType)}` from string `{command}`", paramName: nameof(command));
                }

                Command? parsedCommand = null;
                switch (parsedCommandType)
                {
                    case CommandType.Get:
                        parsedCommand = new GetCommand();
                        break;
                    case CommandType.Set:
                        parsedCommand = new SetCommand()
                        {
                            Value = Encoding.UTF8.GetBytes(parameters[1]),
                        };
                        break;
                    case CommandType.CompareExchange:
                        parsedCommand = new CompareExchangeCommand()
                        {
                            Comparand = Encoding.UTF8.GetBytes(parameters[1]),
                            Value = Encoding.UTF8.GetBytes(parameters[2]),
                        };
                        break;
                    case CommandType.CompareRemove:
                        parsedCommand = new CompareRemoveCommand()
                        {
                            Comparand = Encoding.UTF8.GetBytes(parameters[1]),
                        };
                        break;
                    case CommandType.Remove:
                        parsedCommand = new RemoveCommand();
                        break;
                    case CommandType.PrefixEnumerate:
                        parsedCommand = new PrefixEnumerateCommand();
                        break;
                }

                Contract.Assert(parsedCommand != null);

                parsedCommand.Key = Encoding.UTF8.GetBytes(parameters[0]);

                return new Result<Command>(parsedCommand);
            }
            catch (Exception e)
            {
                return new Result<Command>(e, message: $"Failed to parse request `{command}` with parameters `{string.Join(", ", parameters)}`");
            }
        }
    }
}
