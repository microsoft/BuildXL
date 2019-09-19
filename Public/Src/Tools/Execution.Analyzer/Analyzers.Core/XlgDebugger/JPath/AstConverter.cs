// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace BuildXL.Execution.Analyzer.JPath
{
    /// <summary>
    /// Used for errors created by the <see cref="AstConverter"/>.
    /// </summary>
    public class AstException : Exception
    {
        public AstException(string message) : base(message) { }
    }

    /// <summary>
    /// Converts from CST to AST (<see cref="Expr"/>).
    ///
    /// Throws <see cref="AstException"/> in case of an error
    /// (e.g., when a regular expression literal is an invalid regular expression)
    /// </summary>
    public class AstConverter : JPathBaseVisitor<Expr>
    {
        private AstException AstError(IToken token, string message)
        {
            return new AstException($"({token.Line}:{token.Column}) :: {message}");
        }

        public override Expr VisitFilterExpr([JetBrains.Annotations.NotNull] JPathParser.FilterExprContext context)
        {
            return new FilterExpr(lhs: context.Lhs.Accept(this), filter: context.Filter.Accept(this));
        }

        public override Expr VisitIntLitExpr([JetBrains.Annotations.NotNull] JPathParser.IntLitExprContext context)
        {
            // the grammar ensures that this is a valid integer literal so int.Parse won't fail
            return new IntLit(int.Parse(context.Value.Text));
        }

        public override Expr VisitMapExpr([JetBrains.Annotations.NotNull] JPathParser.MapExprContext context)
        {
            return new MapExpr(context.Lhs.Accept(this), (Selector)context.Selector.Accept(this));
        }

        public override Expr VisitRangeExpr([JetBrains.Annotations.NotNull] JPathParser.RangeExprContext context)
        {
            return new RangeExpr(
                array: context.Lhs.Accept(this),
                begin: context.Begin.Accept(this),
                end: context.End.Accept(this));
        }

        public override Expr VisitIndexExpr([JetBrains.Annotations.NotNull] JPathParser.IndexExprContext context)
        {
            return new RangeExpr(
                array: context.Lhs.Accept(this),
                begin: context.Index.Accept(this),
                end: null);
        }

        public override Expr VisitRegExLitExpr([JetBrains.Annotations.NotNull] JPathParser.RegExLitExprContext context)
        {
            try
            {
                var regex = new Regex(context.Value.Text.Trim('/', '!'));
                return new RegexLit(regex);
            }
            catch (ArgumentException e)
            {
                throw AstError(context.Value, $"Value '{context.Value}' is not a valid regex: {e.Message}");
            }
        }

        public override Expr VisitRootExpr([JetBrains.Annotations.NotNull] JPathParser.RootExprContext context)
        {
            return RootExpr.Instance;
        }

        public override Expr VisitSelectorExpr([JetBrains.Annotations.NotNull] JPathParser.SelectorExprContext context)
        {
            return context.Sub.Accept(this);
        }

        public override Expr VisitPropertyId([JetBrains.Annotations.NotNull] JPathParser.PropertyIdContext context)
        {
            return new Selector(context.PropertyName.Text);
        }

        public override Expr VisitEscId([JetBrains.Annotations.NotNull] JPathParser.EscIdContext context)
        {
            return new Selector(context.PropertyName.Text.Trim('`'));
        }

        public override Expr VisitVarExpr([JetBrains.Annotations.NotNull] JPathParser.VarExprContext context)
        {
            return new VarExpr(context.Var.Text);
        }

        public override Expr VisitIdSelector([JetBrains.Annotations.NotNull] JPathParser.IdSelectorContext context)
        {
            return context.Name.Accept(this);
        }

        public override Expr VisitUnionSelector([JetBrains.Annotations.NotNull] JPathParser.UnionSelectorContext context)
        {
            return new Selector(context._Names
                .Select(n => (n.Accept(this) as Selector).PropertyNames.First())
                .ToArray());
        }

        public override Expr VisitStrLitExpr([JetBrains.Annotations.NotNull] JPathParser.StrLitExprContext context)
        {
            return new StrLit(context.Value.Text.Trim('"', '\''));
        }

        public override Expr VisitSubExpr([JetBrains.Annotations.NotNull] JPathParser.SubExprContext context)
        {
            return context.Sub.Accept(this);
        }

        public override Expr VisitExprIntExpr([JetBrains.Annotations.NotNull] JPathParser.ExprIntExprContext context)
        {
            return context.Expr.Accept(this);
        }

        public override Expr VisitUnaryIntExpr([JetBrains.Annotations.NotNull] JPathParser.UnaryIntExprContext context)
        {
            return new UnaryExpr(context.Op.Token, context.Sub.Accept(this));
        }

        public override Expr VisitBinaryIntExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryIntExprContext context)
        {
            return new BinaryExpr(context.Op.Token, context.Lhs.Accept(this), context.Rhs.Accept(this));
        }

        public override Expr VisitSubIntExpr([JetBrains.Annotations.NotNull] JPathParser.SubIntExprContext context)
        {
            return context.Sub.Accept(this);
        }

        public override Expr VisitBinaryBoolExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryBoolExprContext context)
        {
            return new BinaryExpr(context.Op.Token, context.Lhs.Accept(this), context.Rhs.Accept(this));
        }

        public override Expr VisitSubBoolExpr([JetBrains.Annotations.NotNull] JPathParser.SubBoolExprContext context)
        {
            return context.Sub.Accept(this);
        }

        public override Expr VisitBoolLogicExpr([JetBrains.Annotations.NotNull] JPathParser.BoolLogicExprContext context)
        {
            return context.Expr.Accept(this);
        }

        public override Expr VisitBinaryLogicExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryLogicExprContext context)
        {
            return new BinaryExpr(context.Op.Token, context.Lhs.Accept(this), context.Rhs.Accept(this));
        }

        public override Expr VisitUnaryLogicExpr([JetBrains.Annotations.NotNull] JPathParser.UnaryLogicExprContext context)
        {
            return new UnaryExpr(context.Op.Token, context.Sub.Accept(this));
        }

        public override Expr VisitSubLogicExpr([JetBrains.Annotations.NotNull] JPathParser.SubLogicExprContext context)
        {
            return context.Sub.Accept(this);
        }

        public override Expr VisitCardinalityExpr([JetBrains.Annotations.NotNull] JPathParser.CardinalityExprContext context)
        {
            return new CardinalityExpr(context.Sub.Accept(this));
        }

        public override Expr VisitFuncAppExprParen([JetBrains.Annotations.NotNull] JPathParser.FuncAppExprParenContext context)
        {
            return new FuncAppExpr(
                func: context.Func.Accept(this),
                args: context._Args.Select(arg => arg.Accept(this)).ToArray());
        }

        public override Expr VisitFuncOptExpr([JetBrains.Annotations.NotNull] JPathParser.FuncOptExprContext context)
        {
            return new FuncAppExpr(
                func: context.Func.Accept(this),
                opts: new FuncOpt(
                    name: context.OptName.Text,
                    value: context.OptValue?.Accept(this)));
        }

        public override Expr VisitPipeExpr([JetBrains.Annotations.NotNull] JPathParser.PipeExprContext context)
        {
            return new FuncAppExpr(
                func: context.Func.Accept(this),
                args: new[] { context.Input.Accept(this) });
        }

        public override Expr VisitBinExpr([JetBrains.Annotations.NotNull] JPathParser.BinExprContext context)
        {
            return new BinaryExpr(
                op: (context.Op.GetChild(0).GetChild(0) as ITerminalNode).Symbol,
                lhs: context.Lhs.Accept(this),
                rhs: context.Rhs.Accept(this));
        }

        public override Expr VisitLetExpr([JetBrains.Annotations.NotNull] JPathParser.LetExprContext context)
        {
            return new LetExpr(
                name: context.Var.Text,
                value: context.Val.Accept(this),
                sub: context.Sub?.Accept(this));
        }

        public override Expr VisitAssignExpr([JetBrains.Annotations.NotNull] JPathParser.AssignExprContext context)
        {
            return new LetExpr(
                name: context.Var.Text,
                value: context.Val.Accept(this),
                sub: null);
        }
    }
}