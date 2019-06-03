// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.Tests.TestCacheFactory+TestCacheFactoryConfiguration",
    Justification = "Tool is confused - it is constructed generically")]

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// This test class tests the functionality of the CacheFactory static class
    /// All tests in this class use a generic, cache type independent approach
    /// This class will implement the ICacheFactory interface and will use it own data structures to interact with CacheFactory
    /// </summary>
    public class TestCacheFactory : ICacheFactory
    {
        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
            => CacheConfigDataValidator.ValidateConfiguration<TestCacheFactoryConfiguration>(cacheData, cacheConfig => new Failure[] { });

        /// <summary>
        /// TestCacheConfig implements ICacheConfigData and it is used by some tests that require an actual ICacheConfigData object instance
        /// </summary>
        private class TestCacheConfig : Dictionary<string, object>, ICacheConfigData
        {
        }

        /// <summary>
        /// TestCacheFactoryConfiguration is data object that defines various cache "parameters"
        /// Since this test class implements ICacheFactory, the TestCacheFactoryConfiguration will be used to store configuration data for a generic cache
        /// that TestCacheFactory can produce.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performace", "CA1812", Justification = "Tool is confused - it is constructed")]
        private sealed class TestCacheFactoryConfiguration
        {
            /// <summary>
            /// String member with a default value (it does not have to be specified in the Json config string)
            /// </summary>
            [DefaultValue("Value_of_StringWithDefaultValue")]
            public string StringWithDefaultValue { get; set; }

            /// <summary>
            /// String member without a default value (it has to be specified in the Json config string)
            /// </summary>
            public string StringWithNoDefaultValue { get; set; }

            /// <summary>
            /// String member with property initializer (it does not have to be specified in the Json string)
            /// </summary>
            public string StringWithPropertyInitializer { get; set; } = "NotNull";

            /// <summary>
            /// Sample int value (optional in the Json string)
            /// </summary>
            [DefaultValue(1)]
            public int IntValue { get; set; }

            /// <summary>
            /// Sample bool value (optional in the Json string)
            /// </summary>
            [DefaultValue(true)]
            public bool BoolValue { get; set; }

            /// <summary>
            /// Sample float value (optional in the Json string)
            /// </summary>
            [DefaultValue(3.1415)]
            public float FloatValue { get; set; }

            /// <summary>
            /// Sample embedded ICacheConfigData object (optional in the Json string)
            /// </summary>
            [DefaultValue(null)]
            public ICacheConfigData CacheConfigValue { get; set; }
        }

        /// <summary>
        /// This lambda expression is used to validate the cache configuration data produced by CacheFactory.
        /// </summary>
        /// <remarks>
        /// Each test defines a specific lambda expression that issues a series of XAssert calls to validate the returned data.
        /// The lambda expression is called by TestCacheFactory::InitializeCache after receiving the cache configuration data
        /// from CacheFactory::Create&lt;TestCacheFactoryConfiguration&gt;.
        /// The reason why we have to validate the produced results in this complicated manner is that CacheFactory only
        /// produces a cache object and does not allow access to the configuration data that is used to create the returned cache object.
        /// XUnit always creates a separate instance of the test class for each test, therefore we do not have to be concerned with
        /// parallelism (tests will never step on each other's resultValidationLambda).
        /// </remarks>
        private static Action<TestCacheFactoryConfiguration> resultValidationLambda;

        /// <summary>
        /// This property stores the name of the assembly that contains System.Object.
        /// We use System.Object to test that the cache factory handles the case when a cache factory type name is specified
        /// that does not implement ICacheFactory.
        /// </summary>
        private static string NameOfAssemblyContainingTypeSystemObject => typeof(object).Module.Assembly.GetName().Name;

        /// <summary>
        /// Creates a cache instance from a Json data string
        /// </summary>
        /// <param name="jsonDataString">Json input data</param>
        /// <returns>Cache object or a Failure</returns>
        public Task<Possible<ICache, Failure>> InitializeCacheAsync(string jsonDataString)
        {
            return CacheFactory.InitializeCacheAsync(jsonDataString, default(Guid));
        }

        /// <summary>
        /// Creates a cache instance from a ICacheConfigData data structure
        /// </summary>
        /// <param name="cacheData">ICacheConfigData input data</param>
        /// <returns>Cache object or a Failure</returns>
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            var possibleCacheConfig = cacheData.Create<TestCacheFactoryConfiguration>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            TestCacheFactoryConfiguration cacheConfig = possibleCacheConfig.Result;

            // check if the cache configuration structure we received back is what we expect
            XAssert.IsNotNull(resultValidationLambda);
            resultValidationLambda(cacheConfig);

            // instantiate new cache - the unit tests do not need it, therefore we always return null.
            return await Task.FromResult(new Possible<ICache, Failure>((ICache)null));
        }

        /// <summary>
        /// This is a utility method that converts a Dictionary to a Json string. This method retrieves the name of the type of the class containing this test.
        /// </summary>
        /// <param name="dictionary">Dictionary to convert to Json</param>
        /// <param name="assemblyName">Optional Assembly name. When null, no Assembly entry will be added to the Json data</param>
        /// <returns>Json string containing the data from the dictionary</returns>
        private string GetJsonStringFromDictionary(Dictionary<string, object> dictionary, string assemblyName)
        {
            return GetJsonStringFromDictionary(dictionary, assemblyName, GetType().FullName);
        }

        /// <summary>
        /// This is a utility method that converts a Dictionary to a Json string. This method retrieves the name of the current assembly and type.
        /// </summary>
        /// <param name="dictionary">Dictionary to convert to Json</param>
        /// <returns>Json string containing the data from the dictionary</returns>
        private string GetJsonStringFromDictionary(Dictionary<string, object> dictionary)
        {
            return GetJsonStringFromDictionary(dictionary, System.Reflection.Assembly.GetExecutingAssembly().GetName().Name, GetType().FullName);
        }

        /// <summary>
        /// This is a utility method that converts a Dictionary to a Json string.
        /// </summary>
        /// <param name="dictionary">Dictionary to convert to Json</param>
        /// <param name="assemblyName">Optional Assembly name. When null, no Assembly entry will be added to the Json data</param>
        /// <param name="typeName">Optional Type name. When null, no Type entry will be added to the Json data</param>
        /// <returns>Json string containing the data from the dictionary</returns>
        private string GetJsonStringFromDictionary(Dictionary<string, object> dictionary, string assemblyName, string typeName)
        {
            StringBuilder sbJson = new StringBuilder();
            sbJson.AppendLine("{");

            // add assembly and type info
            if (!string.IsNullOrEmpty(assemblyName))
            {
                sbJson.AppendLine(string.Format("\"Assembly\":\"{0}\",", assemblyName));
            }

            if (!string.IsNullOrEmpty(typeName))
            {
                sbJson.AppendLine(string.Format("\"Type\":\"{0}\",", typeName));
            }

            // enumerate dictionary keys
            foreach (var key in dictionary.Keys)
            {
                if (dictionary[key].GetType() == typeof(string))
                {
                    // add string
                    sbJson.AppendLine(string.Format("\"{0}\":\"{1}\",", key, dictionary[key]));
                }
                else if (dictionary[key].GetType() == typeof(bool))
                {
                    // add bool (true/false should be lower case)
                    sbJson.AppendLine(string.Format("\"{0}\":{1},", key, dictionary[key].ToString().ToLower()));
                }
                else if (dictionary[key].GetType() == typeof(Dictionary<string, object>))
                {
                    // add ICacheConfigData
                    sbJson.AppendLine(string.Format("\"{0}\":{1},", key, GetJsonStringFromDictionary((Dictionary<string, object>)dictionary[key])));
                }
                else
                {
                    // add all other values
                    sbJson.AppendLine(string.Format("\"{0}\":{1},", key, dictionary[key]));
                }
            }

            sbJson.AppendLine("}");

            // return json
            return sbJson.ToString();
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid cache object when passing in a Json string with all the required and optional parameters
        /// </summary>
        [Fact]
        public async Task TestFullJsonString()
        {
            string jsonString = GetJsonStringFromDictionary(new Dictionary<string, object>()
                {
                    { "StringWithDefaultValue", "Value_of_StringWithDefaultValue2" },
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                    { "IntValue", 123 },
                    { "BoolValue", false },
                    { "FloatValue", 245.45 },
                    {
                        "CacheConfigValue", new Dictionary<string, object>()
                    {
                                            { "StringWithDefaultValue", "Value_of_CacheConfigValue_StringWithDefaultValue" },
                                            { "StringWithNoDefaultValue", "Value_of_CacheConfigValue_StringWithNoDefaultValue" },
                                            { "IntValue", 255 },
                                            { "BoolValue", true },
                                            { "FloatValue", 333.43 },
                                         }
                    }
                });

            // set up the result validation lambda
            resultValidationLambda = (obj1) =>
            {
                XAssert.AreEqual("Value_of_StringWithDefaultValue2", obj1.StringWithDefaultValue);
                XAssert.AreEqual("Value_of_StringWithNoDefaultValue", obj1.StringWithNoDefaultValue);
                XAssert.AreEqual(123, obj1.IntValue);
                XAssert.AreEqual(false, obj1.BoolValue);
                XAssert.AreEqual(245.45F, obj1.FloatValue);

                XAssert.AreEqual("Value_of_CacheConfigValue_StringWithDefaultValue", obj1.CacheConfigValue["StringWithDefaultValue"]);
                XAssert.AreEqual("Value_of_CacheConfigValue_StringWithNoDefaultValue", obj1.CacheConfigValue["StringWithNoDefaultValue"]);
                XAssert.AreEqual(255, Convert.ToInt32(obj1.CacheConfigValue["IntValue"]));
                XAssert.AreEqual(true, Convert.ToBoolean(obj1.CacheConfigValue["BoolValue"]));
                XAssert.AreEqual(333.43F, Convert.ToSingle(obj1.CacheConfigValue["FloatValue"]));

                // the lambda assigned to resultValidationLambda is static and each test should set it. We will set it to null to make sure that no other tests use this lambda accidentally.
                resultValidationLambda = null;
            };

            // call InitializeCache, there should be no exception
            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we get an actual cache
            XAssert.IsTrue(cache.Succeeded);
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid cache object when passing in a Json string with only the required parameters
        /// </summary>
        [Fact]
        public async Task TestMinJsonString()
        {
            string jsonString = GetJsonStringFromDictionary(new Dictionary<string, object>()
                {
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                });

            // set up the result validation lambda
            resultValidationLambda = (obj1) =>
            {
                XAssert.AreEqual("Value_of_StringWithDefaultValue", obj1.StringWithDefaultValue);
                XAssert.AreEqual("Value_of_StringWithNoDefaultValue", obj1.StringWithNoDefaultValue);
                XAssert.AreEqual(1, obj1.IntValue);
                XAssert.AreEqual(true, obj1.BoolValue);
                XAssert.AreEqual(3.1415F, obj1.FloatValue);

                XAssert.IsNull(obj1.CacheConfigValue);

                // the lambda assigned to resultValidationLambda is static and each test should set it. We will set it to null to make sure that no other tests use this lambda accidentally.
                resultValidationLambda = null;
            };

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we get an actual cache
            XAssert.IsTrue(cache.Succeeded);
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when the Json string does not include a required parameter
        /// </summary>
        [Fact]
        public async Task TestJsonStringWithMissingRequiredParameter()
        {
            // we will not specify StringWithNoDefaultValue - a required parameter that does not have a default value in TestCacheFactory
            string jsonString = GetJsonStringFromDictionary(new Dictionary<string, object>()
                {
                    { "StringWithDefaultValue", "Value_of_StringWithDefaultValue" },
                    { "IntValue", 123 },
                    { "BoolValue", false },
                    { "FloatValue", 245.45 }
                });

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            XAssert.IsFalse(cache.Succeeded);

            // validate the returned error message
            XAssert.AreEqual("BuildXL.Cache.Tests.TestCacheFactory requires a value for 'StringWithNoDefaultValue' in Json configuration data", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when the Json assigns a value with an incorrect type to parameter
        /// </summary>
        [Fact]
        public async Task TestJsonStringWithIncorrectParameterValueType()
        {
            // we will specify and string value to an int parameter
            string jsonString = GetJsonStringFromDictionary(new Dictionary<string, object>()
                {
                    { "StringWithDefaultValue", "Value_of_StringWithDefaultValue" },
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                    { "IntValue", "Invalid int value" },
                    { "BoolValue", false },
                    { "FloatValue", 245.45 }
                });

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            XAssert.IsFalse(cache.Succeeded);

            // validate the returned error message
            XAssert.AreEqual("BuildXL.Cache.Tests.TestCacheFactory Json configuration field 'IntValue' can not be set to 'Invalid int value'\nInput string was not in a correct format.", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when the Json assigns True instead of true to a boolean parameter
        /// </summary>
        [Fact]
        public async Task TestJsonStringWithIncorrectBoolParameterValueCapitalization()
        {
            // we will specify 'True' instead of the 'true' for BoolValue
            string jsonString = GetJsonStringFromDictionary(new Dictionary<string, object>()
                {
                    { "StringWithDefaultValue", "Value_of_StringWithDefaultValue" },
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                    { "IntValue", 123 },
                    { "BoolValue", true },
                    { "FloatValue", 245.45 }
                });

            jsonString = jsonString.Replace("true", "True");

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            XAssert.IsFalse(cache.Succeeded);

            // validate the returned error message
            XAssert.AreEqual("Json parser error:\nUnexpected character encountered while parsing value: T. Path 'BoolValue', line 7, position 12.", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when an invalid Json string is passed in (one that is not a Json at all)
        /// </summary>
        [Fact]
        public async Task TestInvalidJsonString()
        {
            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync("This is an invalid Json string");

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // make sure that we do not get a cache
            Assert.False(cache.Succeeded);

            // validate the returned error message
            XAssert.AreEqual("Json parser error:\nUnexpected character encountered while parsing value: T. Path '', line 0, position 0.", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid cache when a Json string contains fields that do not exist in the factory's config structure
        /// </summary>
        [Fact]
        public async Task TestInvalidJsonField()
        {
            // we will specify a field name that does not exist in TestCacheFactoryConfiguration
            string jsonString = GetJsonStringFromDictionary(new Dictionary<string, object>()
                {
                    { "InvalidFieldName", "Value_of_StringWithDefaultValue" },
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                });

            // set up the result validation lambda
            resultValidationLambda = (obj1) =>
            {
                XAssert.AreEqual("Value_of_StringWithDefaultValue", obj1.StringWithDefaultValue);

                // the lambda assigned to resultValidationLambda is static and each test should set it. We will set it to null to make sure that no other tests use this lambda accidentally.
                resultValidationLambda = null;
            };

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            Assert.True(cache.Succeeded);
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when a Json string contains an invalid Assembly name
        /// </summary>
        [Fact]
        public async Task TestInvalidAssemblyNameInJson()
        {
            // we will specify a field name that does not exist in TestCacheFactoryConfiguration
            string jsonString = GetJsonStringFromDictionary(
                new Dictionary<string, object>()
                {
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                }, "InvalidAssemblyName");

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            Assert.False(cache.Succeeded);

            // validate the returned error message
            Assert.StartsWith("InvalidAssemblyName:BuildXL.Cache.Tests.TestCacheFactory cannot be loaded or it is not a valid ICacheFactory type", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when a Json string does not contain an Assembly name
        /// </summary>
        [Fact]
        public async Task TestMissingAssemblyNameInJson()
        {
            // we will specify a field name that does not exist in TestCacheFactoryConfiguration
            string jsonString = GetJsonStringFromDictionary(
                new Dictionary<string, object>()
                {
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                }, null);

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            Assert.False(cache.Succeeded);

            // validate the returned error message
            XAssert.AreEqual("Cache factory Assembly name is required, but it was not specified", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when a Json string contains an invalid Type name (one that does not exist in the assembly)
        /// </summary>
        [Fact]
        public async Task TestInvalidTypeNameInJson()
        {
            // we will specify a field name that does not exist in TestCacheFactoryConfiguration
            string jsonString = GetJsonStringFromDictionary(
                new Dictionary<string, object>()
                {
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                }, NameOfAssemblyContainingTypeSystemObject, "InvalidTypeName");

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            Assert.False(cache.Succeeded);

            // validate the returned error message
            Assert.Contains("InvalidTypeName cannot be loaded or it is not a valid ICacheFactory type", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when a Json string contains an incorrect Type name (one that exist, but does not implement ICacheFactory)
        /// </summary>
        [Fact]
        public async Task TestIncorrectTypeNameInJson()
        {
            // we will specify a field name that does not exist in TestCacheFactoryConfiguration
            string jsonString = GetJsonStringFromDictionary(
                new Dictionary<string, object>()
                {
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                }, NameOfAssemblyContainingTypeSystemObject, "System.Object");

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            Assert.False(cache.Succeeded);
            // validate the returned error message
            Assert.Contains("System.Object cannot be loaded or it is not a valid ICacheFactory type", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that InitializeCache returns a valid Failure when a Json string does not contain an Type name
        /// </summary>
        [Fact]
        public async Task TestMissingTypeNameInJson()
        {
            // we will specify a field name that does not exist in TestCacheFactoryConfiguration
            string jsonString = GetJsonStringFromDictionary(
                new Dictionary<string, object>()
                {
                    { "StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue" },
                }, NameOfAssemblyContainingTypeSystemObject, null);

            // this Json string will produce a failure and there will be no results to validate. We will set the result validation to null here
            resultValidationLambda = null;

            // call InitializeCache, there should be no exception
            Possible<ICache, Failure> cache = await InitializeCacheAsync(jsonString);

            // make sure that we do not get a cache
            Assert.False(cache.Succeeded);

            // validate the returned error message
            Assert.Contains("Cache factory Type name is required, but it was not specified", cache.Failure.Describe());
        }

        /// <summary>
        /// Verifies that Create returns a valid Failure when a Json string does not contain an Type name
        /// </summary>
        [Fact]
        public void TestMissingTypeNameWhenCallingCreate()
        {
            ICacheConfigData data = new TestCacheConfig();
            data.Add("StringWithNoDefaultValue", "Value_of_StringWithNoDefaultValue"); // no Type added

            // call InitializeCache, there should be no exception
            Possible<TestCacheFactoryConfiguration, Failure> returnObj = data.Create<TestCacheFactoryConfiguration>();

            // make sure that we do not get a valid object back
            Assert.False(returnObj.Succeeded);

            // validate the returned error message
            Assert.Contains("Json configuration field 'Type' is missing", returnObj.Failure.Describe());
        }
    }
}
