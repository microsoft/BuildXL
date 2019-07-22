// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Execution.Analyzer;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Analyzers.Core.XLGPlusPlus
{
    public class XLGppDataStore: IDisposable
    {
        /// <summary>
        /// Rocks DB Accessor for XLG++ data
        /// </summary>
        private KeyValueStoreAccessor Accessor { get; set; }

        /// <summary>
        /// Open the datastore and populate the KeyValueStoreAccessor for the XLG++ DB
        /// </summary>
        public XLGppDataStore(string storeDirectory,
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
            }
            else
            {
                Accessor = null;
            }
        }

        /// <summary>
        /// Open the datastore and populate the KeyValueStoreAccessor for the XLG++ DB
        /// </summary>
        /// <returns>Boolean if datastore was opened successfully</returns>
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

        /// <summary>
        /// Gets all the events of a certain type from the DB
        /// </summary>
        /// <returns>List of event objects recovered from DB </returns>
        public IEnumerable<string> GetEventsByType(int eventTypeID)
        {
            Contract.Assert(Accessor != null, "XLGppStore must be initialized via OpenDatastore first");

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
                        Console.WriteLine(BXLInvocationEvent.Parser.ParseFrom(kvp.Value));
                        storedEvents.Add(BXLInvocationEvent.Parser.ParseFrom(kvp.Value).ToString());
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
            Contract.Assert(Accessor != null, "XLGppStore must be initialized via OpenDatastore first");

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

        public void Dispose()
        {
            Contract.Assert(Accessor != null, "XLGppStore must be initialized via OpenDatastore first");

            Accessor.Dispose();
        }
    }
}
