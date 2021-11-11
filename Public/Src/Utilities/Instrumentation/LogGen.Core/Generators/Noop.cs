// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.LogGen.Core;

namespace BuildXL.LogGen.Generators
{
    /// <summary>
    /// Generator that doesn't emit anything
    /// </summary>
    public sealed class Noop : GeneratorBase
    {
        /// <inheritdoc/>
        public override void GenerateLogMethodBody(LoggingSite site, Func<string> getMessageExpression)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<Tuple<string, string>> ConsumedNamespaces => Enumerable.Empty<Tuple<string, string>>();

        /// <inheritdoc/>
        public override void GenerateClass()
        {
        }
    }
}
