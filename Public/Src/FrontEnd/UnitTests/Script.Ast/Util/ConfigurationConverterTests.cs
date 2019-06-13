// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Sdk;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Utilities.FormattableStringEx;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace Test.DScript.Util
{
    public class ConfigurationConverterTests
    {
        private static readonly ObjectLiteral s_emptyObjectLiteral = ObjectLiteral0.SingletonWithoutProvenance;
        private readonly CommandLineConfiguration m_defaultConf;

        private readonly FrontEndContext m_context;

        public ConfigurationConverterTests()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
            m_defaultConf = new CommandLineConfiguration();
        }

        /// <summary>
        /// Precondition fail if string table is not passed.
        /// </summary>
        [Fact(Skip = "Test contract require")]
        public void TestInvalidArgTable()
        {
            Assert.ThrowsAny<Exception>(() =>
                ConfigurationConverter.ConvertObjectLiteralToConfiguration(null, s_emptyObjectLiteral));
        }

        /// <summary>
        /// Precondition fail if object literal is not passed.
        /// </summary>
        [Fact(Skip = "Test contract require")]
        public void TestInvalidArgObjectLiteral()
        {
            Assert.ThrowsAny<Exception>(() =>
                ConfigurationConverter.ConvertObjectLiteralToConfiguration(m_context, null));
        }

        /// <summary>
        /// Convert empty object literal given empty string table, both when
        /// (1) Configuration, and (2) IConfiguration is specified as return type.
        /// </summary>
        [Fact]
        public void TestValidEmptyObjectLiteral()
        {
            MyEqual(ConfigurationConverter.Convert<ConfigurationImpl>(m_context, s_emptyObjectLiteral), m_defaultConf);
            MyEqual(ConfigurationConverter.Convert<IConfiguration>(m_context, s_emptyObjectLiteral), m_defaultConf);
        }

        [Fact]
        public void TestInvalidEnumMemberConversion()
        {
            Assert.Throws<ConversionException>(() =>
                ConfigurationConverter.Convert<MyClassWithEnum>(m_context, CreateObject("member", "enum.member")));
        }

        /// <summary>
        /// Configuration object with only one IValue field set to a non-null value.
        /// </summary>
        [Fact]
        public void TestValidSimple()
        {
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject(
                    "sandbox",
                    CreateObject("breakOnUnexpectedFileAccess", true)));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "Sandbox");
            AssertAlmostEqual(
                new SandboxConfiguration(),
                conf.Sandbox,
                "BreakOnUnexpectedFileAccess");
            XAssert.IsTrue(conf.Sandbox.BreakOnUnexpectedFileAccess);
        }

        /// <summary>
        /// Configuration object with two IValue fields set to non-null values
        /// </summary>
        [Fact]
        public void TestValidLessSimple()
        {
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject(
                    "cache", CreateObject(
                        "cacheGraph", true,
                        "cacheSpecs", SpecCachingOption.Enabled),
                    "schedule", CreateObject("stopOnFirstError", false),
                    "sandbox", CreateObject("defaultTimeout", 1000)));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "Cache", "Schedule", "Engine", "Sandbox");
            AssertAlmostEqual(
                new CacheConfiguration
                {
                    CacheGraph = true,
                    CacheSpecs = SpecCachingOption.Enabled
                },
                conf.Cache,
                "CacheGraph", "CacheSpec");
            AssertAlmostEqual(
                new ScheduleConfiguration
                {
                    StopOnFirstError = false,
                },
                conf.Schedule,
                "DefaultTimeout");
            XAssert.IsTrue(conf.Cache.CacheGraph);
            XAssert.AreEqual(SpecCachingOption.Enabled, conf.Cache.CacheSpecs);
            XAssert.IsFalse(conf.Schedule.StopOnFirstError);
            XAssert.AreEqual(1000, conf.Sandbox.DefaultTimeout);
        }

        /// <summary>
        /// Configuration object with one enum field set to a valid enum constant.
        /// </summary>
        [Fact]
        public void TestValidEnum()
        {
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject("logging", CreateObject("diagnostic", DiagnosticLevels.Engine)));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "Logging");
            AssertAlmostEqual(
                new LoggingConfiguration(),
                conf.Logging,
                "Diagnostic");
            XAssert.AreEqual(DiagnosticLevels.Engine, conf.Logging.Diagnostic);
        }

        /// <summary>
        /// Configuration object with only one List&lt;string&gt; field set to empty list.
        /// </summary>
        [Fact]
        public void TestValidEmptyListOfStrings()
        {
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject("allowedEnvironmentVariables", new ObjectLiteral[0]));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "AllowedEnvironmentVariables");
            XAssert.IsNotNull(conf.AllowedEnvironmentVariables);
            XAssert.AreEqual(0, conf.AllowedEnvironmentVariables.Count);
        }

        /// <summary>
        /// Configuration object with only one List&lt;? extends IValue&gt; field set to null.
        /// </summary>
        [Fact]
        public void TestValidNullListOfObjectLiterals()
        {
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject("resolvers", null));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "Resolvers");
            XAssert.AreEqual(null, conf.Resolvers);
        }

        /// <summary>
        /// Configuration object with only one List&lt;string&gt; field set to an array of strings.
        /// </summary>
        [Fact]
        public void TestValidListOfStrings()
        {
            var envVarStrings = new[] { "var1", "var2" };
            foreach (var envVarList in GetDifferentListRepresenations(envVarStrings))
            {
                IConfiguration conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                    m_context,
                    CreateObject("allowedEnvironmentVariables", envVarList));

                XAssert.IsNotNull(conf);
                AssertAlmostEqualToDefaultConfigurationObject(conf, "AllowedEnvironmentVariables");
                XAssert.IsTrue(MyEqual(envVarStrings, conf.AllowedEnvironmentVariables.ToArray()));
            }
        }

        /// <summary>
        /// Configuration object with only one List&lt;int&gt; field set to an array of ints.
        /// </summary>
        [Fact]
        public void TestValidListOfInts()
        {
            var noLogInts = new[] { 1, 2 };
            foreach (var noLogList in GetDifferentListRepresenations(noLogInts))
            {
                var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                    m_context,
                    CreateObject("logging", CreateObject("noLog", noLogList)));

                XAssert.IsNotNull(conf);
                AssertAlmostEqualToDefaultConfigurationObject(conf, "Logging");

                XAssert.AreEqual(noLogInts.Length, conf.Logging.NoLog.Count);
                XAssert.AreEqual(noLogInts[0], conf.Logging.NoLog[0]);
                XAssert.AreEqual(noLogInts[1], conf.Logging.NoLog[1]);
            }
        }

        /// <summary>
        /// Configuration object with only one List&lt;string&gt; field set to an array of object literals.
        /// </summary>
        [Fact]
        public void TestInvalidListOfStrings()
        {
            var resolvers = new ObjectLiteral[] { CreateObject("priority", 1), CreateObject("type", "1") };
            foreach (var resolversList in GetDifferentListRepresenations(resolvers))
            {
                try
                {
                    ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                        m_context,
                        CreateObject("allowedEnvironmentVariables", resolversList));
                    XAssert.Fail("Expected to fail with ConversionException when trying to set ObjectLiteral[] to List<string>");
                }
                catch (ConversionException)
                {
                    // OK, as expected
                }
            }
        }

        /// <summary>
        /// Configuration object with only one Map&lt;string, ? extends IValue&gt;
        /// field set to an ObjectLiteral representing string->string map.
        /// </summary>
        [Fact]
        public void TestInvalidMapOfStringToIValue()
        {
            Assert.Throws<ConversionException>(() =>
                ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                    m_context,
                    CreateObject(
                        "mounts", CreateObject(
                            "mount1", "str1",
                            "mount2", "str2"))));
        }

        [Fact]
        public void TestArrayLiteralDoesNotConvertToObjectLiteral()
        {
            Assert.Throws<ConversionException>(() =>
                ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                    m_context,
                    CreateObject(
                        "mounts",
                        CreateArray("1", "2", "3"))));
        }

        [Fact]
        public void TestMapConversion()
        {
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                    m_context,
                    CreateObject(
                        "resolvers",
                        CreateArray(
                            CreateObject(
                                "kind", "MsBuild",
                                "root", AbsolutePath.Invalid,
                                "environment", CreateMap(new KeyValuePair<object, object>[] {
                                    new KeyValuePair<object, object>("1", "hi"),
                                    new KeyValuePair<object, object>("2", "bye") }
                                )
                            )
                        )
                    )
                );
            var resolver = conf.Resolvers.Single();
            var msBuildResolver = resolver as MsBuildResolverSettings;
            XAssert.IsNotNull(msBuildResolver);

            XAssert.AreEqual("hi", msBuildResolver.Environment["1"].GetValue());
            XAssert.AreEqual("bye", msBuildResolver.Environment["2"].GetValue());
        }

        /// <summary>
        /// Configuration object with only one List&lt;? extends IValue&gt; field set to an array of object literals.
        /// </summary>
        [Fact]
        public void TestValidListOfObjectLiterals()
        {
            var resolvers = new ObjectLiteral[]
                            {
                                CreateObject("kind", "SourceResolver"),
                                CreateObject("kind", "SourceResolver")
                            };

            // lazy because in case there is a _bug_ in the converter, we don't want this auxiliary conversion
            // to fail first, before the actual unit test (inside the for loop)
            var myConvertedResolversArrayLazy = Lazy.Create(() =>
                resolvers.Select(r => ConfigurationConverter.Convert<IResolverSettings>(m_context, r)).ToArray());

            foreach (var resolversList in GetDifferentListRepresenations(resolvers))
            {
                IConfiguration conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                    m_context,
                    CreateObject("resolvers", resolversList));

                XAssert.IsNotNull(conf);
                AssertAlmostEqualToDefaultConfigurationObject(conf, "Resolvers");
                var confResolversArray = conf.Resolvers.ToArray();

                XAssert.IsTrue(MyEqual(myConvertedResolversArrayLazy.Value, confResolversArray));
            }
        }

        /// <summary>
        /// Configuration object with only one List&lt;? extends IValue&gt; field set to an array of strings.
        /// </summary>
        [Fact]
        public void TestInvalidListOfObjectLiterals()
        {
            var resolvers = new[] { "hi" };
            foreach (var resolversList in GetDifferentListRepresenations(resolvers))
            {
                try
                {
                    ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                        m_context,
                        CreateObject("resolvers", resolversList));
                    XAssert.Fail("Expected to fail with ConversionException when trying to set string[] to List<IResolver>");
                }
                catch (ConversionException)
                {
                    // OK, as expected
                }
            }
        }

        /// <summary>
        /// Configuration object with only one List&lt;? extends IValue&gt; field set to an array of object literals, but invalid resolvers.
        /// </summary>
        [Fact(Skip = "Skip")]
        public void TestValidListOfObjectLiteralsButInvalidResolvers()
        {
            var resolvers = new ObjectLiteral[] { CreateObject("priority", 2, "kind", "UnknownResolver") };
            foreach (var resolversList in GetDifferentListRepresenations(resolvers))
            {
                try
                {
                    ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                        m_context,
                        CreateObject("resolvers", resolversList));
                    XAssert.Fail("Expected to fail with ConversionException when trying to set string[] to List<IResolver>");
                }
                catch (ConversionException)
                {
                    // OK, as expected
                }
            }
        }

        /// <summary>
        /// Configuration object with only one AbsolutePath field set to a valid path string
        /// </summary>
        [Fact]
        public void TestValidPathAsString()
        {
            string PathStr = A("x", "temp");
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject(
                    "cache", CreateObject(
                        "cacheLogFilePath", PathStr)));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "Cache");
            AssertAlmostEqual(
                new CacheConfiguration(),
                conf.Cache,
                "CacheLogFilePath");
            XAssert.AreEqual(PathStr, conf.Cache.CacheLogFilePath.ToString(m_context.PathTable));
        }

        /// <summary>
        /// Configuration object with only one AbsolutePath field set to an invalid path string
        /// </summary>
        [Fact]
        public void TestInvalidPathAsString()
        {
            Assert.Throws<ConversionException>(() =>
                {
                    const string PathStr = "test";
                    ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                        m_context,
                        CreateObject(
                            "cache", CreateObject(
                                "cacheLogFilePath", PathStr)));
                });
        }

        /// <summary>
        /// Configuration object with only one List&lt;AbsolutePath&gt; field set to
        /// an array containing at least one invalid path string.
        /// </summary>
        [Fact]
        public void TestInvalidListOfPaths()
        {
            string PathStr0 = A("x", "temp", "file1.txt");
            string PathStr1 = "file2.txt";
            foreach (var configImportsList in GetDifferentListRepresenations(new[] { PathStr0, PathStr1 }))
            {
                try
                {
                    ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                        m_context,
                        CreateObject(
                            "startup",
                            CreateObject("additionalConfigFiles", configImportsList)));
                    XAssert.Fail("Expected to fail with ConversionException when trying to set string[] containing invalid path strings to List<Path>");
                }
                catch (ConversionException)
                {
                    // OK, as expected
                }
            }
        }

        /// <summary>
        /// Configuration object with only one Map&lt;string, AbsolutePath&gt; field set to a valid map
        /// </summary>
        [Fact]
        public void TestValidMapOfPaths()
        {
            var Path0 = AbsolutePath.Create(m_context.PathTable, A("x", "temp", "file1.txt"));
            var Path1 = AbsolutePath.Create(m_context.PathTable, A("x", "temp", "file2.txt"));
            var conf = ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                m_context,
                CreateObject(
                    "engine", CreateObject(
                        "rootMap", CreateObject("path0", Path0,
                                             "path1", Path1))));

            XAssert.IsNotNull(conf);
            AssertAlmostEqualToDefaultConfigurationObject(conf, "Engine");
            XAssert.AreEqual(2, conf.Engine.RootMap.Count);
            XAssert.AreEqual(Path0, conf.Engine.RootMap["path0"]);
            XAssert.AreEqual(Path1, conf.Engine.RootMap["path1"]);
        }

        /// <summary>
        /// Configuration object with only one Map&lt;string, AbsolutePath&gt; field set to
        /// a map containing at least one invalid path string.
        /// </summary>
        [Fact]
        public void TestInvalidMapOfPaths()
        {
            var Path0 = AbsolutePath.Create(m_context.PathTable, A("x", "temp", "file1.txt"));
            string Path1 = "file2.txt";
            Assert.Throws<ConversionException>(() =>
                ConfigurationConverter.ConvertObjectLiteralToConfiguration(
                   m_context,
                    CreateObject(
                        "engine", CreateObject(
                            "rootMap", CreateObject("path0", Path0,
                                                 "path1", Path1)))));
        }

        [Fact]
        public void TestMergeValueTypeForDebugScript()
        {
            var confLit = CreateObject("frontEnd", CreateObject("debugScript", true));
            var conf = ConfigurationConverter.AugmentConfigurationWith(
                m_context,
                new CommandLineConfiguration(),
                confLit);

            XAssert.IsNotNull(conf);
            XAssert.AreEqual(true, conf.FrontEnd.DebugScript);
        }

        private static IEnumerable<object> GetDifferentListRepresenations<T>(IReadOnlyList<T> array)
        {
            var objectArray = new object[array.Count];
            for (var i = 0; i < objectArray.Length; i++)
            {
                objectArray[i] = array[i];
            }

            return new List<object>
                         {
                             array,
                             ArrayLiteral.CreateWithoutCopy(objectArray.Select(e => EvaluationResult.Create(e)).ToArray(), default(LineInfo), AbsolutePath.Invalid)
                         };
        }

        private static bool MyEqual(object expected, object actual)
        {
            if (expected == null)
            {
                return actual == null;
            }

            // C# arrays/lists
            var expectedAsList = expected as IList;
            var actualAsList = actual as IList;
            if (expectedAsList != null && actualAsList != null)
            {
                if (expectedAsList.Count != actualAsList.Count)
                {
                    return false;
                }

                for (int i = 0; i < expectedAsList.Count; i++)
                {
                    if (!MyEqual(expectedAsList[i], actualAsList[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // C# dictionaries
            var expectedAsDictionary = expected as IDictionary;
            var actualAsDictionary = actual as IDictionary;
            if (expectedAsDictionary != null && actualAsDictionary != null)
            {
                if (expectedAsDictionary.Count != actualAsDictionary.Count)
                {
                    return false;
                }

                foreach (var key in expectedAsDictionary.Keys)
                {
                    if (!MyEqual(expectedAsDictionary[key], actualAsDictionary[key]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // Primitives
            if (expected is IFormattable || expected is string)
            {
                var result = expected.Equals(actual);
                return result;
            }

            // objects
            var expectedAsIValue = expected as object;
            if (expectedAsIValue != null)
            {
                return MyAlmostEqual(expectedAsIValue, actual as object);
            }

            // everything else
            return false;
        }

        private static bool MyAlmostEqual(object expected, object actual, params string[] propertyNamesToExclude)
        {
            IEnumerable<string> props;
            return MyAlmostEqual(expected, actual, out props, propertyNamesToExclude);
        }

        private static bool MyAlmostEqual(object expected, object actual, out IEnumerable<string> mismatchedProperties, params string[] propertyNamesToExclude)
        {
            var myMismatchedProperties = new List<string>();
            var exceptions = new HashSet<string>(propertyNamesToExclude);
            var type = expected.GetType();
            foreach (var property in type.GetTypeInfo().GetProperties())
            {
                var propertyName = property.Name;
                if (exceptions.Contains(propertyName))
                {
                    continue;
                }

                if (!MyEqual(property.GetValue(expected), property.GetValue(actual)))
                {
                    myMismatchedProperties.Add(propertyName);
                }
            }

            mismatchedProperties = myMismatchedProperties;
            return !myMismatchedProperties.Any();
        }

        private void AssertAlmostEqualToDefaultConfigurationObject(IConfiguration conf, params string[] exceptions)
        {
            XAssert.IsNotNull(conf);
            AssertAlmostEqual(conf, m_defaultConf, exceptions);
        }

        /// <summary>
        /// Uses reflection to fetch all accessible properties and checks if their values
        /// in the two objects match (except for the listed exceptions)
        /// </summary>
        private static void AssertAlmostEqual<T>(T expected, T actual, params string[] propertyNamesToExclude)
        {
            IEnumerable<string> mismatchedProperties;
            var equal = MyAlmostEqual(expected, actual, out mismatchedProperties, propertyNamesToExclude);
            XAssert.IsTrue(equal, I($"The following properties are different: {string.Join(", ", mismatchedProperties)}"));
        }

        private StringId CreateString(string name)
        {
            return StringId.Create(m_context.StringTable, name);
        }

        private ObjectLiteral CreateObject(string name, object value)
        {
            return ObjectLiteral.Create(new List<Binding> { new Binding(CreateString(name), value ?? UndefinedValue.Instance, location: default(LineInfo)) }, default(LineInfo), AbsolutePath.Invalid);
        }

        private ObjectLiteral CreateObject(string name1, object value1, string name2, object value2)
        {
            return
                ObjectLiteral.Create(new List<Binding> { new Binding(CreateString(name1), value1, location: default(LineInfo)), new Binding(CreateString(name2), value2, location: default(LineInfo)) }, default(LineInfo), AbsolutePath.Invalid);
        }

        private ObjectLiteral CreateObject(string name1, object value1, string name2, object value2, string name3, object value3)
        {
            return
                ObjectLiteral.Create(
                    new List<Binding>
                    {
                        new Binding(CreateString(name1), value1, location: default(LineInfo)),
                        new Binding(CreateString(name2), value2, location: default(LineInfo)),
                        new Binding(CreateString(name3), value3, location: default(LineInfo))
                    }, default(LineInfo), AbsolutePath.Invalid);
        }

        private ArrayLiteral CreateArray(params object[] elements)
        {
            return ArrayLiteral.CreateWithoutCopy(elements.Select(e => EvaluationResult.Create(e)).ToArray(), default(LineInfo), AbsolutePath.Invalid);
        }

        private OrderedMap CreateMap(IEnumerable<KeyValuePair<object, object>> elements)
        {
            var result = OrderedMap.Empty;
            foreach(var pair in elements)
            {
                result = result.Add(EvaluationResult.Create(pair.Key), EvaluationResult.Create(pair.Value));
            }
            return result;
        }

        internal enum MyEnum
        {
            One
        }

        internal class MyClassWithEnum
        {
            public MyEnum Member { get; set; }
        }
    }
}
