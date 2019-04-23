// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Statements
{
    /// <summary>
    /// Switch statement.
    /// </summary>
    public class SwitchStatement : Statement
    {
        /// <nodoc />
        public Expression Control { get; }

        /// <nodoc />
        public IReadOnlyList<CaseClause> CaseClauses { get; }

        /// <nodoc />
        [CanBeNull]
        public DefaultClause DefaultClause { get; }

        private Dictionary<EvaluationResult, int> m_caseTable;

        /// <nodoc />
        public SwitchStatement(
            Expression control,
            IReadOnlyList<CaseClause> caseClauses,
            DefaultClause defaultClause,
            LineInfo location)
            : base(location)
        {
            Contract.Requires(control != null);
            Contract.Requires(caseClauses != null);
            Contract.RequiresForAll(caseClauses, c => c != null);

            Control = control;
            CaseClauses = caseClauses;
            DefaultClause = defaultClause;
        }

        /// <nodoc />
        public SwitchStatement(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Control = ReadExpression(context);
            CaseClauses = ReadArrayOf<CaseClause>(context);
            DefaultClause = Read<DefaultClause>(context);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            Control.Serialize(writer);
            WriteArrayOf(CaseClauses, writer);
            Serialize(DefaultClause, writer);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.SwitchStatement;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var clauses = string.Join(Environment.NewLine, CaseClauses.Select(c => c.ToDebugString()));
            clauses = DefaultClause != null ? clauses + Environment.NewLine + DefaultClause.ToDebugString() : clauses;

            return "switch (" + Control.ToDebugString() + ") {" + clauses + "}";
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            var control = Control.Eval(context, env, frame);

            if (control.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            bool foundMatch = false;
            bool reachBreak = false;

            int startCaseIndex = 0;

            if (CaseClauses.Count >= 5)
            {
                if (!TryGetCaseIndex(context, env, frame, control, out bool hasError, out startCaseIndex))
                {
                    if (hasError)
                    {
                        return EvaluationResult.Error;
                    }

                    startCaseIndex = CaseClauses.Count;
                }
                else
                {
                    foundMatch = true;
                }
            }

            for (int i = startCaseIndex; i < CaseClauses.Count; ++i)
            {
                if (!foundMatch)
                {
                    var ccCond = CaseClauses[i].CaseExpression.Eval(context, env, frame);

                    if (ccCond.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    if (EqualityComparer<object>.Default.Equals(control.Value, ccCond.Value))
                    {
                        foundMatch = true;
                        var cc = CaseClauses[i].Eval(context, env, frame);

                        if (cc.Value == BreakValue.Instance)
                        {
                            reachBreak = true;
                            break;
                        }

                        if (cc.IsErrorValue || frame.ReturnStatementWasEvaluated)
                        {
                            // Got the error, or 'return' expression was reached.
                            return cc;
                        }
                    }
                }
                else
                {
                    var cc = CaseClauses[i].Eval(context, env, frame);

                    if (cc.Value == BreakValue.Instance)
                    {
                        reachBreak = true;
                        break;
                    }

                    if (cc.IsErrorValue || frame.ReturnStatementWasEvaluated)
                    {
                        // Got the error, or 'return' expression was reached.
                        return cc;
                    }
                }
            }

            if ((!foundMatch || !reachBreak) && DefaultClause != null)
            {
                var dc = DefaultClause.Eval(context, env, frame);
                {
                    if (dc.Value == BreakValue.Instance)
                    {
                        return EvaluationResult.Undefined;
                    }

                    if (dc.IsErrorValue || frame.ReturnStatementWasEvaluated)
                    {
                        // Got the error, or 'return' expression was reached.
                        return dc;
                    }
                }
            }

            return EvaluationResult.Undefined;
        }

        private bool TryGetCaseIndex(Context context, ModuleLiteral env, EvaluationStackFrame args, EvaluationResult key, out bool hasError, out int caseIndex)
        {
            hasError = false;
            caseIndex = -1;
            var table = Volatile.Read(ref m_caseTable);

            if (table == null)
            {
                table = new Dictionary<EvaluationResult, int>();

                for (int i = 0; i < CaseClauses.Count; ++i)
                {
                    var value = CaseClauses[i].CaseExpression.Eval(context, env, args);

                    if (value.IsErrorValue)
                    {
                        hasError = true;
                        return false;
                    }

                    if (!table.ContainsKey(value))
                    {
                        table.Add(value, i);
                        if (key == value)
                        {
                            caseIndex = i;
                        }
                    }
                }

                Interlocked.CompareExchange(ref m_caseTable, table, null);
            }

            return caseIndex != -1 || table.TryGetValue(key, out caseIndex);
        }
    }
}
