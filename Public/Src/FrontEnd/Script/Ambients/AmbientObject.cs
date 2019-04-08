// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for type Object.
    /// </summary>
    public sealed class AmbientObject : AmbientDefinition<ObjectLiteral>
    {
        /// <nodoc />
        public AmbientObject(PrimitiveTypes knownTypes)
            : base("Object", knownTypes)
        {
        }

        private CallSignature StaticMergeSignature => CreateSignature(
            restParameterType: AmbientTypes.ObjectType,
            returnType: AmbientTypes.ObjectType);

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<ObjectLiteral>> CreateMembers()
        {
            return (new[]
            {
                Create(AmbientName, Symbol(Constants.Names.OverrideFunction), (CallableMemberSignature1<ObjectLiteral>)Override),
                Create(AmbientName, Symbol(Constants.Names.OverrideKeyFunction), (CallableMemberSignature2<ObjectLiteral>)OverrideKey),
                Create(AmbientName, Symbol(Constants.Names.MergeFunction), (CallableMemberSignature1<ObjectLiteral>)Merge),
                Create(AmbientName, Symbol("keys"), (CallableMemberSignature0<ObjectLiteral>)Keys),
                Create(AmbientName, Symbol("get"), (CallableMemberSignature1<ObjectLiteral>)Get),
                Create(AmbientName, Symbol("withCustomMerge"), (CallableMemberSignature1<ObjectLiteral>)WithCustomMerge),
            }).ToDictionary((CallableMember<ObjectLiteral> m) => m.Name.StringId, (CallableMember<ObjectLiteral> m) => m);
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Object",
                new[]
                {
                    Function(Constants.Names.MergeFunction, StaticMerge, StaticMergeSignature),
                });
        }

        private static EvaluationResult StaticMerge(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            ObjectLiteral result = null;

            var arrayOfObjects = Args.AsArrayLiteral(args, 0);

            for (int i = 0; i < arrayOfObjects.Length; i++)
            {
                if (arrayOfObjects[i].IsUndefined)
                {
                    continue;
                }

                var arg = Converter.ExpectObjectLiteral(
                        arrayOfObjects[i],
                        new ConversionContext(pos: i, objectCtx: arrayOfObjects));
                if (result == null)
                {
                    result = arg;
                }
                else
                {
                    var merged = result.Merge(context, EvaluationStackFrame.UnsafeFrom(new EvaluationResult[0]), EvaluationResult.Create(arg));
                    // Merge can fail due to custom merge functions failing.
                    if (merged.IsErrorValue)
                    {
                        return merged;
                    }

                    // Left and right are guaranteed to be objects so the result must be object.
                    result = (ObjectLiteral)merged.Value;
                }
            }

            if (result == null)
            {
                return EvaluationResult.Undefined;
            }
            
            return EvaluationResult.Create(result);
        }

        private static EvaluationResult Override(Context context, ObjectLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            Contract.Requires(context != null);
            Contract.Requires(receiver != null);
            Contract.Requires(captures != null);

            return receiver.Override(context, arg);
        }

        private static EvaluationResult OverrideKey(Context context, ObjectLiteral receiver, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures)
        {
            var key = Converter.ExpectString(arg1);
            var value = arg2;
            var name = context.FrontEndContext.StringTable.AddString(key);
            var objLit = ObjectLiteral.Create(new Binding(name, value, location: default(LineInfo)));
            return Override(context, receiver, EvaluationResult.Create(objLit), captures);
        }

        private static EvaluationResult Merge(Context context, ObjectLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            Contract.Requires(context != null);
            Contract.Requires(receiver != null);
            Contract.Requires(captures != null);

            var result = receiver.Merge(context, captures, arg);

            // Merge can fail due to custom merge functions failing.
            if (result.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            return result;
        }

        private static EvaluationResult Keys(Context context, ObjectLiteral receiver, EvaluationStackFrame captures)
        {
            var keys = receiver.Keys.Select(id => EvaluationResult.Create(context.FrontEndContext.StringTable.GetString(id)));
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(keys.ToArray(), receiver.Location, receiver.Path));
        }

        private static EvaluationResult Get(Context context, ObjectLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var argAsStr = Converter.ExpectString(arg);
            return receiver[context.FrontEndContext.StringTable.AddString(argAsStr)];
        }

        private static EvaluationResult WithCustomMerge(Context context, ObjectLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);

            var entry = context.TopStack;
            var invocationLocation = entry.InvocationLocation;
            var invocationPath = entry.Path;

            // Creating a new object with a custom merge is equivalent to override the current object with an object that only has the custom merge
            // function as an entry. And since the object literal creation process depends on its number of members, let's just do the override 
            // explicitly
            var newObject = ObjectLiteral.Create(
                new [] {new NamedValue(context.Literals.CustomMergeFunction.StringId.Value, EvaluationResult.Create(closure)) },
                invocationLocation,
                invocationPath);

            return receiver.Combine(context, ObjectLiteral.OverrideFunction, EvaluationResult.Create(newObject));
        }
    }
}
