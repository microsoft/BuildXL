// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.ContractsLight;
using System.Linq.Expressions;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Runtime;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using Expression = BuildXL.FrontEnd.Script.Expressions.Expression;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic value caching functionality.
    /// </summary>
    public sealed class AmbientValueCache : AmbientDefinitionBase
    {
        /// <nodoc />
        public AmbientValueCache(PrimitiveTypes knownTypes)
            : base("ValueCache", knownTypes)
        {
        }

        /// <nodoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("ValueCache"),
                new[]
                {
                    Function("getOrAdd", GetOrAdd, GetOrAddSignature),
                    Function("getOrAddWithState", GetOrAddWithState, GetOrAddWithStateSignature),
                });
        }

        /// <summary>
        /// DScript exposes a value cache. The backing store is kept inside of 'IEvaluationScheduler'.
        /// Values from this cache should never be returned directly; instead, the result should be cloned first 
        /// (to avoid exposing an observable side effect).
        /// </summary>
        private EvaluationResult GetOrAdd(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 0);
            var key = args[0];
            var closure = Args.AsClosure(args, 1);

            return DoGetOrAdd(context, env, key, null, closure);
        }

        /// <summary>
        /// Same as <see cref="GetOrAdd(Context, ModuleLiteral, EvaluationStackFrame)"/> except that a state is 
        /// passed to the function and then propagated to the callback.
        /// </summary>
        private EvaluationResult GetOrAddWithState(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 0);
            var key = args[0];

            Args.CheckArgumentIndex(args, 0);
            var state = args[1];
            var closure = Args.AsClosure(args, 2);

            return DoGetOrAdd(context, env, key, state, closure);
        }

        private EvaluationResult DoGetOrAdd(Context context, ModuleLiteral env, EvaluationResult key, EvaluationResult? state, Closure factoryClosure)
        {
            var helper = new HashingHelper(context.PathTable, recordFingerprintString: false);

            // Add the qualifier to the key
            var qualifierId = context.LastActiveModuleQualifier.QualifierId;
            var qualifierDisplayString = context.ContextTree.FrontEndContext.QualifierTable.GetQualifier(qualifierId).ToDisplayString(StringTable);
            helper.Add(qualifierDisplayString);
            if (!TryHashValue(key, helper))
            {
                return EvaluationResult.Error;
            }

            var keyFingerprint = helper.GenerateHash().ToHex();
            var thunkCreatedByThisThread = false;

            // ensure that all concurrent evaluations of the same value cache key will get the same thunk and module
            var thunkAndModule = context.EvaluationScheduler.ValueCacheGetOrAdd(
                keyFingerprint,
                () =>
                {
                    var factoryArgs = state.HasValue 
                        ? new Expression[] { new LocalReferenceExpression(SymbolAtom.Create(context.StringTable, "state"), index: factoryClosure.Frame.Length, default) }
                        : CollectionUtilities.EmptyArray<Expression>();

                    var thunk = new Thunk(ApplyExpression.Create(factoryClosure, factoryArgs, factoryClosure.Location), null);
                    var module = context.LastActiveUsedModule;
                    thunkCreatedByThisThread = true;
                    return (thunk, module);
                });

            // proceed to evaluate that same thunk concurrently; the cycle detector will detect cycles if any

            var contextFactory = new Thunk.MutableContextFactory(
                thunkAndModule.thunk,
                FullSymbol.Create(context.SymbolTable, "vc_" + keyFingerprint),
                thunkAndModule.module, 
                templateValue: null,
                location: factoryClosure.Location,
                // this makes sure that exactly the thread that created the thunk gets to evaluate it
                // (semantically, this doesn't change anything; it just makes debugging more intuitive)
                forceWaitForResult: !thunkCreatedByThisThread);

            var frame = EvaluationStackFrame.Empty();
            if (state.HasValue)
            {
                frame = EvaluationStackFrame.Create(frameSize: factoryClosure.Frame.Length + 1, paramsCount: 1, paramsOffset: factoryClosure.Frame.Length);
                for (int i = 0; i < factoryClosure.Frame.Length; i++)
                {
                    frame[i] = factoryClosure.Frame[i];
                }
                frame[factoryClosure.Frame.Length] = state.Value;
            }
            
            using (frame)
            {
                var resultToClone = thunkAndModule.thunk.Evaluate(context, thunkAndModule.module, frame, ref contextFactory);

                // The object returned will always be a cloned copy.
                // DScript is a side effect free language, but we use object identity
                // for equality comparison so to avoid making cache hits observable to
                // users we opt to clone the value from the cache each time, even after the
                // first time we add it to the cache.
                // This is also the reason why we don't have separate functions for inspecting or simply adding
                // because the results would be observable and potentially invalidating all the
                // incremental evaluations in DScript.
                return DeepCloneValue(resultToClone);
            }
        }

        private CallSignature GetOrAddSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.AnyType, PrimitiveType.AnyType),
            returnType: PrimitiveType.AnyType);

        private CallSignature GetOrAddWithStateSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.AnyType, PrimitiveType.AnyType, PrimitiveType.AnyType),
            returnType: PrimitiveType.AnyType);
        
        /// <summary>
        /// Helper to Deeply clone values
        /// </summary>
        public static EvaluationResult DeepCloneValue(EvaluationResult value)
        {
            if (value.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            if (value.IsUndefined)
            {
                return value; // Undefined is structurally compared
            }

            switch (value.Value)
            {
                case bool boolean:
                case string str:
                case int number:
                case EnumValue enumValue:
                case PathAtom pathAtomValue:
                case RelativePath relPathValue:
                case AbsolutePath pathValue:
                case FileArtifact fileValue:
                case DirectoryArtifact dirValue:
                case StaticDirectory staticDirValue:
                    return value; // Intrinsics are structurally compared
                case OrderedMap map:
                    return new EvaluationResult(DeepCloneMap(map));
                case OrderedSet set:
                    return new EvaluationResult(DeepCloneSet(set));
                case ArrayLiteral array:
                    return new EvaluationResult(DeepCloneArray(array));
                case ObjectLiteral obj:
                    return new EvaluationResult(DeepCloneObject(obj));
                case Closure closure:
                    var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value.Value);
                    throw new UnsupportedTypeValueObjectException(typeOfKind.ToRuntimeString(), new ErrorContext(pos: 1));
                default:
                    throw Contract.AssertFailure("Unexpected runtime value");
            }
        }

        private static OrderedMap DeepCloneMap(OrderedMap map)
        {
            var taggedEntries = new KeyValuePair<EvaluationResult, TaggedEntry>[map.Count];
            long tag = 0;

            foreach (var kv in map)
            {
                var clonedKey = DeepCloneValue(kv.Key);
                var clonedValue = DeepCloneValue(kv.Value);
                taggedEntries[tag] = new KeyValuePair<EvaluationResult, TaggedEntry>(clonedKey, new TaggedEntry(clonedValue, tag));
                tag++;
            }

            return new OrderedMap(ImmutableDictionary<EvaluationResult, TaggedEntry>.Empty.SetItems(taggedEntries), tag);
        }

        private static OrderedSet DeepCloneSet(OrderedSet set)
        {
            var taggedItems = new TaggedEntry[set.Count];
            long tag = 0;

            foreach (var item in set)
            {
                var clonedItem = DeepCloneValue(item);
                taggedItems[tag] = new TaggedEntry(clonedItem, tag);
                tag++;
            }

            return new OrderedSet(ImmutableHashSet<TaggedEntry>.Empty.Union(taggedItems), tag);
        }

        private static ArrayLiteral DeepCloneArray(ArrayLiteral array)
        {
            var results = new EvaluationResult[array.Count];

            for (int i = 0; i < array.Count; i++)
            {
                results[i] = DeepCloneValue(array[i]);
            }

            return ArrayLiteral.CreateWithoutCopy(results, array.Location, array.Path);
        }

        private static ObjectLiteral DeepCloneObject(ObjectLiteral obj)
        {
            var namedValues = new List<NamedValue>(obj.Count);

            foreach (var member in obj.Members)
            {
                namedValues.Add(new NamedValue(member.Key.Value, DeepCloneValue(member.Value)));
            }

            return ObjectLiteral.Create(namedValues, obj.Location, obj.Path);
        }

        /// <summary>
        /// Attempt to hash the runtime value.
        /// </summary>
        public static bool TryHashValue(EvaluationResult value, HashingHelper helper)
        {
            if (value.IsErrorValue)
            {
                return false;
            }

            // Always mark the kind
            var valueKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value.Value);
            helper.Add((byte)valueKind);

            if (value.IsUndefined)
            {
                return true;
            }

            switch (value.Value)
            {
                case bool boolean:
                    helper.Add(boolean ? 1 : 0);
                    return true;
                case string str:
                    helper.Add(str);
                    return true;
                case int number:
                    helper.Add(number);
                    return true;
                case EnumValue enumValue:
                    helper.Add(enumValue.Value);
                    helper.Add(enumValue.Name.StringId);
                    return true;
                case OrderedMap map:
                    return TryHashMap(map, helper);
                case OrderedSet set:
                    return TryHashSet(set, helper);
                case ArrayLiteral array:
                    return TryHashArray(array, helper);
                case ObjectLiteral obj:
                    return TryHashObject(obj, helper);
                case PathAtom pathAtomValue:
                    helper.Add(pathAtomValue.StringId);
                    return true;
                case RelativePath relPathValue:
                    return TryHashRelativePath(relPathValue, helper);
                case AbsolutePath pathValue:
                    helper.Add(pathValue);
                    return true;
                case FileArtifact fileValue:
                    helper.Add(fileValue);
                    return true;
                case DirectoryArtifact dirValue:
                    return TryHashDirectoryArtifact(dirValue, helper);
                case StaticDirectory staticDirValue:
                    return TryHashStaticDirectoryArtifact(staticDirValue, helper);
                case Closure closure:
                    var typeOfKind = RuntimeTypeIdExtensions.ComputeTypeOfKind(value.Value);
                    throw new UnsupportedTypeValueObjectException(typeOfKind.ToRuntimeString(), new ErrorContext(pos: 1));
                default:
                    throw Contract.AssertFailure("Unexpected runtime value");
            }
        }

        private static bool TryHashStaticDirectoryArtifact(StaticDirectory staticDirValue, HashingHelper helper)
        {
            helper.Add((int)staticDirValue.SealDirectoryKind);

            if (!TryHashDirectoryArtifact(staticDirValue.Root, helper))
            {
                return false;
            }

            helper.Add(staticDirValue.Contents.Length);
            foreach (var content in staticDirValue.Contents)
            {
                helper.Add(content);
            }

            return true;
        }

        private static bool TryHashDirectoryArtifact(DirectoryArtifact dirValue, HashingHelper helper)
        {
            // Okay to add the partial seal id and other fields here. the partial seal id is not stable build-over-bulid but this cache is just for a single build
            // Therfore for safety this is implemnted here and not implemented in the hasinghelper.
            helper.Add(dirValue.Path);
            helper.Add("partialSealId", dirValue.PartialSealId);
            helper.Add(dirValue.IsSharedOpaque ? 1 : 0);
            return true;
        }

        private static bool TryHashSet(OrderedSet set, HashingHelper helper)
        {
            helper.Add(set.Count);
            foreach (var item in set)
            {
                if (!TryHashValue(item, helper))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryHashMap(OrderedMap map, HashingHelper helper)
        {
            helper.Add(map.Count);
            foreach (var kv in map)
            {
                if (!TryHashValue(kv.Key, helper))
                {
                    return false;
                }
                if (!TryHashValue(kv.Value, helper))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryHashArray(ArrayLiteral array, HashingHelper helper)
        {
            helper.Add(array.Length);
            foreach (var item in array.Values)
            {
                if (!TryHashValue(item, helper))
                {
                    return false;
                }
            }

            return true;
        }
        private static bool TryHashObject(ObjectLiteral obj, HashingHelper helper)
        {
            helper.Add(obj.Count);
            foreach (var member in obj.Members)
            {
                if (!ComputeKeyFingerPrint(member.Key, helper))
                {
                    return false;
                }

                if (!TryHashValue(member.Value, helper))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryHashRelativePath(RelativePath relPath, HashingHelper helper)
        {
            var components = relPath.Components;
            helper.Add(components.Length);
            foreach (var component in components)
            {
                helper.Add(component);
            }

            return true;
        }

        private static bool ComputeKeyFingerPrint(StringId value, HashingHelper helper)
        {
            helper.Add(value);
            return true;
        }

    }
}
