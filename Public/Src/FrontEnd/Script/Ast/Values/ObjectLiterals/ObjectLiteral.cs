// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using TypeScript.Net.Extensions;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Object literal.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710")]
    public abstract class ObjectLiteral : ModuleOrObjectBase
    {
        /// <summary>
        /// Combines two objects by discarding the left object
        /// </summary>
        internal static MergeFunction OverrideFunction = (leftObject, rightObject) => rightObject;

        /// <summary>
        /// Delegate that knows how to combine to objects into a third one
        /// </summary>
        public delegate EvaluationResult MergeFunction(EvaluationResult rightObject, EvaluationResult leftObject);

        /// <summary>
        /// Gets the path where the object literal is defined.
        /// </summary>
        /// <remarks>
        /// This path is only for error reporting.
        /// </remarks>
        public AbsolutePath Path { get; }

        /// <nodoc />
        protected ObjectLiteral(LineInfo location, AbsolutePath path)
            : base(location)
        {
            Path = path;
        }

        /// <summary>
        /// Creates an object literal from an array of bindings.
        /// </summary>
        public static ObjectLiteral Create(params Binding[] bindings)
        {
            return Create(bindings, location: default(LineInfo), path: AbsolutePath.Invalid);
        }

        /// <summary>
        /// Creates an object literal from a list of named values.
        /// </summary>
        internal static ObjectLiteral Create(IReadOnlyList<NamedValue> namedValues, LineInfo location, AbsolutePath path)
        {
            switch (namedValues.Count)
            {
                case 0:
                    return new ObjectLiteral0(location, path);
                case 1:
                    return new ObjectLiteralSlim<StructArray1<NamedValue>>(
                        StructArray.Create(namedValues[0]),
                        location,
                        path);
                case 2:
                    return new ObjectLiteralSlim<StructArray2<NamedValue>>(
                        StructArray.Create(namedValues[0], namedValues[1]),
                        location,
                        path);
                case 3:
                    return new ObjectLiteralSlim<StructArray3<NamedValue>>(
                        StructArray.Create(namedValues[0], namedValues[1], namedValues[2]),
                        location,
                        path);
                case 4:
                    return new ObjectLiteralSlim<StructArray4<NamedValue>>(
                        StructArray.Create(namedValues[0], namedValues[1], namedValues[2], namedValues[3]),
                        location,
                        path);
                case 5:
                    return new ObjectLiteralSlim<StructArray5<NamedValue>>(
                        StructArray.Create(namedValues[0], namedValues[1], namedValues[2], namedValues[3], namedValues[4]),
                        location,
                        path);
                default:
                    return new ObjectLiteralN(namedValues, location, path);
            }
        }

        /// <summary>
        /// Creates an object literal from a list of bindings.
        /// </summary>
        public static ObjectLiteral Create(IReadOnlyList<Binding> bindings, LineInfo location, AbsolutePath path)
        {
            Contract.Requires(bindings != null);

            var namedValues = new List<NamedValue>(bindings.Count);
            foreach (var b in bindings.AsStructEnumerable())
            {
                namedValues.Add(NamedValue.Create(b));
            }

            return Create(namedValues, location, path);
        }

        /// <summary>
        /// Creates an object literal from the stream.
        /// </summary>
        public static ObjectLiteral Create(DeserializationContext context, LineInfo location)
        {
            var reader = context.Reader;

            var path = reader.ReadAbsolutePath();
            var count = reader.ReadInt32Compact();

            var namedValues = new List<NamedValue>(count);
            for (int i = 0; i < count; i++)
            {
                var name = reader.ReadStringId();

                bool isNode = reader.ReadBoolean();
                object body = isNode ? Read(context) : ConstExpressionSerializer.ReadConstValue(reader);
                namedValues.Add(NamedValue.Create(new Binding(name, body, location)));
            }

            return Create(namedValues, location, path);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Path);
            writer.WriteCompact(Count);
            foreach (var member in Members)
            {
                writer.Write(member.Key);

                var node = member.Value.Value as Node;

                bool isNode = node != null;
                writer.Write(isNode);

                if (node != null)
                {
                    node.Serialize(writer);
                }
                else
                {
                    ConstExpressionSerializer.WriteConstValue(writer, member.Value.Value);
                }
            }
        }

        /// <inheritdoc />
        public sealed override EvaluationResult GetOrEvalField(
            Context context,
            SymbolAtom name,
            bool recurs,
            ModuleLiteral origin,
            LineInfo location)
        {
            Contract.Requires(name.IsValid);

            return GetOrEvalField(context, name.StringId, recurs, origin, location);
        }

        /// <summary>
        /// Gets or evaluates field.
        /// </summary>
        /// <returns>
        /// Returns evaluated field or <see cref="UndefinedValue.Instance"/> if member is not found.
        /// Unfortunately there is no way to distinguish between type error or missing member, so in both
        /// cases this method will return the same - undefined.
        /// </returns>
        /// <remarks>
        /// Fields may need to be evaluated. This happens in the case of module fields that have thunked expressions.
        /// </remarks>
        public abstract EvaluationResult GetOrEvalField([NotNull]Context context, StringId name, bool recurs, [NotNull]ModuleLiteral origin, LineInfo location);

        /// <summary>
        /// Evaluates a right-hand side expression if not evaluated yet.
        /// </summary>
        protected internal static EvaluationResult EvalExpression(Context context, ModuleLiteral env, EvaluationResult o, EvaluationStackFrame args)
        {
            var e = o.Value as Expression;
            return e?.Eval(context, env, args) ?? o;
        }

        /// <summary>
        /// Gets the right-hand side of a name.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1043:UseIntegralOrStringArgumentForIndexers")]
        [NotNull]
        public abstract EvaluationResult this[SymbolAtom name] { get; }

        /// <summary>
        /// Gets the right-hand side of a name.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1043:UseIntegralOrStringArgumentForIndexers")]
        [NotNull]
        public abstract EvaluationResult this[StringId name] { get; }

        /// <summary>
        /// Gets the size of object literal.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Gets the members of the object literal.
        /// </summary>
        public abstract IEnumerable<KeyValuePair<StringId, EvaluationResult>> Members { get; }

        /// <summary>
        /// Gets the names of the keys in the value.
        /// </summary>
        public abstract IEnumerable<StringId> Keys { get; }

        /// <summary>
        /// Returns whether this object literal has a given key.
        /// </summary>
        public abstract bool HasKey(StringId key);

        /// <inheritdoc />
        public override bool TryProject(Context context, SymbolAtom name, ModuleLiteral origin, PredefinedTypes predefinedTypes, out EvaluationResult result, LineInfo location)
        {
            var value = this[name.StringId];

            if (value.IsUndefined)
            {
                // TODO: Optimize this for look-up miss cases (there can be tons of them).
                var resolvedMember = predefinedTypes.AmbientObject.ResolveMember(this, name) ?? (object)UndefinedValue.Instance;
                result = EvaluationResult.Create(resolvedMember);
            }
            else
            {
                result = value;
            }

            return true;
        }

        /// <summary>
        /// Overrides this with <paramref name="right"/>.
        /// </summary>
        public virtual EvaluationResult Override(Context context, EvaluationResult right)
        {
            return Combine(context, OverrideFunction, right);
        }

        /// <summary>
        /// Returns the custom merge function if available. Null otherwise.
        /// </summary>
        /// <remarks>
        /// Unfortunately, ArrayLiteral inherits from ObjectLiteral, so there is not
        /// a uniform way to retrieve the custom merge function. Each subclass should
        /// reimplement this if needed
        /// </remarks>
        [CanBeNull]
        protected virtual MergeFunction TryGetCustomMergeFunction(Context context, EvaluationStackFrame captures)
        {
            var customMerge = this[context.Literals.CustomMergeFunction];
            if (!customMerge.IsUndefined)
            {
                return GetCustomMergeFunctionFromClosure(context, captures, customMerge);
            }

            return null;
        }

        /// <summary>
        /// Merges this with <paramref name="right"/>
        /// </summary>
        public virtual EvaluationResult Merge(Context context, EvaluationStackFrame captures, EvaluationResult right)
        {
            var mergeFunction = GetMergeFunction(context, captures, this, right);
            return mergeFunction(EvaluationResult.Create(this), right);
        }

        /// <summary>
        /// Retrieves a custom merge function if available, otherwise the default one
        /// </summary>
        /// <remarks>
        /// Handles the left-wins-over-right behavior when custom functions are specified
        /// </remarks>
        [NotNull]
        protected MergeFunction GetMergeFunction(Context context, EvaluationStackFrame captures, ObjectLiteral leftObject, EvaluationResult rightObject)
        {
            // If the left object has a custom merge, that trumps the other cases
            var customMergeLeft = leftObject.TryGetCustomMergeFunction(context, captures);
            if (customMergeLeft != null)
            {
                return customMergeLeft;
            }

            // Otherwise if the right object has a custom merge, then use that one
            if (rightObject.Value is ObjectLiteral rightObjectLiteral)
            {
                var customMergeRight = rightObjectLiteral.TryGetCustomMergeFunction(context, captures);
                if (customMergeRight != null)
                {
                    return customMergeRight;
                }
            }

            // Otherwise, use the default merge function
            return GetDefaultMergeFunction(context, captures);
        }

        /// <summary>
        /// Helper method that returns a (object, object) -> object from a closure
        /// </summary>
        protected static MergeFunction GetCustomMergeFunctionFromClosure(Context context, EvaluationStackFrame captures, EvaluationResult customMergeClosure)
        {
            var closure = Converter.ExpectClosure(customMergeClosure);
            int paramsCount = closure.Function.Params;

            return (leftObject, rightObject) =>
                   {
                       using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
                       {
                           frame.TrySetArguments(paramsCount, leftObject, rightObject);
                           return context.InvokeClosure(closure, frame);
                       }
                   };
        }

        /// <summary>
        /// Default merge behavior for object literals: recursively go into fields with same key and do merge
        /// </summary>
        /// <remarks>
        /// Subclasses with specific default merge functions should override this method (e.g. <see cref="ArrayLiteral"/>)
        /// </remarks>
        protected virtual MergeFunction GetDefaultMergeFunction(Context context, EvaluationStackFrame captures)
        {
            return (leftObject, rightObject) => Combine(context, GetRecursiveMerge(context, captures), rightObject);
        }

        private static MergeFunction GetRecursiveMerge(Context context, EvaluationStackFrame captures)
        {
            return (leftObject, rightObject) =>
                   {
                       // Merge only operates recursively on object literals.
                       // Otherwise it behaves as override and right object wins
                       if (leftObject.Value is ObjectLiteral leftObjectLiteral)
                       {
                           return leftObjectLiteral.Merge(context, captures, rightObject);
                       }

                       return rightObject;
                   };
        }

        /// <summary>
        /// Combines this and right guided by 'combine' function. This is common logic for both merge and override
        /// </summary>
        internal EvaluationResult Combine(Context context, MergeFunction combine, EvaluationResult right)
        {
            // If the right hand side is undefined, short-circuit and return left.
            if (right.IsUndefined)
            {
                return EvaluationResult.Create(this);
            }

            // If the right hand side is not an object literal (e.g. a number), then the combine just returns it. Same thing if it is an array.
            var rightObject = right.Value as ObjectLiteral;
            if (rightObject == null || rightObject is ArrayLiteral)
            {
                return right;
            }

            // So at this point we know both sides are object literals, and therefore right members should be combined with left members

            // The result of a combine will have a number of members bounded by the summation between right and left members
            // So only when both sides are ObjectLiteralSlim we *could* end up with something that fits into an ObjectLiteralSlim
            // The following strategy puts all left side values into a dictionary first, to avoid the naive n^2 lookup algorithm
            // TODO: Still something better could be worked out when the result fits in an ObjectLiteralSlim (like sorting the keys first) so we avoid going to an intermediate dictionary
            var objectLiteralN = this as ObjectLiteralN;
            var values = objectLiteralN != null ? objectLiteralN.Values.ToDictionary() : Members.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value);

            foreach (var binding in rightObject.Members)
            {
                // If the right member exists on the left, we replace it with whatever 'combine' gives us. Otherwise, we just reflect the right side on the left side.
                if (values.TryGetValue(binding.Key.Value, out var rightMember))
                {
                    values[binding.Key.Value] = combine(rightMember, binding.Value);
                }
                else
                {
                    values[binding.Key.Value] = binding.Value;
                }
            }

            var entry = context.TopStack;
            LineInfo location;
            AbsolutePath path;
            if (entry != null)
            {
                location = entry.InvocationLocation;
                path = entry.Path;
            }
            else
            {
                location = default(LineInfo);
                path = AbsolutePath.Invalid;
            }

            // We don't want to return an ObjectLiteralN unconditionally, to leverage ObjectLiteralSlim memory savings
            // If the result doesn't fit in an ObjectLiteralSlim, we already have a dictionary built for an ObjectLiteralN, so
            // we create it directly to avoid extra allocations
            if (values.Count > 5)
            {
                return EvaluationResult.Create(new ObjectLiteralN(values, location, path));
            }

            // Otherwise, we use the regular Create so it dispatches the creation to the right ObjectLiteralSlim
            return EvaluationResult.Create(Create(values.SelectArray(kvp => new NamedValue(kvp.Key, kvp.Value)).ToList(), location, path));
        }
    }
}
