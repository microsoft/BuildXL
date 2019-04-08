// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities.Qualifier;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script
{
    /// <nodoc />
    [DebuggerDisplay("{ToDebuggerDisplay,nq}")]
    public sealed class DisplayStackTraceEntry
    {
        /// <nodoc />
        public string File { get; }

        /// <nodoc />
        public int Line { get; }

        /// <nodoc />
        public int Position { get; }

        /// <nodoc />
        public string FunctionName { get; }

        /// <nodoc />
        public StackEntry Entry { get; }

        /// <nodoc />
        public DisplayStackTraceEntry(string file, int line, int position, string functionName, StackEntry entry)
        {
            File = file;
            Line = line;
            Position = position;
            FunctionName = functionName;
            Entry = entry;
        }
        
        /// <nodoc />
        public DisplayStackTraceEntry(Location location, string functionName, StackEntry entry)
            : this(location.File, location.Line, location.Position, functionName, entry) { }

        /// <nodoc />
        internal Location Location()
        {
            return new Location { File = File, Line = Line, Position = Position };
        }

        /// <nodoc />
        public string ToDisplayString(QualifierTable qualifierTable)
        {
            Contract.Requires(qualifierTable != null);

            return StringHelper(qualifierTable);
        }

        /// <nodoc />
        public string ToDebuggerDisplay()
        {
            return StringHelper(null);
        }

        private string StringHelper(QualifierTable qualifierTable)
        {
            var location = Location().ToDisplayString();
            string qualifier = "?";
            if (Entry?.Env != null && qualifierTable != null)
            {
                var qualifierId = Entry.Env.Qualifier.QualifierId;
                qualifier = qualifierTable.GetFriendlyUserString(qualifierId);
            }

            return FunctionName != null
                ? I($"  {location}: at {FunctionName} [{qualifier}]")
                : I($"  {location} [{qualifier}]");
        }
    }
}
