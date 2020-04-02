// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Interprets the set of commands and its dependencies as specified in <see cref="IRushResolverSettings.Commands"/>
    /// </summary>
    public static class RushCommandsInterpreter
    {
        /// <summary>
        /// Computes the set of commands and dependencies requested for a rush build and performs all validations
        /// </summary>
        /// <remarks>
        /// All validation errors are logged here
        /// </remarks>
        public static bool TryComputeAndValidateCommands(
            LoggingContext context,
            Location location,
            IReadOnlyList<DiscriminatingUnion<string, IRushCommand>> commands,
            out IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> computedCommands)
        {
            if (!ComputeCommands(context, location, commands, out computedCommands))
            {
                // Error is already logged
                return false;
            }

            return ValidateNoCycles(context, location, computedCommands);
        }

        private static bool ComputeCommands(
            LoggingContext context,
            Location location,
            IReadOnlyList<DiscriminatingUnion<string, IRushCommand>> commands,
            out IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> resultingCommands)
        {
            if (commands == null)
            {
                // If not defined, the default is ["build"]
                commands = new[] { new DiscriminatingUnion<string, IRushCommand>("build") };
            }

            var computedCommands = new Dictionary<string, IReadOnlyList<IRushCommandDependency>>(commands.Count);
            resultingCommands = computedCommands;

            for (int i = 0; i < commands.Count; i++)
            {
                DiscriminatingUnion<string, IRushCommand> command = commands[i];
                string commandName = command.GetCommandName();

                if (string.IsNullOrEmpty(commandName))
                {
                    Tracing.Logger.Log.RushCommandIsEmpty(context, location);
                    return false;
                }

                if (computedCommands.ContainsKey(commandName))
                {
                    Tracing.Logger.Log.RushCommandIsDuplicated(context, location, commandName);
                    return false;
                }

                if (command.GetValue() is string simpleCommand)
                {
                    // A simple string command first on the list means depending on the same command of all its dependencies.
                    // Canonical example: 'build'
                    if (i == 0)
                    {
                        computedCommands.Add(
                            simpleCommand,
                            new[] { new RushCommandDependency { Command = simpleCommand, Kind = RushCommandDependency.Package } });
                    }
                    else
                    {
                        // A simple string command that is not first in the list means depending on the immediate predecesor in the list
                        // Canonical example: 'build', 'test'
                        computedCommands.Add(
                            simpleCommand,
                            new[] { new RushCommandDependency { Command = commands[i - 1].GetCommandName(), Kind = RushCommandDependency.Local } });
                    }
                }
                else
                {
                    // Otherwise if a full fledge command is specified, then we honor it as is
                    var rushCommand = (IRushCommand)command.GetValue();
                    computedCommands.Add(commandName, rushCommand.DependsOn);
                }
            }

            return true;
        }

        private static bool ValidateNoCycles(
            LoggingContext context,
            Location location,
            IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> commands)
        {
            // Dependencies on 'package' commands can never form a cycle (cycles across projects is validated at pip scheduling level)
            // So we only need to make sure there is not a cycle across 'local' dependencies
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            var cycle = new Stack<string>();

            foreach (string command in commands.Keys)
            {
                if (HasCycleInCommands(command, commands, visited, visiting, cycle))
                {
                    // The returned stack needs to be reversed so, when enumerating, the traversal goes from dependency to dependent
                    Tracing.Logger.Log.CycleInRushCommands(context, location, string.Join(" -> ", cycle.Reverse()));
                    return false;
                }
            }
            return true;
        }

        private static bool HasCycleInCommands(string command, IReadOnlyDictionary<string, IReadOnlyList<IRushCommandDependency>> commands, HashSet<string> visited, HashSet<string> visiting, Stack<string> cycle)
        {
            // We have already seen this command and know there are no cycles under it
            if (visited.Contains(command))
            {
                return false;
            }

            cycle.Push(command);

            // We found a cycle
            if (visiting.Contains(command))
            {
                return true;
            }

            visiting.Add(command);

            if (commands.TryGetValue(command, out IReadOnlyList<IRushCommandDependency> dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    // Package dependencies are never part of cycles
                    if (!dependency.IsLocalKind())
                    {
                        continue;
                    }

                    if (HasCycleInCommands(dependency.Command, commands, visited, visiting, cycle))
                    {
                        return true;
                    }
                }
            }
            cycle.Pop();
            visited.Add(command);
            visiting.Remove(command);

            return false;
        }
    }
}
