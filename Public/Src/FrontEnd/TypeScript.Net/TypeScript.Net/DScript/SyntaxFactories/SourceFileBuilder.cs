// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.Types;

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// A builder class for constructing source files.
    /// </summary>
    public sealed class SourceFileBuilder
    {
        private readonly List<IStatement> m_statements = new List<IStatement>();

        /// <nodoc />
        public SourceFileBuilder Statement(IStatement statement)
        {
            Contract.Requires(statement != null);
            m_statements.Add(statement);
            return this;
        }

        /// <nodoc />
        public SourceFileBuilder SemicolonAndBlankLine()
        {
            return Statement(new BlankLineStatement());
        }

        /// <nodoc />
        public ISourceFile Build()
        {
            return new SourceFile(m_statements.ToArray());
        }
    }
}
