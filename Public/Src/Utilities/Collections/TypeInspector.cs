// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Collections
{

    /// <summary>
    /// Provides helper methods for inspecting type layouts.
    /// </summary>
    /// <remarks>
    /// Adopted from https://github.com/SergeyTeplyakov/ObjectLayoutInspector
    /// </remarks>
    internal static class TypeInspector
    {
        /// <summary>
        /// Returns an instance size and the overhead for a given type.
        /// </summary>
        /// <remarks>
        /// If <paramref name="type"/> is value type then the overhead is 0.
        /// Otherwise the overhead is 2 * PtrSize.
        /// </remarks>
        public static (int size, int overhead) GetSize(Type type)
        {
// The functionality is not supported in .net standard case
#if NET_STANDARD_20
            return (size: -1, overhead: 0);
#else
            if (type.IsValueType)
            {
                return (size: GetSizeOfValueTypeInstance(type), overhead: 0);
            }

            var size = GetSizeOfReferenceTypeInstance(type);
            return (size, 2 * IntPtr.Size);
#endif
        }

#if !NET_STANDARD_20
        /// <summary>
        /// Return s the size of a reference type instance excluding the overhead.
        /// </summary>
        public static int GetSizeOfReferenceTypeInstance(Type type)
        {
            Debug.Assert(!type.IsValueType);

            var fields = GetFieldOffsets(type);

            if (fields.Length == 0)
            {
                // Special case: the size of an empty class is 1 Ptr size
                return IntPtr.Size;
            }

            // The size of the reference type is computed in the following way:
            // MaxFieldOffset + SizeOfThatField
            // and round that number to closest point size boundary
            var maxValue = fields.MaxBy(tpl => tpl.offset);
            int sizeCandidate = maxValue.offset + GetFieldSize(maxValue.fieldInfo.FieldType);

            // Rounding this stuff to the nearest ptr-size boundary
            int roundTo = IntPtr.Size - 1;
            return (sizeCandidate + roundTo) & (~roundTo);
        }

        /// <summary>
        /// Returns the size of the field if the field would be of type <paramref name="t"/>.
        /// </summary>
        /// <remarks>
        /// For reference types the size is always a PtrSize.
        /// </remarks>
        public static int GetFieldSize(Type t)
        {
            if (t.IsValueType)
            {
                return GetSizeOfValueTypeInstance(t);
            }

            return IntPtr.Size;
        }

        /// <summary>
        /// Helper struct that is used for computing the size of a struct.
        /// </summary>
        struct SizeComputer<T>
        {
            // Both fields should be of the same type because the CLR can rearrange the struct and 
            // the offset of the second field would be the offset of the 'dummyField' not the offset of the 'offset' field.
            public T dummyField;
            public T offset;

            public SizeComputer(T dummyField, T offset) => (this.dummyField, this.offset) = (dummyField, offset);
        }

        /// <summary>
        /// Computes size for <paramref name="type"/>.
        /// </summary>
        public static int GetSizeOfValueTypeInstance(Type type)
        {
            Debug.Assert(type.IsValueType);

            var generatedType = typeof(SizeComputer<>).MakeGenericType(type);
            // The offset of the second field is the size of the 'type'
            var fieldsOffsets = GetFieldOffsets(generatedType);
            return fieldsOffsets[1].offset;
        }

        /// <summary>
        /// Gets an array of field information and their offsets for <typeparamref name="T"/>.
        /// </summary>
        public static (FieldInfo fieldInfo, int offset)[] GetFieldOffsets<T>()
        {
            return GetFieldOffsets(typeof(T));
        }

        /// <summary>
        /// Gets an array of field information with their offsets for a given <paramref name="t"/>.
        /// </summary>
        public static (FieldInfo fieldInfo, int offset)[] GetFieldOffsets(Type t)
        {
            // GetFields does not return private fields from the base types.
            // Need to use a custom helper function.
            var fields = t.GetInstanceFields();
            //var fields2 = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            Func<object?, long[]> fieldOffsetInspector = GenerateFieldOffsetInspectionFunction(fields);

            var (instance, success) = TryCreateInstanceSafe(t);
            if (!success)
            {
                return Array.Empty<(FieldInfo, int)>();
            }

            var addresses = fieldOffsetInspector(instance);

            if (addresses.Length <= 1)
            {
                return Array.Empty<(FieldInfo, int)>();
            }

            var baseLine = getBaseLine(addresses[0]);

            // Converting field addresses to offsets using the first field as a baseline
            return fields
                .Select((field, index) => (field: field, offset: (int)(addresses[index + 1] - baseLine)))
                .OrderBy(tpl => tpl.offset)
                .ToArray();

            long getBaseLine(long referenceAddress) => t.IsValueType ? referenceAddress : referenceAddress + IntPtr.Size;
        }

        private static Func<object?, long[]> GenerateFieldOffsetInspectionFunction(FieldInfo[] fields)
        {
            var method = new DynamicMethod(
                name: "GetFieldOffsets",
                returnType: typeof(long[]),
                parameterTypes: new[] { typeof(object) },
                m: typeof(TypeInspector).Module,
                skipVisibility: true);

            ILGenerator ilGen = method.GetILGenerator();

            // Declaring local variable of type long[]
            ilGen.DeclareLocal(typeof(long[]));
            // Loading array size onto evaluation stack
            ilGen.Emit(OpCodes.Ldc_I4, fields.Length + 1);

            // Creating an array and storing it into the local
            ilGen.Emit(OpCodes.Newarr, typeof(long));
            ilGen.Emit(OpCodes.Stloc_0);

            // Loading the local with an array
            ilGen.Emit(OpCodes.Ldloc_0);

            // Loading an index of the array where we're going to store the element
            ilGen.Emit(OpCodes.Ldc_I4, 0);

            // Loading object instance onto evaluation stack
            ilGen.Emit(OpCodes.Ldarg_0);

            // Converting reference to long
            ilGen.Emit(OpCodes.Conv_I8);

            // Storing the reference in the array
            ilGen.Emit(OpCodes.Stelem_I8);

            for (int i = 0; i < fields.Length; i++)
            {
                // Loading the local with an array
                ilGen.Emit(OpCodes.Ldloc_0);

                // Loading an index of the array where we're going to store the element
                ilGen.Emit(OpCodes.Ldc_I4, i + 1);

                // Loading object instance onto evaluation stack
                ilGen.Emit(OpCodes.Ldarg_0);

                // Getting the address for a given field
                ilGen.Emit(OpCodes.Ldflda, fields[i]);

                // Converting field offset to long
                ilGen.Emit(OpCodes.Conv_I8);

                // Storing the offset in the array
                ilGen.Emit(OpCodes.Stelem_I8);
            }

            ilGen.Emit(OpCodes.Ldloc_0);
            ilGen.Emit(OpCodes.Ret);

            return (Func<object?, long[]>)method.CreateDelegate(typeof(Func<object, long[]>));
        }

        /// <summary>
        /// Returns all instance fields including the fields declared in all base types.
        /// </summary>
        public static FieldInfo[] GetInstanceFields(this Type type)
        {
            return getBaseTypesAndThis(type).SelectMany(t => getDeclaredFields(t)).Where(fi => !fi.IsStatic).ToArray();

            IEnumerable<Type> getBaseTypesAndThis(Type? t)
            {
                while (t != null)
                {
                    yield return t;

                    t = t.BaseType;
                }
            }

            IEnumerable<FieldInfo> getDeclaredFields(Type t)
            {
                if (t is TypeInfo ti)
                {
                    return ti.DeclaredFields;
                }

                return t.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic);
            }
        }

        /// <summary>
        /// Tries to create an instance of a given type.
        /// </summary>
        /// <remarks>
        /// There is a limit of what types can be instantiated.
        /// The following types are not supported by this function:
        /// * Open generic types like <code>typeof(List&lt;&gt;)</code>
        /// * Abstract types
        /// </remarks>
        public static (object? result, bool success) TryCreateInstanceSafe(Type t)
        {
            if (!CanCreateInstance(t))
            {
                return (result: null, success: false);
            }

            // Value types are handled separately
            if (t.IsValueType)
            {
                return Success(Activator.CreateInstance(t));
            }

            // String is handled separately as well due to security restrictions
            if (t == typeof(string))
            {
                return Success(string.Empty);
            }

            // It is actually possible that GetUnitializedObject will return null.
            // I've got null for some security related types.
            return Success(GetUninitializedObject(t));

            (object? result, bool success) Success(object? o) => (o, o != null);
        }

        private static object? GetUninitializedObject(Type t)
        {
            try
            {
                return FormatterServices.GetUninitializedObject(t);
            }
            catch (TypeInitializationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the instance of type <paramref name="t"/> can be instantiated.
        /// </summary>
        public static bool CanCreateInstance(this Type t)
        {
            // Abstract types and generics are not supported
            if (t.IsAbstract || isOpenGenericType(t) || t.IsCOMObject)
            {
                return false;
            }

            // TODO: check where ArgIterator is located
            if (// t == typeof(ArgIterator) || 
                t == typeof(RuntimeArgumentHandle) || t == typeof(TypedReference) || t.Name == "Void"
                || t == typeof(IsVolatile) || t == typeof(RuntimeFieldHandle) || t == typeof(RuntimeMethodHandle) ||
                t == typeof(RuntimeTypeHandle))
            {
                // This is a special type
                return false;
            }

            if (t.BaseType == typeof(ContextBoundObject))
            {
                return false;
            }

            return true;
            static bool isOpenGenericType(Type type)
            {
                return type.IsGenericTypeDefinition && !type.IsConstructedGenericType;
            }
        }

        /// <summary>
        /// Returns true if a given type is unsafe.
        /// </summary>
        public static bool IsUnsafeValueType(this Type t)
        {
            return t.GetCustomAttribute(typeof(UnsafeValueTypeAttribute)) != null;
        }
#endif // NET_STANDARD_20

#if !NET5_0_OR_GREATER
        /// <summary>
        /// Gets the item with the max key given the optional key comparer.
        /// </summary>
        [return: MaybeNull]
        public static T? MaxBy<T, TKey>(this IEnumerable<T> items, Func<T, TKey> keySelector, IComparer<TKey>? keyComparer = null)
        {
            keyComparer ??= Comparer<TKey>.Default;
            T? maxItem = default;
            TKey? maxKey = default;
            bool isFirst = true;

            bool hasElements = false;
            foreach (var item in items)
            {
                hasElements = true;
                var currentKey = keySelector(item);
                if (isFirst || keyComparer.Compare(currentKey, maxKey!) > 0)
                {
                    isFirst = false;
                    maxItem = item;
                    maxKey = currentKey;
                }
            }

            if (!hasElements)
            {
                throw new InvalidOperationException("The sequence has no elements.");
            }

            return maxItem;
        }
#endif
    }
}