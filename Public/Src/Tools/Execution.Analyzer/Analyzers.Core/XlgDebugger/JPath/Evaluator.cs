// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Execution.Analyzer.JPath
{
    /// <summary>
    /// Evaluates an expression of type <see cref="Expr"/> and produces a result of type <see cref="Result"/>.
    /// 
    /// Every expression evaluates to a collection of values (hence <see cref="Result.Value"/> is of type List)
    /// </summary>
    public sealed class Evaluator
    {
        /// <summary>
        /// The result of an evaluation.
        /// 
        /// Every expression evaluates to a vector, which is accessible via the <see cref="Value"/> property.
        /// </summary>
        public sealed class Result : IEnumerable<object>, IEquatable<Result>
        {
            /// <summary>
            /// Empty result
            /// </summary>
            public static readonly Result Empty = new Result(new object(), new object[0]);

            private readonly Lazy<IReadOnlyList<object>> m_value;

            private readonly object m_identity;

            /// <summary>
            /// The result of evaluation.  Every expression evaluates to a vector, hence the type.
            /// </summary>
            public IReadOnlyList<object> Value => m_value.Value;

            /// <summary>
            /// Number of values in this vector (<see cref="Value"/>)
            /// </summary>
            public int Count => Value.Count;

            /// <summary>
            /// Whether this is a scalar result (the vector contains exactly one element)
            /// </summary>
            public bool IsScalar => Count == 1;

            /// <summary>
            /// Whether this vector is empty.
            /// </summary>
            public bool IsEmpty => Count == 0;

            private Result(object identity, IEnumerable<object> arr)
            {
                Contract.Requires(identity != null);
                Contract.Requires(arr != null);

                m_identity = identity;
                m_value = new Lazy<IReadOnlyList<object>>(() => arr.ToList());
            }

            /// <summary>Factory method from a scalar.</summary>
            public static Result Scalar(object scalar) => new Result(scalar, new[] { scalar });

            /// <summary>Factory method from a vector.</summary>
            public static Result Array(IEnumerable<object> arr) => new Result(arr, arr);

            // IEnumerable methods

            public IEnumerator<object> GetEnumerator() => Value.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => Value.GetEnumerator();

            // hash code / equality 

            public bool Equals(Result other)
            {
                return other != null && m_identity.Equals(other.m_identity);
            }

            public override bool Equals(object obj)
            {
                return obj is Result res && Equals(res);
            }

            public override int GetHashCode()
            {
                return m_identity.GetHashCode();
            }

            // implicit conversions

            public static implicit operator Result(int scalar)       => Scalar(scalar);
            public static implicit operator Result(bool scalar)      => Scalar(scalar);
            public static implicit operator Result(string scalar)    => Scalar(scalar);
            public static implicit operator Result(Regex scalar)     => Scalar(scalar);
            public static implicit operator Result(Function scalar)  => Scalar(scalar);
            public static implicit operator Result(ObjectInfo obj)   => Scalar(obj);
            public static implicit operator Result(object[] arr)     => Array(arr);
            public static implicit operator Result(List<object> arr) => Array(arr);
        }

        /// <summary>
        /// An environment against which expressions are evaluated
        /// </summary>
        public class Env
        {
            private readonly IDictionary<string, Result> m_vars;

            /// <summary>
            /// Parent environment.
            /// </summary>
            public Env Parent { get; }

            /// <summary>
            /// The result of the last evaluated expression (against which properties are resolved)
            /// </summary>
            public Result Current { get; }

            /// <summary>
            /// A caller-provided object resolver.
            /// 
            /// Takes an object and returns an <see cref="ObjectInfo"/> for it
            /// (i.e., a name and a list of its properties).
            /// </summary>
            public ObjectResolver Resolver { get; }

            /// <nodoc />
            public Env(Env parent, Result current, ObjectResolver resolver, IDictionary<string, Result> vars = null)
            {
                Contract.Requires(resolver != null);
                Contract.Requires(current != null);
                Contract.Requires(vars == null || vars.Keys.All(name => name.StartsWith("$")));

                Parent = parent;
                Current = current;
                Resolver = resolver;

                m_vars = new Dictionary<string, Result>();
                if (vars != null)
                {
                    m_vars.AddRange(vars);
                }
            }

            /// <nodoc />
            public Env(ObjectResolver objectResolver, object root)
                : this(null, Result.Scalar(root), objectResolver) { }

            /// <summary>Returns all vars defined in this environment only</summary>
            public IEnumerable<KeyValuePair<string, Result>> Vars => m_vars;

            /// <summary>
            /// Returns any <see cref="Result"/> bound to <paramref name="varName"/>.
            /// If this environment does not contain any binding for <paramref name="varName"/>,
            /// the lookup continues in the <see cref="Parent"/> environment.  When no parent
            /// environment exist, returns null.
            /// </summary>
            public Result GetVar(string varName)
            {
                return m_vars.TryGetValue(varName, out var result)
                    ? result
                    : Parent?.GetVar(varName);
            }

            /// <summary>
            /// MUTATES the current environment by adding  variable binding.
            /// </summary>
            public Result SetVar(string varName, Result value)
            {
                m_vars[varName] = value;
                return value;
            }

            /// <summary>
            /// The root object (<see cref="RootExpr"/>).
            /// </summary>
            public Result Root => Parent != null ? Parent.Root : Current;

            /// <summary>
            /// Returns a new child environment in which only the <see cref="Current"/> property is updated to <paramref name="newCurrent"/>.
            /// </summary>
            internal Env WithCurrent(Result newCurrent)
            {
                return new Env(parent: this, newCurrent, Resolver);
            }

            /// <summary>
            /// Returns a new child environment in which <paramref name="vars"/> are set.
            /// </summary>
            internal Env WithVars(params (string Name, Result Value)[] vars)
            {
                return new Env(parent: this, Current, Resolver, vars.ToDictionary(t => t.Name, t => t.Value));
            }

            /// <inheritdoc />
            public string GetContentHash()
            {
                using (var sbWrapper = Pools.StringBuilderPool.GetInstance())
                {
                    var sb = sbWrapper.Instance;
                    sb.Append("Current:").Append(Current.GetHashCode());
                    foreach (var v in m_vars)
                    {
                        sb.Append(",var:").Append(v.Key).Append(":").Append(v.Value.GetHashCode());
                    }
                    if (Parent != null)
                    {
                        sb.Append(",parent:").Append(Parent.GetHashCode());
                    }
                    return sb.ToString();
                }
            }
        }

        /// <summary>
        /// An argument to be passed when applying a function.
        /// 
        /// Either <see cref="Name"/> or <see cref="Value"/> may be null.
        /// </summary>
        public class Arg
        {
            /// <summary>Name of the argument.  May be null, in which case <see cref="Value"/> must be non-null</summary>
            [CanBeNull] public string Name { get; }

            /// <summary>Value of the argument.  Maybe be null, in which case <see cref="Name"/> must be non-null</summary>
            [CanBeNull] public Result Value { get; }

            /// <nodoc />
            public bool HasName => Name != null;

            /// <nodoc />
            public bool HasValue => Value != null;

            /// <nodoc />
            public Arg(string name, Result value)
            {
                Contract.Requires(name == null || name.Trim().Length > 0);
                Contract.Requires(name != null || value != null);
                Name = name;
                Value = value;
            }
        }

        /// <summary>
        /// Arguments that are passed to library-defined functions (returned by <see cref="Env.ResolveFunc"/>)
        /// </summary>
        public class Args : IEnumerable<Result>
        {
            /// <summary>Reference to the current evaluator</summary>
            public Evaluator Eval { get; }

            /// <summary>Name of the function to which these arguments are passed</summary>
            private readonly string m_funcName;

            /// <summary>The arguments passed to the function</summary>
            private readonly Arg[] m_allArgs;

            private readonly Arg[] m_opts;
            private readonly Result[] m_values;

            /// <summary>Number of free args</summary>
            public int ArgCount => m_values.Length;

            /// <summary>Number of named options</summary>
            public int OptCount => m_opts.Length;

            /// <nodoc />
            public Args(Evaluator eval, string funcName, Arg[] args)
            {
                Eval = eval;
                m_funcName = funcName;
                m_allArgs = args;

                m_values = args.Where(a => !a.HasName).Select(a => a.Value).ToArray();
                m_opts = args
                    .Where(a => a.HasName)
                    .SelectMany(a =>
                    {
                        // option starts with "--" --> it's a single option
                        if (a.Name.StartsWith("--"))
                        {
                            return new[] { new Arg(a.Name.Substring(2), a.Value) };
                        }
                        
                        // option starts with a single "-" --> each char is an option and the value is associated with the last char
                        Contract.Assert(a.Name.StartsWith("-"));
                        var chars = a.Name.Substring(1).ToCharArray();
                        return chars.Select((c, idx) => new Arg(name: $"{c}", value: idx == chars.Length - 1 ? a.Value : null));
                    })
                    .ToArray();
            }

            /// <summary>Whether a given switch is present</summary>
            public bool HasSwitch(string switchName) => m_opts.Any(opt => opt.Name == switchName);

            /// <summary>Switch value or null if no switch is present</summary>
            public Result GetSwitch(string switchName) => m_opts.FirstOrDefault(opt => opt.Name == switchName)?.Value;

            /// <summary>
            /// Returns all objects from all args
            /// </summary>
            public IEnumerable<object> Flatten()
            {
                return m_values.SelectMany(result => result);
            }

            /// <summary>
            /// Returns the argument at position <paramref name="i"/> or throws if that index is out of bounds
            /// </summary>
            public Result this[int i]
            {
                get
                {
                    if (i < 0)
                    {
                        i = i + ArgCount;
                    }

                    if (i < 0 || i >= ArgCount)
                    {
                        throw Eval.Error($"Function '{m_funcName}' received {m_values.Length} arguments but needs at least {i+1}");
                    }

                    return m_values[i];
                }
            }

            #region IEnumerable implementation

            public IEnumerator<Result> GetEnumerator()
            {
                return ((IEnumerable<Result>)m_values).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<Result>)m_values).GetEnumerator();
            }

            #endregion

            #region Helper methods delegating to Eval

            public int ToInt(object obj) => Eval.ToInt(obj);
            public bool ToBool(object obj) => Eval.ToBool(obj);
            public string ToString(object obj) => Eval.ToString(obj);
            public object ToScalar(Result res) => Eval.ToScalar(res);
            public string Preview(object obj) => Eval.PreviewObj(obj);
            public bool Matches(string str, Result pattern) => Eval.Matches(str, pattern);

            #endregion
        }

        /// <summary>
        /// Library-defined function delegate type.
        /// </summary>
        public delegate Result LibraryFunc(Args args);

        /// <summary>
        /// A function value.
        /// </summary>
        public sealed class Function
        {
            private readonly LibraryFunc m_func;
            private readonly Arg[] m_curriedArgs;

            /// <nodoc />
            public string Name { get; }

            /// <nodoc />
            public int MinArity { get; }

            /// <nodoc />
            public int? MaxArity { get; }

            /// <nodoc />
            public Function(LibraryFunc func, string name = "<lambda>", int minArity = 1, int? maxArity = null, IEnumerable<Arg> curriedArgs = null)
            {
                Contract.Requires(func != null);

                m_func = func;
                Name = name;
                MinArity = minArity;
                MaxArity = maxArity;
                m_curriedArgs = (curriedArgs ?? CollectionUtilities.EmptyArray<Arg>()).ToArray();
            }

            /// <nodoc />
            public Result Apply(Evaluator eval, IEnumerable<Arg> args) => Apply(eval, args.ToArray());

            /// <nodoc />
            public Result Apply(Evaluator eval, Arg[] args)
            {
                var argCount = args.Where(a => !a.HasName).Count();
                if (argCount < MinArity)
                {
                    return new Function(
                        m_func, 
                        Name,
                        minArity: MinArity - argCount,
                        maxArity: MaxArity != null ? MaxArity.Value - argCount : MaxArity,
                        curriedArgs: m_curriedArgs.Concat(args));
                }

                return m_func(new Args(eval, Name, m_curriedArgs.Concat(args).ToArray()));
            }
        }

        /// <summary>
        /// Object resolver delegate type.
        /// 
        /// GroupedBy.Pips.ByType.Process[Outputs.Files.Path = $.Files[RewriteCount > 0][0].Path]
        /// </summary>
        public delegate ObjectInfo ObjectResolver(object obj);

        private readonly Stack<(Env, Expr)> m_evalStack = new Stack<(Env, Expr)>();
        private readonly Stack<Env> m_envStack = new Stack<Env>();
        private readonly Dictionary<string, Result> m_evalCache = new Dictionary<string, Result>();

        public Env TopEnv => m_envStack.Peek();

        public Evaluator(Env rootEnv)
        {
            Contract.Requires(rootEnv != null);
            m_envStack.Push(rootEnv);
        }

        /// <nodoc />
        public Result Eval(Expr expr)
        {
            var key = $"{expr.Print()}|{TopEnv.GetContentHash()}";
            if (m_evalCache.TryGetValue(key, out var result))
            {
                Console.WriteLine("============== cached: " + key);
                return result;
            }

            return m_evalCache[key] = EvalInternal(expr);
        }

        private Result EvalInternal(Expr expr)
        {
            m_evalStack.Push((TopEnv, expr));
            try
            {
                switch (expr)
                {
                    case VarExpr varExpr:
                        return TopEnv.GetVar(varExpr.Name) ?? Result.Empty;

                    case Selector selector:
                        return TopEnv.Current
                            .Select(obj => TopEnv.Resolver(obj))
                            .SelectMany(obj => obj.Properties.Where(p => selector.PropertyNames.Contains(p.Name)))
                            .SelectMany(prop =>
                            {
                                // automatically flatten non-scalar results
                                switch (prop.Value)
                                {
                                    case IEnumerable<object> ie: return ie;
                                    case string str:             return new[] { str }; // string is IEnumerable, so exclude it here
                                    case IEnumerable ie2:        return ie2.Cast<object>();
                                    default:
                                        return new[] { prop.Value };
                                }
                            })
                            .ToList();

                    case RangeExpr rangeExpr:
                        if (rangeExpr.Array != null)
                        {
                            return InNewEnv(rangeExpr.Array, new RangeExpr(null, rangeExpr.Begin, rangeExpr.End));
                        }

                        var array = TopEnv.Current;
                        if (array.IsEmpty)
                        {
                            return array;
                        }

                        var begin = IEval(rangeExpr.Begin);
                        var end = rangeExpr.End != null
                            ? IEval(rangeExpr.End)
                            : begin;

                        if (begin < 0)
                        {
                            begin += array.Count;
                        }

                        if (end < 0)
                        {
                            end += array.Count;
                        }

                        if (begin > end || begin < 0 || begin >= array.Count || end < 0 || end >= array.Count)
                        {
                            return Result.Empty;
                        }

                        return array.ToList().GetRange(index: begin, count: end - begin + 1);

                    case FilterExpr filterExpr:
                        if (filterExpr.Lhs != null)
                        {
                            return InNewEnv(filterExpr.Lhs, new FilterExpr(null, filterExpr.Filter));
                        }

                        return TopEnv
                            .Current
                            .Where(obj => ToBool(InNewEnv(Result.Scalar(obj), filterExpr.Filter)))
                            .ToList();

                    case MapExpr mapExpr:
                        return InNewEnv(mapExpr.Lhs, mapExpr.PropertySelector);

                    case FuncAppExpr funcExpr:
                        var funcResult = Eval(funcExpr.Func);
                        var function = ToFunc(funcResult, funcExpr.Func);
                        var args = funcExpr
                            .Opts
                            .Select(opt => new Arg(
                                name: opt.Name,
                                value: opt.Value != null ? Eval(opt.Value) : null));
                        return function.Apply(this, args);

                    case LetExpr letExpr:
                        var name = letExpr.Name;
                        var value = Eval(letExpr.Value);
                        return letExpr.Sub != null
                            ? InNewEnv(TopEnv.WithVars((name, value)), letExpr.Sub)
                            : TopEnv.SetVar(name, value);

                    case CardinalityExpr cardExpr:
                        return Eval(cardExpr.Sub).Count;

                    case RootExpr rootExpr:
                        return TopEnv.Root;

                    case IntLit intLit:
                        return intLit.Value;

                    case StrLit strLit:
                        return strLit.Value;

                    case RegexLit regexLit:
                        return regexLit.Value;

                    case UnaryExpr ue:
                        return EvalUnaryExpr(ue);

                    case BinaryExpr be:
                        return EvalBinaryExpr(be);

                    default:
                        throw new Exception("Evaluation not implemented for type: " + expr?.GetType().FullName);
                }
            }
            finally
            {
                m_evalStack.Pop();
            }
        }

        private Result InNewEnv(Expr newCurrent, Expr inNewEnv) => InNewEnv(Eval(newCurrent), inNewEnv);

        private Result InNewEnv(Result newCurrent, Expr inNewEnv) => InNewEnv(TopEnv.WithCurrent(newCurrent), inNewEnv);

        private Result InNewEnv(Env newEnv, Expr inNewEnv)
        {
            m_envStack.Push(newEnv);
            try
            {
                return Eval(inNewEnv);
            }
            finally
            {
                m_envStack.Pop();
            }
        }

        private int IEval(Expr expr) => ToInt(Eval(expr), source: expr);
        private bool BEval(Expr expr) => ToBool(Eval(expr), source: expr);

        private Result EvalUnaryExpr(UnaryExpr expr)
        {
            var result = Eval(expr);
            switch (expr.Op.Type)
            {
                case JPathLexer.NOT:   return !ToBool(result, expr);
                case JPathLexer.MINUS: return -ToInt(result, expr);

                default:
                    throw ApplyError(expr.Op);
            }
        }

        private Result EvalBinaryExpr(BinaryExpr expr)
        {
            var lhs = expr.Lhs;
            var rhs = expr.Rhs;
            switch (expr.Op.Type)
            {
                case JPathLexer.GTE: return IEval(lhs) >= IEval(rhs);
                case JPathLexer.GT:  return IEval(lhs) >  IEval(rhs);
                case JPathLexer.LTE: return IEval(lhs) <= IEval(rhs);
                case JPathLexer.LT:  return IEval(lhs) <  IEval(rhs);
                case JPathLexer.EQ:  return Eval(lhs).Equals(Eval(rhs));
                case JPathLexer.NEQ: return !Eval(lhs).Equals(Eval(rhs));

                case JPathLexer.AND: return BEval(lhs) && BEval(rhs);
                case JPathLexer.OR:  return BEval(lhs) || BEval(rhs);
                case JPathLexer.XOR: return BEval(lhs) != BEval(rhs);
                case JPathLexer.IFF: return BEval(lhs) == BEval(rhs);

                case JPathLexer.PLUS:  return IEval(lhs) + IEval(rhs);
                case JPathLexer.MINUS: return IEval(lhs) - IEval(rhs);
                case JPathLexer.TIMES: return IEval(lhs) * IEval(rhs);
                case JPathLexer.DIV:   return IEval(lhs) / IEval(rhs);
                case JPathLexer.MOD:   return IEval(lhs) % IEval(rhs);

                case JPathLexer.MATCH:  return Matches(Eval(lhs), Eval(rhs));
                case JPathLexer.NMATCH: return !Matches(Eval(lhs), Eval(rhs));

                default:
                    throw ApplyError(expr.Op);
            }
        }

        /// <summary>
        /// Whether the value in <paramref name="lhs"/> matches the value in <paramref name="rhs"/>.
        /// </summary>
        /// <param name="lhs">Can be any value.</param>
        /// <param name="rhs">Must be a scalar string or regular expression</param>
        /// <returns></returns>
        public bool Matches(Result lhs, Result rhs)
        {
            return lhs.Any(obj => Matches(PreviewObj(obj), rhs));
        }

        /// <summary>
        /// Whether the value in <paramref name="lhs"/> matches the value in <paramref name="rhs"/>.
        /// </summary>
        /// <param name="lhsStr">String to check</param>
        /// <param name="rhs">Must be a scalar string or regular expression</param>
        public bool Matches(string lhsStr, Result rhs)
        {
            var rhsVal = ToScalar(rhs);
            switch (rhsVal)
            {
                case string str: return lhsStr.Contains(str);
                case Regex regex: return regex.Match(lhsStr).Success;
                default:
                    throw TypeError(rhsVal, "string | Regex");
            }
        }

        /// <summary>
        /// Uses the current environment to resolve <paramref name="obj"/> and returns its "preview" string.
        /// 
        /// Every object can be resolved to something, so this function never fails.
        /// </summary>
        public string PreviewObj(object obj)
        {
            var env = TopEnv;
            if (env == null)
            {
                return obj?.ToString() ?? "<null>";
            }

            var objInfo = env.Resolver(obj);
            return objInfo.Preview;
        }

        /// <summary>
        /// Returns the single value if <paramref name="value"/> is scalar; otherwise throws.
        /// </summary>
        public object ToScalar(Result value)
        {
            if (!value.IsScalar)
            {
                throw Error("Expected a scalar value, got a vector of size " + value.Count);
            }

            return value.Value.First();
        }

        /// <summary>
        /// Converts <paramref name="obj"/> to int if possible; otherwise throws.
        /// 
        /// A <see cref="Result"/> can be converted only if it is a scalar value.
        /// Other than that, only numeric values can be converted to int.
        /// </summary>
        public int ToInt(object obj, Expr source = null)
        {
            switch (obj)
            {
                case Result r when r.IsScalar: return ToInt(ToScalar(r), source);
                case int i:                    return i;
                case long l:                   return (int)l;
                case byte b:                   return b;
                case short s:                  return s;
                default:
                    throw TypeError(obj, "int", source);
            }
        }

        /// <summary>
        /// Converts <paramref name="obj"/> to <see cref="Function"/> if possible; otherwise throws.
        /// 
        /// A <see cref="Result"/> can be converted only if it is a scalar value.
        /// Other than that, only a <see cref="Function"/> object can be converted to string.
        /// </summary>
        public Function ToFunc(object obj, Expr source = null)
        {
            switch (obj)
            {
                case Result r when r.IsScalar: return ToFunc(ToScalar(r), source);
                case Function f:               return f;
                default:
                    throw TypeError(obj, "function", source);
            }
        }

        /// <summary>
        /// Converts <paramref name="obj"/> to string if possible; otherwise throws.
        /// 
        /// A <see cref="Result"/> can be converted only if it is a scalar value.
        /// Other than that, only a string can be converted to string.
        /// </summary>
        public string ToString(object obj, Expr source = null)
        {
            switch (obj)
            {
                case Result r when r.IsScalar: return ToString(ToScalar(r), source);
                case string s:                 return s;
                default:
                    throw TypeError(obj, "string", source);
            }
        }

        /// <summary>
        /// Converts <paramref name="obj"/> to bool if possible; otherwise throws.
        /// 
        /// A <see cref="Result"/> can be converted only if it is a scalar value.
        /// Other than that, the following values are converted to true:
        ///   - an integer that is different from 0
        ///   - a non-empty string
        ///   - a non-empty collection
        ///   - the <code>true</code> boolean constant
        /// </summary>
        public bool ToBool(object obj, Expr source = null)
        {
            if (obj == null)
            {
                return false;
            }

            switch (obj)
            {
                case Result r when r.IsScalar: return ToBool(ToScalar(r), source);
                case IEnumerable<object> arr:  return arr.Any();
                case string str:               return !string.IsNullOrEmpty(str);
                case bool b:                   return b;
                case int i:                    return i != 0;
                default:
                    throw TypeError(obj, "bool", source);
            }
        }

        private string PreviewArray(Result result)
        {
            return "[" + Environment.NewLine +
                    string.Join(string.Empty, result.Select(PreviewObj).Select(str => Environment.NewLine + "  " + str)) +
                    Environment.NewLine + "]";
        }

        private Exception ApplyError(IToken op)
        {
            return Error($"Cannot apply operator '{op.Text}' (token: {op})");
        }

        private Exception TypeError(object obj, string expectedType, Expr source = null)
        {
            var objPreview = PreviewObj(obj);
            var message = $"Cannot convert '{objPreview}' of type {obj?.GetType().Name} to {expectedType}.";
            if (source != null)
            {
                message += $"  This value was obtained by evaluating expression: {source.Print()}";
            }

            return Error(message);
        }

        private Exception Error(string message)
        {
            return new Exception(message);
        }
    }
}
