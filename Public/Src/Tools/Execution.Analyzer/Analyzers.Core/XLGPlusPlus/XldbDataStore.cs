// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Execution.Analyzer.Xldb;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Analyzers.Core.XLGPlusPlus
{
    public sealed class XldbDataStore : IDisposable
    {
        /// <summary>
        /// Rocks DB Accessor for XLG++ data
        /// </summary>
        private KeyValueStoreAccessor Accessor { get; set; }

        /// <summary>
        /// Open the datastore and populate the KeyValueStoreAccessor for the XLG++ DB
        /// </summary>
        public XldbDataStore(string storeDirectory,
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
                Console.Error.WriteLine("Could not create an accessor for RocksDB. Accessor is null");
            }
        }

        /// <summary>
        /// Gets all the events of a certain type from the DB
        /// </summary>
        /// <returns>List of event objects recovered from DB </returns>
        public IEnumerable<string> GetEventsByType(ExecutionEventId eventTypeID)
        {
            Contract.Assert(Accessor != null, "XldbStore must be initialized via OpenDatastore first");

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
                        storedEvents.Add(BXLInvocationEvent.Parser.ParseFrom(kvp.Value).ToString());
                    }
                })
            );

            return storedEvents;
        }

        public void Dispose()
        {
            Accessor.Dispose();
        }
    }
}
