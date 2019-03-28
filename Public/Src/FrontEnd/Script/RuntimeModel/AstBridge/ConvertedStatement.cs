// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Statements;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Union type that represent one or many converted statements.
    /// </summary>
    /// <remarks>
    /// This wrapper is required for performance reasons because it avoids unneccessary allocations for creating unneeded arrays.
    /// Basically, this class is a mimic for: ConvertedStatemnet = OneStatement | ManyStatements;
    /// </remarks>
    internal sealed class ConvertedStatement
    {
        private readonly IReadOnlyList<Statement> m_statements;
        private readonly Statement m_statement;

        public ConvertedStatement(Statement statement)
        {
            Contract.Requires(statement != null);
            m_statement = statement;
        }

        public ConvertedStatement(IReadOnlyList<Statement> statements)
        {
            Contract.Requires(statements != null);

            m_statements = statements;
        }

        // Could be null!
        public Statement Statement => m_statement ?? m_statements.FirstOrDefault();

        public static implicit operator ConvertedStatement(Statement source)
        {
            // This is just for null propagation
            if (source == null)
            {
                return null;
            }

            return new ConvertedStatement(source);
        }

        public static implicit operator ConvertedStatement(List<Statement> statements)
        {
            Contract.Requires(statements != null);
            return new ConvertedStatement(statements);
        }

        public static IList<Statement> FlattenStatements(ICollection<ConvertedStatement> convertedStatements)
        {
            Contract.Requires(convertedStatements != null);

            var result = new List<Statement>(convertedStatements.Count);
            foreach (var statement in convertedStatements)
            {
                if (statement.m_statement != null)
                {
                    result.Add(statement.m_statement);
                }
                else
                {
                    result.AddRange(statement.m_statements);
                }
            }

            return result;
        }
    }
}
