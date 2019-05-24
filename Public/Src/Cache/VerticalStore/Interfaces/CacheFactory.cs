// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Static utility class that implements methods that other cache factories can use to instantiate cache objects
    /// </summary>
    public static class CacheFactory
    {
        /// <summary>
        /// String that holds the dictionary key for the cache factory assembly name
        /// </summary>
        private const string DictionaryKeyFactoryAssemblyName = "Assembly";

        /// <summary>
        /// String that holds the dictionary key for the cache factory type name
        /// </summary>
        private const string DictionaryKeyFactoryTypeName = "Type";

        /// <summary>
        /// The regex that cache IDs must match
        /// </summary>
        private const string CacheIdRegex = @"^[a-zA-Z0-9_]{2,50}$";

        /// <summary>
        /// Construct a cache object from Json data without activity ID
        /// </summary>
        /// <param name="config">Json cache configuration data string</param>
        /// <returns>Possible ICache or error</returns>
        /// <remarks>
        /// This is here mainly for compatibility with dynamic invocation from scripts
        /// such as the GC script or other such tools.  We really want an activity ID
        /// to be included for most cache constructions
        /// </remarks>
        public static Task<Possible<ICache, Failure>> InitializeCacheAsync(string config)
        {
            return InitializeCacheAsync(config, default(Guid));
        }

        /// <summary>
        /// Creates a cache object from Json data
        /// It loads cache factory assemblies and calls the right cache factory
        /// </summary>
        /// <param name="config">Json cache configuration data string</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        public static async Task<Possible<ICache, Failure>> InitializeCacheAsync(string config, Guid activityId)
        {
            ICacheConfigData cacheData;
            Exception exception;
            if (!TryCreateCacheConfigData(config, out cacheData, out exception))
            {
                return new IncorrectJsonConfigDataFailure("Json parser error:\n{0}", exception?.Message ?? "Unknown");
            }

            // create cache instance
            return await InitializeCacheAsync(cacheData, activityId);
        }

        /// <summary>
        /// Creates cache config data from JSON string.
        /// </summary>
        /// <param name="config">JSON string representing cache config</param>
        /// <param name="cacheData">Output cache data</param>
        /// <param name="exception">Output exception if creation failed</param>
        /// <returns>True if creation is successful</returns>
        public static bool TryCreateCacheConfigData(string config, out ICacheConfigData cacheData, out Exception exception)
        {
            cacheData = null;
            exception = null;

            try
            {
                // convert the Json data to CacheConfigData
                cacheData = JsonConvert.DeserializeObject<ICacheConfigData>(config, new CacheJsonDataConverter());
                return true;
            }
            catch (Exception e)
            {
                exception = e;
                return false;
            }
        }

        /// <summary>
        /// Serialize ICacheConfigData into a string
        /// </summary>
        /// <param name="cacheData">The ICacheConfigData to serialize</param>
        /// <returns>Json string that represents the ICacheConfigData</returns>
        public static string Serialize(this ICacheConfigData cacheData)
        {
            return JsonConvert.SerializeObject(cacheData, Formatting.None);
        }

        /// <summary>
        /// Creates a cache object from ICacheConfigData
        /// It loads cache factory assemblies and calls the right cache factory
        /// </summary>
        /// <param name="cacheData">The cache config data to be passed to the factory</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        public static async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            Contract.Requires(cacheData != null);

            object value;
            if (!cacheData.TryGetValue(DictionaryKeyFactoryAssemblyName, out value))
            {
                return new IncorrectJsonConfigDataFailure("Cache factory Assembly name is required, but it was not specified");
            }

            string assembly = value.ToString();

            if (!cacheData.TryGetValue(DictionaryKeyFactoryTypeName, out value))
            {
                return new IncorrectJsonConfigDataFailure("Cache factory Type name is required, but it was not specified");
            }

            string type = value.ToString();

            ICacheFactory factoryObject;
            Exception instantiationException = null;
            try
            {
                Assembly assemblyFile = Assembly.Load(assembly);
                Type myType = assemblyFile.GetType(type);
                if (myType == null)
                {
                    throw new ArgumentException($"Typename {type} could not be found in {assembly}");
                }
                factoryObject = Activator.CreateInstance(myType) as ICacheFactory;

                // instantiate cache factory object
                // factoryObject = Activator.CreateInstance(assembly, type).Unwrap() as ICacheFactory;
            }
            catch (Exception ex)
            {
                // We failed to produce an instance of the specified type. We will return a Failure from the next if statement
                factoryObject = null;
                instantiationException = ex;
            }

            // Build error message for failed ICacheFactory construction 
            if (factoryObject == null)
            {
                string message = $"{assembly}:{type} cannot be loaded or it is not a valid {nameof(ICacheFactory)} type";
                if (instantiationException != null)
                {
                    message += $". Searched for {assembly} in {Path.GetFullPath(".")}. Exception: {instantiationException}";
                }
                return new IncorrectJsonConfigDataFailure(message);
            }

            // call the loaded cache factory and create new cache object
            return await factoryObject.InitializeCacheAsync(cacheData, activityId);
        }

        /// <summary>
        /// Validates that the config data is valid.
        /// It loads cache factory assemblies and calls the right validation method.
        /// </summary>
        public static IEnumerable<Failure> ValidateConfig(ICacheConfigData cacheData)
        {
            Contract.Requires(cacheData != null);

            var failures = new List<Failure>();
            object value;
            if (!cacheData.TryGetValue(DictionaryKeyFactoryAssemblyName, out value))
            {
                failures.Add(new IncorrectJsonConfigDataFailure("Cache factory Assembly name is required, but it was not specified"));
            }

            string assembly = value.ToString();

            if (!cacheData.TryGetValue(DictionaryKeyFactoryTypeName, out value))
            {
                failures.Add(new IncorrectJsonConfigDataFailure("Cache factory Type name is required, but it was not specified"));
            }

            if (failures.Any())
            {
                return failures;
            }

            string type = value.ToString();

            ICacheFactory factoryObject;
            Exception instantiationException = null;
            try
            {
                Assembly assemblyFile = Assembly.Load(assembly);
                Type myType = assemblyFile.GetType(type);
                if (myType == null)
                {
                    throw new ArgumentException($"Typename {type} could not be found in {assembly}");
                }
                factoryObject = Activator.CreateInstance(myType) as ICacheFactory;
            }
            catch (Exception ex)
            {
                // We failed to produce an instance of the specified type. We will return a Failure from the next if statement
                factoryObject = null;
                instantiationException = ex;
            }

            // Make sure that the cache factory is an ICacheFactory.
            if (factoryObject == null)
            {
                string message = $"{assembly}:{type} cannot be loaded or it is not a valid ICacheFactory type";
                if (instantiationException != null)
                {
                    message += $". Searched for {assembly} in {Path.GetFullPath(".")}. Exception: {instantiationException}";
                }
                return new[] { new IncorrectJsonConfigDataFailure(message) };
            }

            // call the loaded cache factory and create new cache object
            return factoryObject.ValidateConfiguration(cacheData);
        }

        /// <summary>
        /// Create the given data object based on the ICacheConfigData
        /// </summary>
        /// <typeparam name="T">Type of the data object to construct</typeparam>
        /// <param name="cacheData">The ICache data object to use</param>
        /// <returns>A new data object already filled in or a failure due to missing or invalid values</returns>
        /// <remarks>
        /// Enforces the contract that all given config items map to the T (no unknown values) other than the two factory values
        /// Enforces the contract that CacheId field both exists and that its value is constrained to a valid cache ID.
        /// </remarks>
        public static Possible<T, Failure> Create<T>(this ICacheConfigData cacheData) where T : class, new()
        {
            Contract.Requires(cacheData != null);

            object value;
            if (!cacheData.TryGetValue(DictionaryKeyFactoryTypeName, out value))
            {
                return new IncorrectJsonConfigDataFailure("Json configuration field '{0}' is missing", DictionaryKeyFactoryTypeName);
            }

            string configName = value.ToString();
            var cacheConfigConversion = cacheData.ConvertTo(typeof(T), configName);

            if (!cacheConfigConversion.Succeeded)
            {
                return new IncorrectJsonConfigDataFailure(cacheConfigConversion.Failure.DescribeIncludingInnerFailures());
            }

            var cacheConfig = (T)cacheConfigConversion.Result;
            var cacheIdProperty = cacheConfig.GetType().GetProperties().FirstOrDefault(p => string.Equals("CacheId", p.Name));

            if (cacheIdProperty != null)
            {
                var cacheId = cacheIdProperty.GetValue(cacheConfig) as string;
                if (cacheId == null)
                {
                    return new IncorrectJsonConfigDataFailure("{0} requires a non-null value for '{1}' in Json configuration data", configName, cacheIdProperty.Name);
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(cacheId, CacheIdRegex))
                {
                    return
                        new IncorrectJsonConfigDataFailure(
                            "{0} of '{1}' does not meet the required naming pattern '{2}' in Json configuration data",
                            cacheIdProperty.Name,
                            cacheId,
                            CacheIdRegex);
                }
            }

            return cacheConfig;
        }

        private static Possible<object, Failure> ConvertTo(this ICacheConfigData cacheData, Type targetType, string configName)
        {
            object target = Activator.CreateInstance(targetType);

            foreach (var propertyInfo in target.GetType().GetProperties())
            {
                object value;
                if (cacheData.TryGetValue(propertyInfo.Name, out value))
                {
                    var nestedValue = value as ICacheConfigData;

                    if (nestedValue != null)
                    {
                        if (propertyInfo.PropertyType == typeof(ICacheConfigData))
                        {
                            propertyInfo.SetValue(target, nestedValue);
                        }
                        else
                        {
                            var nestedTarget = nestedValue.ConvertTo(propertyInfo.PropertyType, configName);
                            if (nestedTarget.Succeeded)
                            {
                                propertyInfo.SetValue(target, nestedTarget.Result);
                            }
                            else
                            {
                                return nestedTarget.Failure;
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            propertyInfo.SetValue(target, Convert.ChangeType(value, propertyInfo.PropertyType, CultureInfo.InvariantCulture));
                        }
                        catch (Exception e)
                        {
                            return new IncorrectJsonConfigDataFailure("{0} Json configuration field '{1}' can not be set to '{2}'\n{3}", configName, propertyInfo.Name, value, e.GetLogEventMessage());
                        }
                    }
                }
                else
                {
                    object defaultValue = propertyInfo.GetValue(target);
                    DefaultValueAttribute defaultValueAttribute =
                        propertyInfo.GetCustomAttributes(true).OfType<DefaultValueAttribute>().FirstOrDefault();

                    if (defaultValueAttribute != null)
                    {
                        try
                        {
                            propertyInfo.SetValue(target, Convert.ChangeType(defaultValueAttribute.Value, propertyInfo.PropertyType, CultureInfo.InvariantCulture));
                        }
                        catch (Exception e)
                        {
                            return
                                new IncorrectJsonConfigDataFailure(
                                    "{0} Json configuration field '{1}' can not be set to the default value of '{2}'\n{3}",
                                    configName,
                                    propertyInfo.Name,
                                    defaultValueAttribute.Value,
                                    e.GetLogEventMessage());
                        }
                    }
                    else if (defaultValue == null)
                    {
                        return new IncorrectJsonConfigDataFailure("{0} requires a value for '{1}' in Json configuration data", configName, propertyInfo.Name);
                    }
                }
            }

            // We used to validate that the JSON config had no fields that did not correspond to a field in our config, but this caused issues when we added new flags, since old versions of the cache would break
            //  when used with newer configs. Because of that, we removed that validation.
            return target;
        }
    }
}
