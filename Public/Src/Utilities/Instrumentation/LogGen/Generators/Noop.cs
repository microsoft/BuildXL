// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.LogGen.Generators
{
    /// <summary>
    /// Generator that doesn't emit anything
    /// </summary>
    internal sealed class Noop : GeneratorBase
    {
        public override void GenerateLogMethodBody(LoggingSite site, Func<string> getMessageExpression)
        {
        }

        public override IEnumerable<Tuple<string, string>> ConsumedNamespaces => Enumerable.Empty<Tuple<string, string>>();

        /// <inheritdoc/>
        public override void GenerateClass()
        {
        }
    }
}
