// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGen
{
    /// <summary>
    /// Logging class that contains the logging sites
    /// </summary>
    internal sealed class LoggingClass
    {
        /// <nodoc />
        public ISymbol Symbol {get;}

        /// <summary>
        /// The unique name for the loggers to generate
        /// </summary>
        public string Name {get;}

        /// <nodoc />
        public IList<LoggingSite> Sites {get;} = new List<LoggingSite>();

        /// <nodoc />
        public LoggingClass(string name, ISymbol symbol)
        {
            Name = name;
            Symbol = symbol;
        }
    }
}
