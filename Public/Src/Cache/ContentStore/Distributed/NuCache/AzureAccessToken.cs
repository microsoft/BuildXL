using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Arguments to the notification sent by <see cref="AzureAccessToken"/>.
    /// </summary>
    public class AccessTokenChangeEventArgs : EventArgs
    {
        /// <summary>
        /// The new access token.
        /// </summary>
        public string AccessToken { get; set; }
    }

    /// <summary>
    /// Abstract class for a token that can be used to access Azure services and can be refreshed over time.
    /// </summary>
    public abstract class AzureAccessToken
    {
        /// <summary>
        /// Subscribing to this event implies that you will receive a notification every time the access token changes.
        /// 
        /// Since the access token could be computed lazily, it is possible for <see cref="GetAccessToken"/> to force a
        /// notification.
        /// </summary>
        public event EventHandler<AccessTokenChangeEventArgs> AccessTokenChange;

        /// <summary>
        /// Fetches the currently valid access token.
        /// </summary>
        public abstract string GetAccessToken();

        /// <summary>
        /// Forces a refresh of the access token.
        /// </summary>
        public abstract void ForceRefresh();

        /// <nodoc />
        protected virtual void NotifyAccessTokenChanged(AccessTokenChangeEventArgs argument)
        {
            Contract.Requires(argument != null);
            AccessTokenChange?.Invoke(this, argument);
        }
    }

    /// <summary>
    /// Represents a fixed connection string that does not change over time. The change event will only be triggered
    /// when <see cref="ForceRefresh"/> is called.
    /// </summary>
    public class ConnectionString : AzureAccessToken
    {
        private readonly string _connectionString;

        /// <nodoc />
        public ConnectionString(string connectionString)
        {
            Contract.Requires(connectionString != null);

            _connectionString = connectionString;
        }

        /// <inheritdoc />
        public override string GetAccessToken()
        {
            return _connectionString;
        }

        /// <inheritdoc />
        public override void ForceRefresh()
        {
            NotifyAccessTokenChanged(new AccessTokenChangeEventArgs
            {
                AccessToken = _connectionString
            });
        }
    }

    /// <summary>
    /// Represents SAS authentication for Azure Storage.
    /// </summary>
    public class AzureStorageSharedAccessSignature : AzureAccessToken, IDisposable
    {
        private readonly string _connectionString;
        private readonly CloudStorageAccount _account;
        private readonly Func<SharedAccessAccountPolicy> _policyFactory;

        private string _sharedAccessSignature = null;
        private Timer _refreshTimer;

        /// <summary>
        /// Constructor for a blanket policy that provides access to everything (see
        /// <see cref="DefaultPolicyFactory"/>).
        /// </summary>
        public AzureStorageSharedAccessSignature(string connectionString) : this(connectionString, DefaultPolicyFactory)
        {
        }

        /// <summary>
        /// Blanket policy factory that gives access to everything, just like a connection string would.
        /// </summary>
        private static SharedAccessAccountPolicy DefaultPolicyFactory()
        {
            return new SharedAccessAccountPolicy()
            {
                Permissions = SharedAccessAccountPermissions.Add | SharedAccessAccountPermissions.Create | SharedAccessAccountPermissions.Delete | SharedAccessAccountPermissions.List | SharedAccessAccountPermissions.ProcessMessages | SharedAccessAccountPermissions.Read | SharedAccessAccountPermissions.Update | SharedAccessAccountPermissions.Write,
                Services = SharedAccessAccountServices.Blob | SharedAccessAccountServices.File | SharedAccessAccountServices.Queue | SharedAccessAccountServices.Table,
                ResourceTypes = SharedAccessAccountResourceTypes.Container | SharedAccessAccountResourceTypes.Object | SharedAccessAccountResourceTypes.Service,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24),
                Protocols = SharedAccessProtocol.HttpsOnly
            };
        }

        /// <summary>
        /// Provides SAS authentication for Azure Storage with a custom policy factory. Useful when you are interested
        /// in restraining the permissions of the obtained tokens.
        /// </summary>
        /// <param name="connectionString">
        /// The connection string to be used.
        /// </param>
        /// <param name="policyFactory">
        /// Builds the policy that will be used for access. It needs to be a new object every time it is renewed,
        /// and will control the lease-time for each SAS token.
        /// </param>
        public AzureStorageSharedAccessSignature(string connectionString, Func<SharedAccessAccountPolicy> policyFactory)
        {
            Contract.Requires(connectionString != null);
            Contract.Requires(policyFactory != null);

            _connectionString = connectionString;
            _account = CloudStorageAccount.Parse(_connectionString);
            _policyFactory = policyFactory;

            _refreshTimer = new Timer(state => ForceRefresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <inheritdoc />
        public override string GetAccessToken()
        {
            if (_sharedAccessSignature == null)
            {
                ForceRefresh();
            }

            return _sharedAccessSignature;
        }

        /// <inheritdoc />
        public override void ForceRefresh()
        {
            var policy = _policyFactory();
            Contract.Requires(policy != null);
            // The lease must be valid as soon as we obtain it
            Contract.Requires(!policy.SharedAccessStartTime.HasValue || policy.SharedAccessStartTime.Value <= DateTimeOffset.UtcNow);

            _sharedAccessSignature = _account.GetSharedAccessSignature(policy);
            AdjustRefreshTimer(policy);
            NotifyAccessTokenChanged(new AccessTokenChangeEventArgs {
                AccessToken = _sharedAccessSignature
            });
        }

        private void AdjustRefreshTimer(SharedAccessAccountPolicy policy)
        {
            if (!policy.SharedAccessExpiryTime.HasValue)
            {
                _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            TimeSpan refreshDelta = policy.SharedAccessExpiryTime.Value - DateTimeOffset.UtcNow;
            // No ridiculously short refresh times
            Contract.Requires(refreshDelta >= TimeSpan.FromHours(1));
            // Give the request some time
            refreshDelta -= TimeSpan.FromMinutes(10);

            _refreshTimer.Change(refreshDelta, Timeout.InfiniteTimeSpan);
        }

        /// <nodoc />
        public void Dispose()
        {
            _refreshTimer.Dispose();
        }
    }
}
