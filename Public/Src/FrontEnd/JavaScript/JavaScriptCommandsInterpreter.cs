// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Interprets the set of commands and its dependencies to execute in a <see cref="JavaScriptProject"/>
    /// </summary>
    public static class JavaScriptCommandsInterpreter
    {
        /// <summary>
        /// Computes the set of commands and dependencies requested for a JavaScript build and performs all validations
        /// </summary>
        /// <remarks>
        /// All validation errors are logged here
        /// </remarks>
        public static bool TryComputeAndValidateCommands(
            LoggingContext context,
            Location location,
            IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand>> commands,
            out IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> computedCommands)
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
            IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand>> commands,
            out IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> resultingCommands)
        {
            if (commands == null)
            {
                // If not defined, the default is ["build"]
                commands = new[] { new DiscriminatingUnion<string, IJavaScriptCommand>("build") };
            }

            var computedCommands = new Dictionary<string, IReadOnlyList<IJavaScriptCommandDependency>>(commands.Count);
            resultingCommands = computedCommands;

            for (int i = 0; i < commands.Count; i++)
            {
                DiscriminatingUnion<string, IJavaScriptCommand> command = commands[i];
                string commandName = command.GetCommandName();

                if (string.IsNullOrEmpty(commandName))
                {
                    Tracing.Logger.Log.JavaScriptCommandIsEmpty(context, location);
                    return false;
                }

                if (computedCommands.ContainsKey(commandName))
                {
                    Tracing.Logger.Log.JavaScriptCommandIsDuplicated(context, location, commandName);
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
                            new[] { new JavaScriptCommandDependency { Command = simpleCommand, Kind = JavaScriptCommandDependency.Package } });
                    }
                    else
                    {
                        // A simple string command that is not first in the list means depending on the immediate predecesor in the list
                        // Canonical example: 'build', 'test'
                        computedCommands.Add(
                            simpleCommand,
                            new[] { new JavaScriptCommandDependency { Command = commands[i - 1].GetCommandName(), Kind = JavaScriptCommandDependency.Local } });
                    }
                }
                else
                {
                    // Otherwise if a full fledge command is specified, then we honor it as is
                    var javaScriptCommand = (IJavaScriptCommand)command.GetValue();
                    computedCommands.Add(commandName, javaScriptCommand.DependsOn);
                }
            }

            return true;
        }

        private static bool ValidateNoCycles(
            LoggingContext context,
            Location location,
            IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> commands)
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
                    Tracing.Logger.Log.CycleInJavaScriptCommands(context, location, string.Join(" -> ", cycle.Reverse()));
                    return false;
                }
            }
            return true;
        }

        private static bool HasCycleInCommands(string command, IReadOnlyDictionary<string, IReadOnlyList<IJavaScriptCommandDependency>> commands, HashSet<string> visited, HashSet<string> visiting, Stack<string> cycle)
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

            if (commands.TryGetValue(command, out IReadOnlyList<IJavaScriptCommandDependency> dependencies))
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
