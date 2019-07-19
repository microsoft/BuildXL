// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Execution.Analyzer;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Analyzers.Core.XLGPlusPlus
{
    public class XLGppDataStore
    {
        public KeyValueStoreAccessor Accessor { get; set; }

        public XLGppDataStore()
        {

        }

        public bool OpenDatastore(string storeDirectory,
            bool defaultColumnKeyTracked = false,
            IEnumerable<string> additionalColumns = null,
            IEnumerable<string> additionalKeyTrackedColumns = null,
            Action<Failure> failureHandler = null,
            bool openReadOnly = false,
            bool dropMismatchingColumns = false,
            bool onFailureDeleteExistingStoreAndRetry = false)
        {

            var accessor = KeyValueStoreAccessor.Open(storeDirectory, 
                defaultColumnKeyTracked, 
                additionalColumns, 
                additionalKeyTrackedColumns, 
                failureHandler, 
                openReadOnly, 
                dropMismatchingColumns, 
                onFailureDeleteExistingStoreAndRetry);

            if (accessor.Succeeded)
            {
                Accessor = accessor.Result;
                return true;
            }
            else
            {
                return false;
            }
        }

        public List<string> GetEventsByType(int eventTypeID)
        {
            var storedEvents = new List<string>();
            var eventQuery = new EventTypeQuery
            {
                EventTypeID = eventTypeID,
            };
            Analysis.IgnoreResult(
                Accessor.Use(database =>
                {
                    foreach (var kvp in database.PrefixSearch(eventQuery.ToByteArray()))
                    {
                        Console.WriteLine(DominoInvocationEvent.Parser.ParseFrom(kvp.Value));
                    }
                })
            );
            
            return storedEvents;
        }

        /// <summary>
        /// Method to test if the appropriate things have been stored in DB.
        /// NOTE: For internal testing/Debugging only!
        /// </summary>
        /// <returns>Stored data in string format</returns>
        public string GetStoredData()
        {
            string value = null;
            Analysis.IgnoreResult(
                Accessor.Use(database =>
                {
                    database.TryGetValue("foo", out value);
                    foreach (var kvp in database.PrefixSearch("b"))
                    {
                        Console.WriteLine("The key is {0}, and the value is {1}", kvp.Key, kvp.Value);
                    }
                })
            );
            return value;
        }
    }
}
