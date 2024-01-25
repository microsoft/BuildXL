// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.Ide.Generator
{
    internal enum ProcessType
    {
        None,
        Csc,
        ResGen,
        XUnit,
        VsTest,
        Xaml,
        Cl,
        Link,
    }

    internal struct ProcessWithType
    {
        /// <summary>
        /// ProcessType
        /// </summary>
        public ProcessType Type;

        /// <summary>
        /// Process pip
        /// </summary>
        public Process Process;

        /// <summary>
        /// Constructs an instance of ProcessWithType
        /// </summary>
        public ProcessWithType(ProcessType type, Process pip)
        {
            Type = type;
            Process = pip;
        }

        /// <summary>
        /// Categorizes a process
        /// </summary>
        public static ProcessWithType Categorize(Context context, Process process)
        {
            ProcessType type = ProcessType.None;
            var stringTable = context.StringTable;

            var toolName = process.GetToolName(context.PathTable);
            if (toolName.CaseInsensitiveEquals(stringTable, context.CscExeName) ||
                (IsDotNetTool(toolName, context) &&
                 FirstArgIsPathWithFileName(context, process, context.CscDllName)))
            {
                type = ProcessType.Csc;
            }
            else if (IsDotNetTool(toolName, context) &&
                FirstArgIsPathWithFileName(context, process, context.XunitConsoleDllName))
            {
                type = ProcessType.XUnit;
            }
            else if (toolName.CaseInsensitiveEquals(stringTable, context.XunitConsoleExeName))
            {
                type = ProcessType.XUnit;
            }
            else if (toolName.CaseInsensitiveEquals(stringTable, context.QtestExeName))
            {
                type = ProcessType.XUnit;
            }
            else if (IsXUnitWrappedInBash(context, process, toolName))
            {
                type = ProcessType.XUnit;
            }
            else if (toolName.CaseInsensitiveEquals(stringTable, context.VsTestExeName))
            {
                type = ProcessType.VsTest;
            }
            else if (toolName.CaseInsensitiveEquals(stringTable, context.ResgenExeName))
            {
                type = ProcessType.ResGen;
            }
            else if (toolName.CaseInsensitiveEquals(stringTable, context.ClExeName))
            {
                type = ProcessType.Cl;
            }
            else if (toolName.CaseInsensitiveEquals(stringTable, context.LinkExeName))
            {
                type = ProcessType.Link;
            }

            return new ProcessWithType(type, process);
        }

        private static bool IsDotNetTool(PathAtom toolName, Context context) 
        {
            return toolName.CaseInsensitiveEquals(context.StringTable, context.DotnetName) || toolName.CaseInsensitiveEquals(context.StringTable, context.DotnetExeName);
        }

        private static bool FirstArgIsPathWithFileName(Context context, Process process, PathAtom name)
        {
            var arguments = context.GetArgumentsDataFromProcess(process);
            if (arguments.FragmentCount == 0)
            {
                return false;
            }

            var firstArg = arguments.First();
            if (firstArg.FragmentType != PipFragmentType.AbsolutePath)
            {
                return false;
            }

            var path = firstArg.GetPathValue();
            if (!path.IsValid)
            {
                return false;
            }

            return path.GetName(context.PathTable).CaseInsensitiveEquals(context.StringTable, name);
        }

        private static bool IsXUnitWrappedInBash(Context context, Process process, PathAtom toolName)
        {
            // See Public/Sdk/Public/Managed/Testing/XUnit/xunitframework.dsc on how xunit processes get wrapped in bash
            // Typically there will be a few arguments before dotnet/xunit as args into bash and to set the executable bit
            if (toolName == context.BashName)
            {
                var arguments = context.GetArgumentsDataFromProcess(process);
                if (arguments.FragmentCount >= 5)
                {
                    for (int i = 0; i < arguments.Count(); i++)
                    {
                        if (arguments.ElementAt(i).FragmentType == PipFragmentType.AbsolutePath)
                        {
                            var elem = arguments.ElementAt(i).GetPathValue();
                            if (i < arguments.Count() - 1 && elem.IsValid && IsDotNetTool(elem.GetName(context.PathTable), context))
                            {
                                elem = arguments.ElementAt(i).GetPathValue();
                                return elem.GetName(context.PathTable).CaseInsensitiveEquals(context.StringTable, context.XunitConsoleDllName);
                            }

                            // If the first path was not dotnet and the second path after that was not xunit, then this is not an xunit pip
                            break;
                        }
                    }
                }
            }

            return false;
        }
    }
}
