// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using DsMap = BuildXL.FrontEnd.Script.Ambients.Map.OrderedMap;
using DsSet = BuildXL.FrontEnd.Script.Ambients.Set.OrderedSet;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Class for handling conversion from objects to specific types of values.
    /// </summary>
    public static class Converter
    {
        /// <summary>
        /// Converts an object to a boolean.
        /// </summary>
        public static bool ExpectBool(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectValue<bool>(value, context);
        }

        /// <summary>
        /// Converts an object to a 32-bit int.
        /// </summary>
        public static int ExpectNumber(EvaluationResult value, bool strict = true, in ConversionContext context = default(ConversionContext))
        {
            if (value.Value is int i)
            {
                return i;
            }

            if (!strict)
            {
                if (value.Value is EnumValue enumValue)
                {
                    return enumValue.Value;
                }
            }

            ThrowException(typeof(int), value, context);
            return default(int);
        }

        /// <summary>
        /// Tries to convert an object to a given type.
        /// </summary>
        /// <returns>True iff the direct cast succeeds.</returns>
        public static bool TryGet<T>(EvaluationResult value, out T i)
        {
            if (value.Value is T variable)
            {
                i = variable;
                return true;
            }

            i = default(T); // Set to a dummy value
            return false;
        }

        /// <summary>
        /// Converts an object to a number.
        /// </summary>
        public static int ExpectNumber(EvaluationResult value, int position)
        {
            if (value.Value is int i)
            {
                return i;
            }

            ThrowException(typeof(int), value, position);
            return default(int);
        }

        /// <summary>
        /// Converts an object to a number.
        /// </summary>
        public static int ExpectNumberOrEnum(EvaluationResult value, int position)
        {
            if (value.Value is int i)
            {
                return i;
            }

            if (value.Value is EnumValue enumValue)
            {
                return enumValue.Value;
            }

            throw CreateException(new[] { typeof(int), typeof(EnumValue) }, value, context: new ConversionContext(pos: position));
        }

        /// <summary>
        /// Converts an object to a number or enum.
        /// </summary>
        public static int? GetNumberOrEnumValue(EvaluationResult value)
        {
            if (value.Value is int i)
            {
                return i;
            }

            var enumValue = value.Value as EnumValue;

            return enumValue?.Value;
        }

        /// <summary>
        /// Wraps <paramref name="value"/> into <see cref="EnumValue"/> if <paramref name="sourceType"/> is enum.
        /// </summary>
        public static EvaluationResult ToEnumValueIfNeeded(System.Type sourceType, int value)
        {
            if (sourceType == typeof(EnumValue))
            {
                // Need to cast to object to avoid implicit conversion from enum to int.
                // TODO: consider removing that implicit conversion altogether.
                return EvaluationResult.Create((object)EnumValue.Create(value));
            }

            return NumberLiteral.Box(value);
        }

        /// <summary>
        /// Converts an object to an enum value.
        /// </summary>
        public static EnumValue ExpectEnumValue(EvaluationResult value, int position)
        {
            var context = new ConversionContext(pos: position);
            return ExpectRef<EnumValue>(value, context);
        }

        /// <summary>
        /// Converts an object to a path atom.
        /// </summary>
        public static PathAtom ExpectPathAtom(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectValue<PathAtom>(value, context);
        }

        /// <summary>
        /// Converts an object to a relative path.
        /// </summary>
        public static RelativePath ExpectRelativePath(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectValue<RelativePath>(value, context);
        }

        /// <summary>
        /// Converts object to an absolute path.
        /// </summary>
        public static AbsolutePath ExpectPath(EvaluationResult value, bool strict = true, in ConversionContext context = default(ConversionContext))
        {
            if (value.Value is AbsolutePath path)
            {
                return path;
            }

            if (!strict)
            {
                switch (value.Value)
                {
                    case FileArtifact artifact:
                        return artifact.Path;
                    case DirectoryArtifact directoryArtifact:
                        return directoryArtifact.Path;
                    case StaticDirectory staticDirectory:
                        return staticDirectory.Path;
                }
            }

            throw CreateException<AbsolutePath>(value, context);
        }

        /// <summary>
        /// Converts an object to a string.
        /// </summary>
        public static string ExpectString(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<string>(value, context);
        }

        /// <summary>
        /// Converts an object to a file.
        /// </summary>
        public static FileArtifact ExpectFile(EvaluationResult value, bool strict = true, in ConversionContext context = default(ConversionContext))
        {
            if (value.Value is FileArtifact artifact)
            {
                return artifact;
            }

            if (!strict)
            {
                if (value.Value is AbsolutePath path)
                {
                    return FileArtifact.CreateSourceFile(path);
                }
            }

            throw CreateException<FileArtifact>(value, context);
        }

        /// <summary>
        /// Converts an object to a directory.
        /// </summary>
        public static DirectoryArtifact ExpectDirectory(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectValue<DirectoryArtifact>(value, context);
        }

        /// <summary>
        /// Converts an object to a static directory.
        /// </summary>
        public static StaticDirectory ExpectStaticDirectory(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<StaticDirectory>(value, context);
        }

        /// <summary>
        /// Converts an object to a static directory with SharedOpaque kind.
        /// </summary>
        public static StaticDirectory ExpectSharedOpaqueDirectory(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            var staticDirectory = ExpectRef<StaticDirectory>(value, context);
            if (staticDirectory.SealDirectoryKind != BuildXL.Pips.Operations.SealDirectoryKind.SharedOpaque)
            {
                throw CreateException<StaticDirectory>(value, context);
            }

            return staticDirectory;
        }

        /// <summary>
        /// Converts an object to a pip id.
        /// </summary>
        public static PipId ExpectPipId(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectValue<PipId>(value, context);
        }

        /// <summary>
        /// Converts an object to either FileArtifact (if value is a FileArtifact or an AbsolutePath) or StaticDirectory.
        /// </summary>
        /// <remarks>
        /// This static method is useful for converting input artifacts into more concrete objects, i.e., file or static directory.
        /// </remarks>
        public static void ExpectFileOrStaticDirectory(
            EvaluationResult value,
            out FileArtifact file,
            out StaticDirectory staticDirectory,
            in ConversionContext context = default(ConversionContext))
        {
            file = FileArtifact.Invalid;
            staticDirectory = null;

            if (value.Value is FileArtifact artifact)
            {
                file = artifact;
            }
            else
            {
                staticDirectory = value.Value as StaticDirectory;
            }

            if (!file.IsValid && staticDirectory == null)
            {
                throw CreateException(new[] { typeof(FileArtifact), typeof(StaticDirectory) }, value, context);
            }
        }

        /// <summary>
        /// Converts an object to either FileArtifact (if value is a FileArtifact or an AbsolutePath) or DirectoryArtifact or StaticDirectory
        /// </summary>
        public static void ExpectPathOrFileOrDirectory(
            EvaluationResult value,
            out FileArtifact file,
            out DirectoryArtifact dir,
            out AbsolutePath path,
            in ConversionContext context = default(ConversionContext))
        {
            file = FileArtifact.Invalid;
            dir = DirectoryArtifact.Invalid;
            path = AbsolutePath.Invalid;

            if (value.Value is FileArtifact artifact)
            {
                file = artifact;
                return;
            }

            if (!TryGetPathOrDirectory(value, out path, out dir))
            {
                throw CreateException(
                    new[] { typeof(DirectoryArtifact), typeof(FileArtifact), typeof(AbsolutePath), typeof(StaticDirectory) },
                    value,
                    context);
            }
        }

        /// <summary>
        /// Converts an object to a path or directory.
        /// </summary>
        public static void ExpectPathOrDirectory(
            EvaluationResult value,
            out AbsolutePath path,
            out DirectoryArtifact dir,
            in ConversionContext context = default(ConversionContext))
        {
            if (!TryGetPathOrDirectory(value, out path, out dir))
            {
                throw CreateException(
                    new[] { typeof(DirectoryArtifact), typeof(AbsolutePath), typeof(StaticDirectory) },
                    value,
                    context);
            }
        }

        private static bool TryGetPathOrDirectory(
            EvaluationResult result,
            out AbsolutePath path,
            out DirectoryArtifact dir)
        {
            path = AbsolutePath.Invalid;
            dir = DirectoryArtifact.Invalid;
            object value = result.Value;

            if (value is AbsolutePath absolutePath)
            {
                path = absolutePath;
            }
            else if (value is DirectoryArtifact)
            {
                dir = (DirectoryArtifact)value;
            }
            else
            {
                if (value is StaticDirectory staticDir)
                {
                    dir = staticDir.Root;
                }
            }

            return path.IsValid || dir.IsValid;
        }

        /// <summary>
        /// Converts an object to an object literal.
        /// </summary>
        public static ObjectLiteral ExpectObjectLiteral(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<ObjectLiteral>(value, context);
        }

        /// <summary>
        /// Converts an object to an IpcMoniker.
        /// </summary>
        public static IIpcMoniker ExpectMoniker(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<IIpcMoniker>(value, context);
        }

        /// <summary>
        /// Extracts <see cref="ObjectLiteral"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns null if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static ObjectLiteral ExtractObjectLiteral(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            return ExtractRef<ObjectLiteral>(literal, property, allowUndefined);
        }

        /// <summary>
        /// Extracts <see cref="ArrayLiteral"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns null if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static ArrayLiteral ExtractArrayLiteral(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            return ExtractRef<ArrayLiteral>(literal, property, allowUndefined);
        }

        /// <summary>
        /// Extracts <see cref="ArrayLiteral"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns null if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static string[] ExtractStringArray(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var array = ExtractArrayLiteral(literal, property, allowUndefined);
            return Converter.ExpectStringArray(array);
        }

        /// <summary>
        /// Extracts an object of a concrete reference type from a given object.
        /// </summary>
        /// <remarks>
        /// Returns null if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static T ExtractRef<T>(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false) where T : class
        {
            var context = new ConversionContext(name: property, objectCtx: literal, allowUndefined: allowUndefined);
            return ExpectRef<T>(literal[property], context);
        }

        /// <summary>
        /// Extracts a number from a given object's property.
        /// </summary>
        /// <remarks>
        /// Returns null if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static int? ExtractNumber(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            return ExtractValue<int>(literal, property, allowUndefined);
        }

        /// <summary>
        /// Extracts a value from a given object's property.
        /// </summary>
        /// <remarks>
        /// Returns null if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static T? ExtractValue<T>(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false) where T : struct
        {
            var context = new ConversionContext(name: property, objectCtx: literal, allowUndefined: allowUndefined);
            var propertyValue = literal[property];
            if (propertyValue.IsUndefined)
            {
                if (allowUndefined)
                {
                    return null;
                }

                throw CreateException<T>(propertyValue, context);
            }

            return ExpectValue<T>(propertyValue, context);
        }

        /// <summary>
        /// Extracts <see cref="AbsolutePath"/>, <see cref="BuildXL.Utilities.FileArtifact"/>, <see cref="BuildXL.Utilities.DirectoryArtifact"/> or <see cref="StaticDirectory"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="AbsolutePath.Invalid"/> if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static AbsolutePath ExtractPathLike(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return AbsolutePath.Invalid;
            }

            return ExpectPath(
                value,
                strict: false, // Path like, means that not only path is convertible to AbsolutePath, but FileLiteral and Directory as well.
                context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts <see cref="PathAtom"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="PathAtom.Invalid"/> if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static PathAtom ExtractPathAtom(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return PathAtom.Invalid;
            }

            return ExpectPathAtom(
                value,
                context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts <see cref="RelativePath"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="RelativePath.Invalid"/> if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static RelativePath ExtractRelativePath(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return RelativePath.Invalid;
            }

            return ExpectRelativePath(
                value,
                context: new ConversionContext(name: property, objectCtx: literal));
        }


        /// <summary>
        /// Extracts <see cref="AbsolutePath"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="AbsolutePath.Invalid"/> if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static AbsolutePath ExtractPath(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return AbsolutePath.Invalid;
            }

            return ExpectPath(
                value,
                strict: true,
                context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts <see cref="BuildXL.Utilities.FileArtifact"/> or <see cref="AbsolutePath"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="FileArtifact.Invalid"/> if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static FileArtifact ExtractFileLike(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return FileArtifact.Invalid;
            }

            return ExpectFile(
                value,
                strict: false,
                context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts <see cref="BuildXL.Utilities.DirectoryArtifact"/> instance from a given object.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="DirectoryArtifact.Invalid"/> if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static DirectoryArtifact ExtractDirectory(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return DirectoryArtifact.Invalid;
            }

            return ExpectDirectory(
                value,
                context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts string property.
        /// </summary>
        /// <remarks>
        /// Returns 'null' if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// </remarks>
        public static string ExtractString(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return null;
            }

            return ExpectString(value, context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts string literal property. This matches the string literal TypeScript construct. e.g. "a" | "b" | "c"
        /// </summary>
        /// <remarks>
        /// Returns 'null' if <paramref name="allowUndefined"/> is true and <paramref name="literal"/> doesn't have a given property.
        /// The extracted string should be contained in the set <paramref name="validStrings"/>
        /// </remarks>
        public static string ExtractStringLiteral(ObjectLiteral literal, SymbolAtom property, ICollection<string> validStrings, bool allowUndefined = false)
        {
            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return null;
            }

            var stringLiteral = ExpectString(value, context: new ConversionContext(name: property, objectCtx: literal));
            if (validStrings.Contains(stringLiteral))
            {
                return stringLiteral;
            }

            throw CreateException(string.Join(" | ", validStrings), value, new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts an enum value from a property.
        /// </summary>
        /// <remarks>
        /// There is no Enum constraint available until C# 7.3, so IConvertible is used as an approximation
        /// </remarks>
        public static T? ExtractEnumValue<T>(ObjectLiteral literal, SymbolAtom property, bool allowUndefined = false) where T: struct, IConvertible
        {
            var enumType = typeof(T);
            Contract.Requires(enumType.IsEnum);

            var value = literal[property];
            if (allowUndefined && value.IsUndefined)
            {
                return null;
            }

            var context = new ConversionContext(name: property, objectCtx: literal);
            var enumValueObject = Enum.ToObject(enumType, ExpectEnumValue(value, context).Value);
            
            if (enumValueObject is T enumValue)
            {
                return enumValue;
            }

            throw CreateException<T>(value, context);
        }

        /// <summary>
        /// Extracts optional boolean property.
        /// Returns 'null' if the property is missing in a given object literal.
        /// </summary>
        public static bool? ExtractOptionalBoolean(ObjectLiteral literal, SymbolAtom property)
        {
            var value = literal[property];
            if (value.IsUndefined)
            {
                return null;
            }

            return ExpectBool(value, context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts optional int property.
        /// Returns 'null' if the property is missing in a given object literal.
        /// </summary>
        public static int? ExtractOptionalInt(ObjectLiteral literal, SymbolAtom property)
        {
            var value = literal[property];
            if (value.IsUndefined)
            {
                return null;
            }

            return ExpectNumber(value, context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Extracts int property.
        /// </summary>
        public static int? ExtractInt(ObjectLiteral literal, SymbolAtom property)
        {
            var value = literal[property];
            return ExpectNumber(value, context: new ConversionContext(name: property, objectCtx: literal));
        }

        /// <summary>
        /// Converts an object to an array literal.
        /// </summary>
        public static ArrayLiteral ExpectArrayLiteral(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<ArrayLiteral>(value, context);
        }

        /// <summary>
        /// Converts an object to an enum value.
        /// </summary>
        public static EnumValue ExpectEnumValue(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<EnumValue>(value, context);
        }

        /// <summary>
        /// Converts an object to a closure.
        /// </summary>
        public static Closure ExpectClosure(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<Closure>(value, context);
        }

        /// <summary>
        /// Converts an object to a map.
        /// </summary>
        public static DsMap ExpectMap(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<DsMap>(value, context);
        }

        /// <summary>
        /// Converts an object to a set.
        /// </summary>
        public static DsSet ExpectSet(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<DsSet>(value, context);
        }

        /// <summary>
        /// Converts an object to a set.
        /// </summary>
        public static MutableSet ExpectMutableSet(EvaluationResult value, in ConversionContext context = default(ConversionContext))
        {
            return ExpectRef<MutableSet>(value, context);
        }

        /// <summary>
        /// Converst an array to an array of strings.
        /// </summary>
        public static string[] ExpectStringArray(ArrayLiteral array)
        {
            if (array == null)
            {
                return CollectionUtilities.EmptyArray<string>();
            }

            var result = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                result[i] = Converter.ExpectString(array[i], new ConversionContext(pos: i, objectCtx: array));
            }

            return result;
        }

        /// <summary>
        /// Converts an object to a value of value type.
        /// </summary>
        private static T ExpectValue<T>(EvaluationResult value, in ConversionContext context) where T : struct
        {
            if (value.Value is T variable)
            {
                return variable;
            }

            throw CreateException<T>(value, context);
        }

        /// <summary>
        /// Converts an object to a value of reference type.
        /// </summary>
        private static T ExpectRef<T>(EvaluationResult value, in ConversionContext context) where T : class
        {
            // This method:
            //       public static T ExpectRef<T>(object o)
            //            where T : class
            //       {
            //            return o as T;
            //       }
            // is 3x slower than
            //       public static ArrayLiteral ExpectArray(object o)
            //       {
            //            return o as ArrayLiteral;
            //       }
            // because the IL of the former method introduce additionally OpCode_unbox.Any
            // https://msdn.microsoft.com/en-us/library/system.reflection.emit.opcodes.unbox_any(v=vs.110).aspx
            // whose behavior is like explicit cast, ( (ArrayLiteral) o), and explicit cast is known to be more
            // expensive because it throws exceptions.

            if (value.Value is T refValue)
            {
                return refValue;
            }

            if (context.AllowUndefined && value.IsUndefined)
            {
                return null;
            }

            throw CreateException<T>(value, context);
        }

        /// <summary>
        /// Expects a path atom from either a string or a path atom.
        /// </summary>
        /// <exception cref="InvalidPathAtomException">If the string segment is not a valid path atom.</exception>
        /// <exception cref="ConvertException">If neither a string nor a path atom is given.</exception>
        public static PathAtom ExpectPathAtomFromStringOrPathAtom(
            StringTable stringTable,
            EvaluationResult segmentValue,
            in ConversionContext context = default)
        {
            Contract.Requires(stringTable != null);

            PathAtom atom;
            object segment = segmentValue.Value;
            if (segment is string seg)
            {
                if (!PathAtom.TryCreate(stringTable, seg, out atom))
                {
                    throw new InvalidPathAtomException(seg, context.ErrorContext);
                }
            }
            else if (segment is PathAtom pathAtom)
            {
                atom = pathAtom;
            }
            else
            {
                throw CreateException(new[] { typeof(string), typeof(PathAtom) }, segmentValue, context);
            }

            return atom;
        }

        /// <summary>
        /// Expects a path fragment (a path atom or a relative path) from either a string, a path atom, or a relative path.
        /// <exception cref="InvalidRelativePathException">If the string fragment is not a valid relative path (and thus a path atom).</exception>
        /// <exception cref="ConvertException">If neither a string, nor a path atom, nor a relative path is given.</exception>
        /// </summary>
        public static void ExpectPathFragment(
            StringTable stringTable,
            EvaluationResult fragmentValue,
            out PathAtom pathAtom,
            out RelativePath relativePath,
            in ConversionContext context = default(ConversionContext))
        {
            Contract.Requires(stringTable != null);

            pathAtom = PathAtom.Invalid;
            relativePath = RelativePath.Invalid;
            object fragment = fragmentValue.Value;

            if (fragment is string frag)
            {
                if (!string.Equals(frag, ".", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(frag, "..", StringComparison.OrdinalIgnoreCase)
                    && PathAtom.Validate((StringSegment)frag))
                {
                    // TODO: Unfortunately absolute_path.combine(".") doesn't result in absolute_path, but absolute_path\., which is treated differently from absolute_path
                    pathAtom = new PathAtom(StringId.Create(stringTable, frag));
                    return;
                }

                if (!RelativePath.TryCreate(stringTable, frag, out relativePath))
                {
                    throw new InvalidRelativePathException(frag, context.ErrorContext);
                }
            }
            else if (fragment is PathAtom)
            {
                pathAtom = (PathAtom)fragment;
            }
            else if (fragment is RelativePath)
            {
                relativePath = (RelativePath)fragment;
            }
            else
            {
                if (!context.AllowUndefined)
                {
                    throw CreateException(new[] { typeof(string), typeof(PathAtom), typeof(RelativePath) }, fragmentValue, context);
                }
            }
        }

        /// <summary>
        /// Creates an exception for unexpected conversion.
        /// </summary>
        public static ConvertException CreateException<T>(EvaluationResult value, in ConversionContext context)
        {
            Type expectedType = typeof(T);
            return CreateException(new[] { expectedType }, value, context);
        }

        /// <summary>
        /// Throws the conversion exception.
        /// </summary>
        public static void ThrowException(Type expectedType, EvaluationResult value, int position)
        {
            throw CreateException(new[] { expectedType }, value, context: new ConversionContext(pos: position));
        }
        
        /// <summary>
        /// Throws the conversion exception.
        /// </summary>
        public static void ThrowException(Type expectedType, EvaluationResult value, in ConversionContext context)
        {
            throw CreateException(new[] { expectedType }, value, context: context);
        }

        /// <summary>
        /// Creates an exception for unexpected conversion from a collection of expected types.
        /// </summary>
        public static ConvertException CreateException(Type[] expectedTypes, EvaluationResult value, in ConversionContext context)
        {
            Contract.Requires(expectedTypes != null);
            Contract.Requires(expectedTypes.Length > 0);

            return new ConvertException(expectedTypes, value, context.ErrorContext);
        }

        /// <summary>
        /// Creates an exception for unexpected conversion with a string representation of the expected types.
        /// </summary>
        public static ConvertException CreateException(string expectedTypes, EvaluationResult value, in ConversionContext context)
        {
            Contract.Requires(!string.IsNullOrEmpty(expectedTypes));

            return new ConvertException(expectedTypes, value, context.ErrorContext);
        }


        /// <summary>
        /// Creates an exception for unexpected conversion.
        /// </summary>
        public static ConvertException UnexpectedTypeException(
            SymbolAtom propertyName,
            EvaluationResult propertyValue,
            ObjectLiteral objectLiteral,
            params Type[] expectedTypes)
        {
            Contract.Requires(expectedTypes != null);
            Contract.Requires(expectedTypes.Length != 0);

            var convContext = new ConversionContext(name: propertyName, objectCtx: objectLiteral);
            return new ConvertException(expectedTypes, propertyValue, convContext.ErrorContext);
        }

        /// <summary>
        /// Gets the corresponding DScript type from the system type.
        /// </summary>
        /// <param name="type">System type.</param>
        /// <param name="dsTypes">Types in DScript.</param>
        /// <returns>DScript type.</returns>
        public static Script.Types.Type GetType(Type type, PrimitiveTypes dsTypes)
        {
            Contract.Requires(type != null);
            Contract.Requires(dsTypes != null);

            // TODO: consider merging this function with 
            // RuntimeTypeIdExtensions.ComputeTypeOfKind
            if (type == typeof(bool))
            {
                return dsTypes.BooleanType;
            }

            if (type == typeof(string))
            {
                return dsTypes.StringType;
            }

            if (type == typeof(int))
            {
                return dsTypes.NumberType;
            }

            if (type == typeof(AbsolutePath))
            {
                return dsTypes.PathType;
            }

            if (type == typeof(FileArtifact))
            {
                return dsTypes.FileType;
            }

            if (type == typeof(PathAtom))
            {
                return dsTypes.PathAtomType;
            }

            if (type == typeof(RelativePath))
            {
                return dsTypes.RelativePathType;
            }

            if (type == typeof(StaticDirectory))
            {
                return dsTypes.StaticDirectoryType;
            }

            if (type == typeof(DirectoryArtifact))
            {
                return dsTypes.DirectoryType;
            }

            if (type == typeof(ArrayLiteral) || typeof(ArrayLiteral).IsAssignableFrom(type))
            {
                return dsTypes.ArrayType;
            }

            if (type == typeof(ObjectLiteral))
            {
                return dsTypes.ObjectType;
            }

            if (type == typeof(ModuleLiteral))
            {
                return dsTypes.ModuleType;
            }

            if (type == typeof(EnumValue))
            {
                return dsTypes.EnumType;
            }

            if (type == typeof(Closure))
            {
                return dsTypes.ClosureType;
            }

            if (type == typeof(CallableValue))
            {
                return dsTypes.AmbientType;
            }

            if (type == typeof(DsMap))
            {
                return dsTypes.MapType;
            }

            if (type == typeof(DsSet))
            {
                return dsTypes.SetType;
            }

            return dsTypes.CreateNamedTypeReference(type.Name.Replace("`", "__"));
        }
    }
}
