// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Script.Runtime;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;

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
                });
        }

        private EvaluationResult GetOrAdd(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 0);
            var key = args[0];
            var closure = Args.AsClosure(args, 1);

            var helper = new HashingHelper(context.PathTable, recordFingerprintString: false);

            // Add the qualifier to the key
            var qualifierId = context.LastActiveModuleQualifier.QualifierId;
            var qualifierDisplayString = context.ContextTree.FrontEndContext.QualifierTable.GetQualifier(qualifierId).ToDisplayString(StringTable);
            helper.Add(qualifierDisplayString);
            if (!TryHashValue(key, helper))
            {
                return EvaluationResult.Error;
            }

            var keyFingerprint = helper.GenerateHash();

            var resultToClone = context.ContextTree.ValueCache.GetOrAdd(
                keyFingerprint,
                _ =>
                {
                    int paramsCount = closure.Function.Params;
                    var newValue = context.InvokeClosure(closure, closure.Frame);
                    if (newValue.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    return newValue;
                }
            );

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

        private CallSignature GetOrAddSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.AnyType, PrimitiveType.AnyType),
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
