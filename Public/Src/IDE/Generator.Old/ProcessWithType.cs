// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;

namespace BuildXL.Ide.Generator.Old
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
            if (toolName.CaseInsensitiveEquals(stringTable, context.CscExeName))
            {
                type = ProcessType.Csc;
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
    }
}
