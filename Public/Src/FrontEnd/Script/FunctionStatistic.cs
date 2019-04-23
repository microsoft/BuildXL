// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Captures invocation statistic for a function in DScript.
    /// </summary>
    public sealed class FunctionStatistic
    {
        /// <nodoc />
        public long Occurrences;

        /// <nodoc />
        public long Elapsed;

        /// <nodoc />
        public string FullName { get; }

        /// <nodoc />
        public bool IsValid { get; }

        private FunctionStatistic()
        {
            IsValid = false;
            FullName = string.Empty;
        }

        /// <summary>
        /// Creates statistic for a user-defined function with a given full name.
        /// </summary>
        public FunctionStatistic(string fullName)
        {
            FullName = fullName;
            IsValid = true;
        }

        /// <summary>
        /// Creates statistic for a user-defined function or to a member function.
        /// </summary>
        public FunctionStatistic(List<SymbolAtom> fullName, CallSignature callSignature, StringTable stringTable)
            : this(GetFullNameAsString(fullName, callSignature, stringTable))
        {}

        /// <summary>
        /// Creates statistic for ambient function.
        /// </summary>
        public FunctionStatistic(SymbolAtom namespaceName, SymbolAtom name, CallSignature callSignature, StringTable stringTable)
            : this(GetFullName(namespaceName, name), callSignature, stringTable)
        {
        }

        /// <summary>
        /// Gets the null-object instance that doesn't track invocation statistics.
        /// </summary>
        public static FunctionStatistic Empty { get; } = new FunctionStatistic();

        /// <nodoc />
        public void TrackInvocation(Context context, long durationInTicks)
        {
            if (IsValid)
            {
                var incrementedOccurrences = Interlocked.Increment(ref Occurrences);
                Interlocked.Add(ref Elapsed, durationInTicks);
                if (incrementedOccurrences == 1)
                {
                    context.RegisterStatistic(this);
                }
            }
        }

        /// <nodoc />
        public static string GetFullNameAsString(List<SymbolAtom> fullName, CallSignature callSignature, StringTable stringTable)
        {
            string fullNameStr = string.Join(".", fullName.Select(n => n.ToString(stringTable)));

            string callSignatureStr = callSignature?.ToStringShort(stringTable) ?? string.Empty;

            return fullNameStr + callSignatureStr;
        }

        /// <nodoc />
        public static List<SymbolAtom> GetFullName(SymbolAtom @namespace, SymbolAtom name)
        {
            var result = new List<SymbolAtom>();

            // Namespace is optional (for instance, for globR from the prelude).
            if (@namespace.IsValid)
            {
                result.Add(@namespace);
            }

            Contract.Assert(name.IsValid);
            result.Add(name);

            return result;
        }
    }
}
