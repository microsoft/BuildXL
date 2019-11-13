// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Reflection;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Util;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using Type = System.Type;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Makes sure all the Bits are in the right places.
    /// </summary>
    public class ConstructorTests
    {
        /// <summary>
        /// This test validates that the copy-constructor of the configuration class properly copies all the values
        /// </summary>
        [Fact]
        public void CloneConstructorWithBooleanTrue()
        {
            ValidateConfigurationCopyConstructor(booleanDefault: true);

            // Since some boolean values only have two states, it is difficult to pick a value that can be distinguished from
            // the default value. I.e. picking true would catch the default of some and would miss a copy. Same for false.
            // Other types don't have the issue since a unique value i.e., 'x:\testPath' or "testString" that distinguishes from the default value
            // is trivial for other types. Even integer 123 is okay :)
            // Therefore we are just running the code twice. Once with all booleans set to true, ones to false.
            ValidateConfigurationCopyConstructor(booleanDefault: false);
        }

        private static void ValidateConfigurationCopyConstructor(bool booleanDefault)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var initialInstance = (CommandLineConfiguration)CreateInstance(context, typeof(CommandLineConfiguration), booleanDefault);

            string from = A("x");
            string to = A("y");

            var remapper = new Remapper(new PathTable(), p => p.Replace(from, to), a => a + "_");
            var pathRemapper = new PathRemapper(
                context.PathTable,
                remapper.PathTable,
                pathStringRemapper: remapper.PathStringRemapper,
                pathAtomStringRemapper: remapper.PathAtomStringRemapper);
            var clone = new CommandLineConfiguration(initialInstance, pathRemapper);

            ValidateEqual(context, typeof(CommandLineConfiguration), initialInstance, clone, "configuration", remapper);
        }

        private static void PopulateObject(BuildXLContext context, Type type, object instance, bool booleanDefault)
        {
            XAssert.IsTrue(type.GetTypeInfo().IsClass);

            foreach (var property in type.GetTypeInfo().GetProperties())
            {
                if (!property.GetGetMethod().IsStatic)
                {
                    var newValue = CreateInstance(context, property.PropertyType, booleanDefault);
                    property.SetValue(instance, newValue);
                }
            }
        }

        private static object CreateInstance(BuildXLContext context, Type type, bool booleanDefault)
        {
            string path = A("x", "path");
            type = GetNonNullableType(type);

            if (type == typeof(bool))
            {
                return booleanDefault;
            }

            if (type == typeof(double))
            {
                return (double)0.23423;
            }

            if (type == typeof(byte))
            {
                return (byte)123;
            }

            if (type == typeof(sbyte))
            {
                return (sbyte)123;
            }

            if (type == typeof(short))
            {
                return (short)123;
            }

            if (type == typeof(ushort))
            {
                return (ushort)123;
            }

            if (type == typeof(int))
            {
                return 123;
            }

            if (type == typeof(uint))
            {
                return (uint)123;
            }

            if (type == typeof(long))
            {
                return (long)123;
            }

            if (type == typeof(ulong))
            {
                return (ulong)123;
            }

            if (type == typeof(string))
            {
                return "nonDefaultString";
            }

            if (type == typeof(ModuleId))
            {
                return ModuleId.UnsafeCreate(123);
            }

            if (type == typeof(LocationData))
            {
                return new LocationData(AbsolutePath.Create(context.PathTable, path), 12, 23);
            }

            if (type == typeof(AbsolutePath))
            {
                return AbsolutePath.Create(context.PathTable, path);
            }

            if (type == typeof(RelativePath))
            {
                string relativePath = R("rel1", "dir1", "path");
                return RelativePath.Create(context.StringTable, relativePath);
            }

            if (type == typeof(FileArtifact))
            {
                return FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, path));
            }

            if (type == typeof(PathAtom))
            {
                return PathAtom.Create(context.StringTable, "atom");
            }

            if (type == typeof(global::BuildXL.Utilities.LineInfo))
            {
                return new global::BuildXL.Utilities.LineInfo(1, 1);
            }

            if (type.GetTypeInfo().IsEnum)
            {
                bool first = true;
                foreach (var value in Enum.GetValues(type))
                {
                    if (!first)
                    {
                        return value;
                    }

                    first = false;
                }

                XAssert.Fail($"Enum {type.FullName} doesn't have more than one value, so can't pick the second one.");
            }

            if (type.GetTypeInfo().IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition();

                if (generic == typeof(IReadOnlyList<>))
                {
                    // Treat IReadOnlyList as if it was List
                    type = typeof(List<>).MakeGenericType(type.GenericTypeArguments[0]);
                    generic = type.GetGenericTypeDefinition();
                }

                if (generic == typeof(List<>))
                {
                    var newList = (IList)Activator.CreateInstance(type);
                    newList.Add(CreateInstance(context, type.GenericTypeArguments[0], booleanDefault));
                    return newList;
                }

                if (generic == typeof(IReadOnlyDictionary<,>))
                {
                    // Treat IReadOnlyList as if it was List
                    type = typeof(Dictionary<,>).MakeGenericType(type.GenericTypeArguments[0], type.GenericTypeArguments[1]);
                    generic = type.GetGenericTypeDefinition();
                }

                if (generic == typeof(Dictionary<,>))
                {
                    var newDictionary = (IDictionary)Activator.CreateInstance(type);
                    newDictionary.Add(
                        CreateInstance(context, type.GenericTypeArguments[0], booleanDefault),
                        CreateInstance(context, type.GenericTypeArguments[1], booleanDefault));
                    return newDictionary;
                }

                if (generic == typeof(ValueTuple<,>))
                {
                    var newTuple = Activator.CreateInstance(type);

                    // In ValueTuple classes, the first 7 values are accessible via Item1-Item7 fields.
                    // The tuple field names (named tuples) aren't part of runtime representation.
                    type.GetField("Item1").SetValue(newTuple, CreateInstance(context, type.GenericTypeArguments[0], booleanDefault));
                    type.GetField("Item2").SetValue(newTuple, CreateInstance(context, type.GenericTypeArguments[1], booleanDefault));

                    return newTuple;
                }
            }

            if (type.GetTypeInfo().IsInterface)
            {
                // Treat interfaces as if it was the mutable class
                type = ConfigurationConverter.FindImplementationType(
                    type,
                    ObjectLiteral.Create(new List<Binding>(), default(LineInfo), AbsolutePath.Invalid),

                    // Return a SourceResolver to instantiate
                    () => "SourceResolver");
            }

            if (type.GetTypeInfo().IsClass)
            {
                var instance = Activator.CreateInstance(type);
                PopulateObject(context, type, instance, booleanDefault);
                return instance;
            }

            XAssert.Fail($"Don't know how to create objects for this type: {type.FullName}.");
            return null;
        }

        private static Type GetNonNullableType(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type);

            if (underlyingType != null)
            {
                type = underlyingType;
            }

            return type;
        }

        public class Remapper
        {
            public readonly PathTable PathTable;
            public readonly Func<string, string> PathStringRemapper;
            public readonly Func<string, string> PathAtomStringRemapper;

            public Remapper(PathTable pathTable, Func<string, string> pathStringRemapper, Func<string, string> pathAtomStringRemapper)
            {
                Contract.Requires(pathTable != null);

                PathTable = pathTable;
                PathStringRemapper = pathStringRemapper;
                PathAtomStringRemapper = pathAtomStringRemapper;
            }
        }

        private static void ValidateEqualMembers(BuildXLContext context, Type type, object expected, object actual, string objPath, Remapper remapper)
        {
            XAssert.IsTrue(type.GetTypeInfo().IsClass);

            foreach (var property in type.GetTypeInfo().GetProperties(BindingFlags.Instance))
            {
                var expectedProperty = property.GetValue(expected);
                var actualProperty = property.GetValue(actual);
                ValidateEqual(context, property.PropertyType, expectedProperty, actualProperty, objPath + "." + property.Name, remapper);
            }
        }

        public static void ValidateEqual(BuildXLContext context, Type type, object expected, object actual, string objPath, Remapper remapper)
        {
            type = GetNonNullableType(type);

            if (type == typeof(bool))
            {
                XAssert.AreEqual((bool)expected, (bool)actual, $"{nameof(Boolean)} values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(int) || type == typeof(short) || type == typeof(sbyte) || type == typeof(long))
            {
                XAssert.AreEqual(
                    Convert.ToInt64(expected),
                    Convert.ToInt64(actual),
                    $"Numeric values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(uint) || type == typeof(ushort) || type == typeof(byte) || type == typeof(ulong))
            {
                XAssert.AreEqual(
                    Convert.ToUInt64(expected),
                    Convert.ToUInt64(actual),
                    $"Numeric values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(double))
            {
                XAssert.AreEqual(
                    Convert.ToDouble(expected),
                    Convert.ToDouble(actual),
                    $"Numeric values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(string))
            {
                XAssert.AreEqual((string)expected, (string)actual, $"{nameof(String)} values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(ModuleId))
            {
                XAssert.AreEqual(
                    (ModuleId)expected,
                    (ModuleId)actual,
                    $"{nameof(ModuleId)} id values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(LocationData))
            {
                AssertEqualLocationData(context, objPath, remapper, (LocationData)expected, (LocationData)actual);
                return;
            }

            if (type == typeof(RelativePath))
            {
                AssertEqualRelativePaths(context, objPath, remapper, (RelativePath)expected, (RelativePath)actual);
                return;
            }

            if (type == typeof(AbsolutePath))
            {
                AssertEqualAbsolutePaths(context, objPath, remapper, (AbsolutePath)expected, (AbsolutePath)actual);
                return;
            }

            if (type == typeof(FileArtifact))
            {
                AssertEqualFileArtifacts(context, objPath, remapper, (FileArtifact)expected, (FileArtifact)actual);
                return;
            }

            if (type == typeof(PathAtom))
            {
                AssertEqualPathAtoms(context, objPath, remapper, (PathAtom)expected, (PathAtom)actual);
                return;
            }

            if (type == typeof(SymbolAtom))
            {
                XAssert.AreEqual((SymbolAtom)expected, (SymbolAtom)actual, $"{nameof(SymbolAtom)} values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(global::BuildXL.Utilities.LineInfo))
            {
                XAssert.AreEqual(
                    (global::BuildXL.Utilities.LineInfo)expected,
                    (global::BuildXL.Utilities.LineInfo)actual,
                    $"{nameof(global::BuildXL.Utilities.LineInfo)} values don't match for objPath: {objPath}");
                return;
            }

            if (type == typeof(LineInfo))
            {
                XAssert.AreEqual(
                    (LineInfo)expected,
                    (LineInfo)actual,
                    $"{nameof(LineInfo)} values don't match for objPath: {objPath}");
                return;
            }

            if (type.GetTypeInfo().IsEnum)
            {
                XAssert.AreEqual((Enum)expected, (Enum)actual, $"Enum values don't match for objPath: {objPath}");
                return;
            }

            if (type.GetTypeInfo().IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition();
                if (generic == typeof(List<>) || generic == typeof(IReadOnlyList<>))
                {
                    XAssert.IsTrue((expected == null) == (actual == null));

                    var expectedList = expected as IList;
                    var actualList = actual as IList;

                    XAssert.IsTrue(
                        (expectedList == null) == (actualList == null),
                        "One of the lists is null, the other isn't for objPath: {0}",
                        objPath);
                    if (expectedList != null)
                    {
                        XAssert.AreEqual(expectedList.Count, actualList.Count, $"Counts of lists don't match for objPath: {objPath}");
                        for (int i = 0; i < expectedList.Count; i++)
                        {
                            ValidateEqual(
                                context,
                                type.GenericTypeArguments[0],
                                expectedList[i],
                                actualList[i],
                                objPath + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                                remapper);
                        }
                    }

                    return;
                }

                if (generic == typeof(Dictionary<,>) || generic == typeof(IReadOnlyDictionary<,>) || generic == typeof(ConcurrentDictionary<,>))
                {
                    XAssert.IsTrue((expected == null) == (actual == null));

                    var expectedDictionary = expected as IDictionary;
                    var actualDictionary = actual as IDictionary;

                    XAssert.IsTrue(
                        (expectedDictionary == null) == (actualDictionary == null),
                        $"One of the dictionaries is null, the other isn't for objPath: {objPath}");
                    if (expectedDictionary != null)
                    {
                        XAssert.AreEqual(
                            expectedDictionary.Count,
                            expectedDictionary.Count,
                            $"Counts of dictionaries don't match for objPath: {objPath}");
                        foreach (var kv in expectedDictionary)
                        {
                            var key = kv.GetType().GetProperty("Key").GetValue(kv);
                            var value = kv.GetType().GetTypeInfo().GetProperty("Value").GetValue(kv);
                            var actualValue = actualDictionary[key];

                            ValidateEqual(context, type.GenericTypeArguments[1], value, actualValue, objPath + "[" + key + "]", remapper);
                        }
                    }

                    return;
                }
            }

            if (type.GetTypeInfo().IsInterface)
            {
                var actualType = ConfigurationConverter.FindImplementationType(
                    type,
                    ObjectLiteral.Create(new List<Binding>(), default(LineInfo), AbsolutePath.Invalid),

                    // Note we only create a sourceresolver, so no need to fiddle, just compare with SourceResolver.
                    () => "SourceResolver");
                ValidateEqualMembers(context, actualType, expected, actual, objPath, remapper);
                return;
            }

            if (type.GetTypeInfo().IsClass)
            {
                ValidateEqualMembers(context, type, expected, actual, objPath, remapper);
                return;
            }

            XAssert.Fail($"Don't know how to compare objects of this type '{type}' for objPath: {objPath}");
        }

        private static void AssertEqualFileArtifacts(
            BuildXLContext context,
            string objPath,
            Remapper remapper,
            FileArtifact expectedFile,
            FileArtifact actualFile)
        {
            if (remapper != null)
            {
                AssertEqualAbsolutePaths(context, objPath, remapper, expectedFile.Path, actualFile.Path);
                XAssert.AreEqual(
                    expectedFile.RewriteCount,
                    actualFile.RewriteCount,
                    $"{nameof(FileArtifact)} values don't match for objPath: {objPath}");
            }
            else
            {
                XAssert.AreEqual(expectedFile, actualFile, $"{nameof(FileArtifact)} values don't match for objPath: {objPath}");
            }
        }

        private static void AssertEqualPathAtoms(BuildXLContext context, string objPath, Remapper remapper, PathAtom expectedAtom, PathAtom actualAtom)
        {
            if (remapper?.PathAtomStringRemapper != null)
            {
                var expectedAtomString = remapper.PathAtomStringRemapper(expectedAtom.ToString(context.StringTable));
                XAssert.AreEqual(
                    expectedAtomString,
                    actualAtom.ToString(remapper.PathTable.StringTable),
                    $"{nameof(PathAtom)} values don't match for objPath: {objPath}");
            }
            else
            {
                XAssert.AreEqual(expectedAtom, actualAtom, $"{nameof(PathAtom)} values don't match for objPath: {objPath}");
            }
        }

        private static void AssertEqualAbsolutePaths(BuildXLContext context, string objPath, Remapper remapper, AbsolutePath expectedPath, AbsolutePath actualPath)
        {
            if (remapper?.PathStringRemapper != null)
            {
                var expectedPathString = remapper.PathStringRemapper(expectedPath.ToString(context.PathTable));
                XAssert.AreEqual(
                    expectedPathString,
                    actualPath.ToString(remapper.PathTable),
                    $"{nameof(AbsolutePath)} values don't match for objPath: {objPath}");
            }
            else
            {
                XAssert.AreEqual(expectedPath, actualPath, $"{nameof(AbsolutePath)} values don't match for objPath: {objPath}");
            }
        }

        private static void AssertEqualRelativePaths(
            BuildXLContext context,
            string objPath,
            Remapper remapper,
            RelativePath expectedPath,
            RelativePath actualPath)
        {
            if (remapper?.PathAtomStringRemapper != null)
            {
                var expectedAtoms = expectedPath.GetAtoms();
                var actualAtoms = actualPath.GetAtoms();

                XAssert.AreEqual(
                    expectedAtoms.Length,
                    actualAtoms.Length,
                    $"{nameof(AbsolutePath)} values don't match for objPath: {objPath}");

                for (int i = 0; i < expectedAtoms.Length; ++i)
                {
                    AssertEqualPathAtoms(context, objPath, remapper, expectedAtoms[i], actualAtoms[i]);
                }
            }
            else
            {
                XAssert.AreEqual(expectedPath, actualPath, $"{nameof(RelativePath)} values don't match for objPath: {objPath}");
            }
        }

        private static void AssertEqualLocationData(
            BuildXLContext context,
            string objPath,
            Remapper remapper,
            LocationData expectedLocationData,
            LocationData actualLocationData)
        {
            if (remapper?.PathStringRemapper != null)
            {
                AssertEqualAbsolutePaths(context, objPath, remapper, expectedLocationData.Path, actualLocationData.Path);
                XAssert.AreEqual(expectedLocationData.Line, actualLocationData.Line, $"{nameof(LocationData)} values don't match for objPath: {objPath}");
                XAssert.AreEqual(expectedLocationData.Position, actualLocationData.Position, $"{nameof(LocationData)} values don't match for objPath: {objPath}");
            }
            else
            {
                XAssert.AreEqual(expectedLocationData, actualLocationData, $"{nameof(LocationData)} values don't match for objPath: {objPath}");
            }
        }
    }
}
