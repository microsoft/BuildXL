// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BuildXL.Utilities;

[assembly: CLSCompliant(false)]

namespace BuildXL.Execution.Analyzer.JPath
{ 
    /// <summary>
    /// Static helper methods for parsing/evaluating JPath expressions
    /// </summary>
    public static class JPath
    {
        class JPathListener : JPathBaseListener, IAntlrErrorListener<IToken>
        {
            /// <nodoc />
            public string FirstError { get; private set; }

            /// <nodoc />
            public bool HasErrors => FirstError != null;

            /// <nodoc />
            public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
            {
                if (FirstError == null)
                {
                    FirstError = msg;
                }
            }

            private readonly Stack<ParserRuleContext> m_ruleStack = new Stack<ParserRuleContext>();

            public override void EnterEveryRule([NotNull] ParserRuleContext context)
            {
                m_ruleStack.Push(context);
            }

            public override void ExitEveryRule([NotNull] ParserRuleContext context)
            {
                m_ruleStack.Pop();
            }
        }

        /// <nodoc />
        public static Possible<Expr> TryParse(string str)
        {
            return TryParseInternal(str).Then(TryConvertInternal);
        }

        /// <nodoc />
        public static Possible<Evaluator.Result> TryEval(Evaluator evaluator, Expr expr)
        {
            Contract.Requires(evaluator != null);
            Contract.Requires(expr != null);

            try
            {
                return evaluator.Eval(expr);
            }
            catch (Exception e)
            {
                return new Failure<string>(e.ToString());
            }
        }

        /// <nodoc />
        public static Possible<Evaluator.Result> TryEval(Evaluator evaluator, string expr)
        {
            return TryParse(expr).Then(e => TryEval(evaluator, e));
        }

        internal static Possible<string[]> GetCompletions(Evaluator evaluator, string str)
        {
            var lexer = new JPathLexer(new AntlrInputStream(str));
            var parser = new JPathParser(new CommonTokenStream(lexer));
            var listener = new JPathListener();
            parser.AddErrorListener(listener);
            var exprCst = parser.expr();

            if (listener.HasErrors)
            {
                return new Failure<string>("Syntex error: " + listener.FirstError);
            }

            var exprAst = exprCst.Accept(new AstConverter());
            var value = evaluator.Eval(exprAst);

            return new[] { "hi", exprAst.Print(), value.Value.ToString() };
        }

        private static Possible<JPathParser.ExprContext> TryParseInternal(string str)
        {
            var lexer = new JPathLexer(new AntlrInputStream(str));
            var tokenStream = new CommonTokenStream(lexer);
            var parser = new JPathParser(tokenStream);
            var listener = new JPathListener();
            parser.AddErrorListener(listener);
            var expr = parser.expr();

            if (listener.HasErrors)
            {
                return new Failure<string>("Syntex error: " + listener.FirstError);
            }
            else if (tokenStream.Index < tokenStream.Size - 1)
            {
                var tokens = string.Join("", Enumerable
                    .Range(tokenStream.Index, count: tokenStream.Size - tokenStream.Index - 1)
                    .Select(idx => tokenStream.Get(idx).Text));
                return new Failure<string>($"Unconsumed tokens: {tokens}");
            }
            else
            {
                return expr;
            }
        }

        private static Possible<Expr> TryConvertInternal(JPathParser.ExprContext expr)
        {
            try
            {
                return expr.Accept(new AstConverter());
            }
            catch (Exception e)
            {
                return new Failure<string>(e.ToString());
            }
        }
    }

}
