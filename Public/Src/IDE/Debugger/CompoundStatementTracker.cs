// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Statements;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    ///     Used by debugger to track the execution of compound statements, such as for(...){} statement.
    /// </summary>
    /// <remarks>
    ///     If we ever add new structural statements to DScript, add debugger support here. As of 2016/07,
    ///     the following statements are considered structural:
    ///         ForOfStatement
    ///         ForStatement
    ///         SwitchStatement
    ///     BlockStatement is not considered structural.
    /// </remarks>
    public sealed class CompoundStatementTracker : INodeTracker
    {
        private readonly Stack m_stack;

        /// <nodoc/>
        public CompoundStatementTracker()
        {
            m_stack = new Stack();
        }

        /// <inheritdoc/>
        public Stack Stack => m_stack;

        /// <summary>
        ///     Enters into a new structural statement node.
        /// </summary>
        /// <returns>
        ///     <code>true</code>if entering into a new structural statement; <code>false</code> if the given node is not a structural statement.
        /// </returns>
        public bool Enter(Node node)
        {
            var adapter = GetAdapter(node);
            if (adapter != null)
            {
                m_stack.Push(adapter);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        ///     Checks if the engine should pause at the given node when asked to step over.
        /// </summary>
        /// <returns>
        ///     <code>null</code> if to follow the default behavior.
        /// </returns>
        public static bool? ShouldPauseWhenSteppingOver(Stack stack, Node node)
        {
            if (stack.Count > 0)
            {
                var adapter = stack.Peek() as ICompoundStatementAdapter;
                return adapter?.ShouldPauseWhenSteppingOver(node);
            }

            return null;
        }

        /// <summary>
        ///     Exits the current structural statement node.  Must call this iff <see cref="Enter"/> was successful.
        ///     No consistency check is enforced inside this method.
        /// </summary>
        public void Exit(Node node)
        {
            if (m_stack.Count > 0)
            {
                var adapter = m_stack.Pop() as ICompoundStatementAdapter;
                Contract.Assert(node == adapter.Node);
            }
        }

        private static ICompoundStatementAdapter GetAdapter(Node node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ForOfStatement: return new ForOfStatementAdapter((ForOfStatement)node);
                case SyntaxKind.ForStatement: return new ForStatementAdapter((ForStatement)node);
                case SyntaxKind.SwitchStatement: return new SwitchStatementAdapter((SwitchStatement)node);
            }

            return null;
        }
    }

    #region Compound statement adapters

    internal interface ICompoundStatementAdapter
    {
        /// <summary>
        ///     Check if the engine should pause at the given node when asked to step over.
        /// </summary>
        /// <returns>
        ///     <code>null</code> if to follow the default behavior.
        /// </returns>
        bool? ShouldPauseWhenSteppingOver(Node node);

        /// <summary>
        ///     Node for which this adapter was created.
        /// </summary>
        Node Node { get; }
    }

    internal sealed class ForStatementAdapter : ICompoundStatementAdapter
    {
        private readonly ForStatement m_forStatement;

        public Node Node => m_forStatement;

        internal ForStatementAdapter(ForStatement forStatement)
        {
            m_forStatement = forStatement;
        }

        public bool? ShouldPauseWhenSteppingOver(Node node)
        {
            // Do not pause at initializer
            if (m_forStatement.Initializer != null && m_forStatement.Initializer == node)
            {
                return false;
            }

            // Always pause at incrementor
            if (m_forStatement.Incrementor != null && m_forStatement.Incrementor == node)
            {
                return true;
            }

            return null;
        }
    }

    internal sealed class ForOfStatementAdapter : ICompoundStatementAdapter
    {
        private readonly ForOfStatement m_forOfStatement;
        private bool m_firstTime;

        public Node Node => m_forOfStatement;

        internal ForOfStatementAdapter(ForOfStatement forOfStatement)
        {
            m_forOfStatement = forOfStatement;
            m_firstTime = true;
        }

        public bool? ShouldPauseWhenSteppingOver(Node node)
        {
            if (m_forOfStatement.Name == node)
            {
                if (m_firstTime)
                {
                    m_firstTime = false;
                    return false;
                }

                return true;
            }

            return null;
        }
    }

    internal sealed class SwitchStatementAdapter : ICompoundStatementAdapter
    {
        private readonly SwitchStatement m_switchStatement;
        private readonly ISet<Node> m_caseExpressionNodes;

        public Node Node => m_switchStatement;

        internal SwitchStatementAdapter(SwitchStatement switchStatement)
        {
            m_switchStatement = switchStatement;
            m_caseExpressionNodes = new HashSet<Node>(switchStatement.CaseClauses.Select(cc => cc.CaseExpression));
        }

        public bool? ShouldPauseWhenSteppingOver(Node node)
        {
            // Technically, a case clause contains case expression and statements. However, the
            // expression is not covered in by case clause's location range and gets Eval()ed
            // separately. So we must capture it in advance.
            //
            // case ................... : ...................
            //     |- case expression -| |- case statements -|
            //              /|\
            //               +---- REF ----+
            //                           |-+- case clause ---|
            //
            // We want to pause at the case expression, and each case statement.

            // Don't expect this and irrelevant, ignore
            if (node.Kind == SyntaxKind.CaseClause)
            {
                return false;
            }

            if (node.Kind == SyntaxKind.DefaultClause)
            {
                return false;
            }

            // Check if we hit the case expression
            // (Some optimization may be required in the future. We don't want to check every single expression
            // contained in SwitchStatement. We just want to pause at the expression immediately following case
            // keyword. So only if there were a better way to tell them apart...)
            Expression expr = node as Expression;
            if (expr != null && m_caseExpressionNodes.Contains(expr))
            {
                return true;
            }

            return null;
        }
    }

    #endregion
}
