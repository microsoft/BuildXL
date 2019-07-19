using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;

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
