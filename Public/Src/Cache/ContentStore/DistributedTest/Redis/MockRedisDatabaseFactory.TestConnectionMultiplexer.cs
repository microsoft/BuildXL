// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
using Microsoft.Caching.Redis.Profiling;
using ExportOptions = Microsoft.Caching.Redis.ExportOptions;
#else
using StackExchange.Redis;
using StackExchange.Redis.Profiling;
using ExportOptions = StackExchange.Redis.ExportOptions;
#endif

namespace ContentStoreTest.Distributed.Redis
{
    public class TestConnectionMultiplexer : IConnectionMultiplexer
    {
        private readonly IDatabase _testDatabaseAsync;
        private readonly Func<bool> _throwConnectionExceptionOnGet;

        public TestConnectionMultiplexer(IDatabase testDatabaseAsync, Func<bool> throwConnectionExceptionOnGet = null)
        {
            _testDatabaseAsync = testDatabaseAsync;
            _throwConnectionExceptionOnGet = throwConnectionExceptionOnGet;
        }

        public string ClientName => throw new NotImplementedException();

        public string Configuration => throw new NotImplementedException();

        public int TimeoutMilliseconds => throw new NotImplementedException();

        public long OperationCount => throw new NotImplementedException();

        public bool PreserveAsyncOrder { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool IsConnected => throw new NotImplementedException();

        public bool IncludeDetailInExceptions { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int StormLogThreshold { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool IsConnecting => throw new NotImplementedException();

        public event EventHandler<RedisErrorEventArgs> ErrorMessage { add { } remove { } }
        public event EventHandler<ConnectionFailedEventArgs> ConnectionFailed { add { } remove { } }
        public event EventHandler<InternalErrorEventArgs> InternalError { add { } remove { } }
        public event EventHandler<ConnectionFailedEventArgs> ConnectionRestored { add { } remove { } }
        public event EventHandler<EndPointEventArgs> ConfigurationChanged { add { } remove { } }
        public event EventHandler<EndPointEventArgs> ConfigurationChangedBroadcast { add { } remove { } }
        public event EventHandler<HashSlotMovedEventArgs> HashSlotMoved { add { } remove { } }

        public void BeginProfiling(object forContext)
        {
            throw new NotImplementedException();
        }

        public void Close(bool allowCommandsToComplete = true)
        {
            throw new NotImplementedException();
        }

        public Task CloseAsync(bool allowCommandsToComplete = true)
        {
            throw new NotImplementedException();
        }

        public bool Configure(TextWriter log = null)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ConfigureAsync(TextWriter log = null)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void ExportConfiguration(Stream destination, ExportOptions options = (ExportOptions)(-1))
        {
            throw new NotImplementedException();
        }

        public ProfiledCommandEnumerable FinishProfiling(object forContext, bool allowCleanupSweep = true)
        {
            throw new NotImplementedException();
        }

        public ServerCounters GetCounters()
        {
            throw new NotImplementedException();
        }

        public IDatabase GetDatabase(int db = -1, object asyncState = null)
        {
            if (_throwConnectionExceptionOnGet != null && _throwConnectionExceptionOnGet())
            {
                // The required constructors are internal, using reflection to mock the connectivity issue.
                Type exceptionType = typeof(RedisConnectionException);

                var constructor = ((TypeInfo)exceptionType).DeclaredConstructors.First(
                    c =>
                    {
                        var parameters = c.GetParameters();
                        return parameters.Length == 2 && parameters[0].ParameterType == typeof(ConnectionFailureType) &&
                               parameters[1].ParameterType == typeof(string);
                    });
                var result = (Exception)constructor.Invoke(new object[] {ConnectionFailureType.UnableToResolvePhysicalConnection, "UnableToResolvePhysicalConnection" });
                throw result;
            }

            return (IDatabase)_testDatabaseAsync;
        }

        public EndPoint[] GetEndPoints(bool configuredOnly = false)
        {
            throw new NotImplementedException();
        }

        public int GetHashSlot(RedisKey key)
        {
            throw new NotImplementedException();
        }

        public IServer GetServer(string host, int port, object asyncState = null)
        {
            throw new NotImplementedException();
        }

        public IServer GetServer(string hostAndPort, object asyncState = null)
        {
            throw new NotImplementedException();
        }

        public IServer GetServer(IPAddress host, int port)
        {
            throw new NotImplementedException();
        }

        public IServer GetServer(EndPoint endpoint, object asyncState = null)
        {
            throw new NotImplementedException();
        }

        public string GetStatus()
        {
            throw new NotImplementedException();
        }

        public void GetStatus(TextWriter log)
        {
            throw new NotImplementedException();
        }

        public string GetStormLog()
        {
            throw new NotImplementedException();
        }

        public ISubscriber GetSubscriber(object asyncState = null)
        {
            throw new NotImplementedException();
        }

        public int HashSlot(RedisKey key)
        {
            throw new NotImplementedException();
        }

        public long PublishReconfigure(CommandFlags flags = CommandFlags.None)
        {
            throw new NotImplementedException();
        }

        public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None)
        {
            throw new NotImplementedException();
        }

        public void RegisterProfiler(Func<ProfilingSession> profilingSessionProvider)
        {
            throw new NotImplementedException();
        }

        public void ResetStormLog()
        {
            throw new NotImplementedException();
        }

        public void Wait(Task task)
        {
            throw new NotImplementedException();
        }

        public T Wait<T>(Task<T> task)
        {
            throw new NotImplementedException();
        }

        public void WaitAll(params Task[] tasks)
        {
            throw new NotImplementedException();
        }
    }
}
