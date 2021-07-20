// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    public class TestErrorResultConverter
    {
        /// <summary>
        /// Validate that all implementations of ResultBase include the necessary constructor to use the AsResult extension method.
        /// </summary>
        [Fact]
        public void ValidateAsResult()
        {
            var resultBaseType = typeof(ResultBase);
            IEnumerable<Type> resultTypes = getResultTypes();

            var testError = new ErrorResult(new Exception(nameof(ValidateAsResult)));

            foreach (Type resultType in resultTypes)
            {
                MethodInfo asResultMethod = typeof(ErrorResult).GetMethod(nameof(ErrorResult.AsResult)).MakeGenericMethod(new Type[] { resultType });

                // testError.AsResult<resultType>();
                asResultMethod.Invoke(testError, null);
            }

            IEnumerable<Type> getResultTypes()
            {
                try
                {
                    return AppDomain.CurrentDomain.GetAssemblies()
                        .Where(assembly => assembly.FullName.Contains("BuildXL"))
                        .SelectMany(assembly => assembly.GetTypes())
                        .Where(t => resultBaseType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract && !t.IsGenericType && t.FullName != resultBaseType.FullName && t.Name != "CustomError")
                        .ToList();
                }
                catch(ReflectionTypeLoadException e)
                {
                    throw new AggregateException("Failed getting error types", e.LoaderExceptions);
                }
            }
        }
    }
}
