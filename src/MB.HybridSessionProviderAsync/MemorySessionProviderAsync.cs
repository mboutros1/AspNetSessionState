// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

using MB.HybridSessionProviderAsync.Resources;
namespace MB.HybridSessionProviderAsync
{
    /// <summary>
    /// Async version of SqlSessionState provider
    /// </summary>
    public class MemorySessionProviderAsync : SessionStateStoreProviderAsyncBase
    {
        private const string INMEMORY_TABLE_CONFIGURATION_NAME = "UseInMemoryTable";
        private const string MAX_RETRY_NUMBER_CONFIGURATION_NAME = "MaxRetryNumber";
        private const string RETRY_INTERVAL_CONFIGURATION_NAME = "RetryInterval";
        private const string CONNECTIONSTRING_NAME_CONFIGURATION_NAME = "connectionStringName";
        private const string SESSIONSTATE_SECTION_PATH = "system.web/sessionState";
        private const double SessionExpiresFrequencyCheckIntervalTicks = 30 * TimeSpan.TicksPerSecond;
        private static long s_lastSessionPurgeTicks;
        private static int s_inPurge;
        private static string s_appSuffix;
        private static bool s_compressionEnabled;
        private static bool s_oneTimeInited = false;
        private static object s_lock = new object();
        private static ISqlSessionStateRepository s_sqlSessionStateRepository;

        /// <summary>
        /// Initialize the provider through the configuration
        /// </summary>
        /// <param name="name">Sessionstate provider name</param>
        /// <param name="config">Configuration values</param>
        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (String.IsNullOrEmpty(name))
            {
                name = "SqlSessionStateAsyncProvider";
            }

            var ssc = (SessionStateSection)ConfigurationManager.GetSection(SESSIONSTATE_SECTION_PATH);
            var connectionString = GetConnectionString(config[CONNECTIONSTRING_NAME_CONFIGURATION_NAME]);

            Initialize(name, config, ssc, connectionString, true);
        }

        // for unit tests
        internal void Initialize(string name, NameValueCollection config, SessionStateSection ssc, ConnectionStringSettings connectionString,
                                    bool shouldCreateTable = false)
        {
            base.Initialize(name, config);

            if (!s_oneTimeInited)
            {
                lock (s_lock)
                {
                    if (!s_oneTimeInited)
                    {
                        s_compressionEnabled = ssc.CompressionEnabled;
                        if (connectionString == null)
                        {
                            s_sqlSessionStateRepository = new MemoryStateRepository();
                        }
                        else
                        {
                            if (ShouldUseInMemoryTable(config))
                            {
                                s_sqlSessionStateRepository = new SqlInMemoryTableSessionStateRepository(
                                    connectionString.ConnectionString,
                                    (int)ssc.SqlCommandTimeout.TotalSeconds, GetRetryInterval(config),
                                    GetMaxRetryNum(config));
                            }
                            else
                            {
                                s_sqlSessionStateRepository = new SqlSessionStateRepository(
                                    connectionString.ConnectionString,
                                    (int)ssc.SqlCommandTimeout.TotalSeconds, GetRetryInterval(config),
                                    GetMaxRetryNum(config));
                            }

                            if (shouldCreateTable)
                            {
                                s_sqlSessionStateRepository.CreateSessionStateTable();
                            }
                        }

                        var appId = AppId ?? HttpRuntime.AppDomainAppId;
                        Debug.Assert(appId != null);
                        s_appSuffix = appId.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);

                        s_oneTimeInited = true;
                    }
                }
            }
        }

        #region properties/methods for unit tests
        internal ISqlSessionStateRepository SqlSessionStateRepository
        {
            get => s_sqlSessionStateRepository;
            set => s_sqlSessionStateRepository = value;
        }

        internal bool CompressionEnabled => s_compressionEnabled;

        internal void ResetOneTimeInited()
        {
            s_oneTimeInited = false;
        }

        internal string AppId
        {
            get; set;
        }

        internal int OrigStreamLen { get; private set; }

        internal static Func<HttpContext, HttpStaticObjectsCollection> GetSessionStaticObjects
        {
            get; set;
        } = SessionStateUtility.GetSessionStaticObjects;
        #endregion

        private bool ShouldUseInMemoryTable(NameValueCollection config)
        {
            var val = config[INMEMORY_TABLE_CONFIGURATION_NAME];
            return (val != null && bool.TryParse(val, out var useInMemoryTable) && useInMemoryTable);
        }

        private int? GetMaxRetryNum(NameValueCollection config)
        {
            var val = config[MAX_RETRY_NUMBER_CONFIGURATION_NAME];
            if (val != null && int.TryParse(val, out var maxRetryNum))
            {
                return maxRetryNum;
            }
            return null;
        }

        private int? GetRetryInterval(NameValueCollection config)
        {
            var val = config[RETRY_INTERVAL_CONFIGURATION_NAME];
            if (val != null && int.TryParse(val, out var retryInterval))
            {
                return retryInterval;
            }
            return null;
        }

        /// <summary>
        /// Create a new SessionStateStoreData
        /// </summary>
        /// <param name="context">Httpcontext</param>
        /// <param name="timeout">Session timeout</param>
        /// <returns></returns>
        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            HttpStaticObjectsCollection staticObjects = null;
            if (context != null)
            {
                staticObjects = GetSessionStaticObjects(context.ApplicationInstance.Context);
            }

            return new SessionStateStoreData(new SessionStateItemCollection(), staticObjects, timeout);
        }

        /// <inheritdoc />
        public override async Task CreateUninitializedItemAsync(
            HttpContextBase context,
            string id,
            int timeout,
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);

            var item = new SessionStateStoreData(new SessionStateItemCollection(),
                        GetSessionStaticObjects(context.ApplicationInstance.Context),
                        timeout);

            await s_sqlSessionStateRepository.CreateUninitializedSessionItemAsync(id, item, timeout);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }

        /// <inheritdoc />
        public override Task EndRequestAsync(HttpContextBase context)
        {
            PurgeIfNeeded();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, false, cancellationToken);
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemExclusiveAsync(
            HttpContextBase context,
            string id,
            CancellationToken cancellationToken)
        {
            return DoGet(context, id, true, cancellationToken);
        }

        /// <inheritdoc />
        public override void InitializeRequest(HttpContextBase context)
        {
            OrigStreamLen = 0;
        }

        /// <inheritdoc />
        public override async Task ReleaseItemExclusiveAsync(
            HttpContextBase context,
            string id,
            object lockId,
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            id = AppendAppIdHash(id);

            await s_sqlSessionStateRepository.ReleaseSessionItemAsync(id, lockId);
        }

        /// <inheritdoc />
        public override async Task RemoveItemAsync(
            HttpContextBase context,
            string id,
            object lockId,
            SessionStateStoreData item,
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            id = AppendAppIdHash(id);

            await s_sqlSessionStateRepository.RemoveSessionItemAsync(id, lockId);
        }

        /// <inheritdoc />
        public override async Task ResetItemTimeoutAsync(
            HttpContextBase context,
            string id,
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            id = AppendAppIdHash(id);

            await s_sqlSessionStateRepository.ResetSessionItemTimeoutAsync(id);
        }

        /// <inheritdoc />
        public override async Task SetAndReleaseItemExclusiveAsync(
            HttpContextBase context,
            string id,
            SessionStateStoreData item,
            object lockId,
            bool newItem,
            CancellationToken cancellationToken)
        {
            int lockCookie;

            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);


            lockCookie = (int?)lockId ?? 0;

            await s_sqlSessionStateRepository.CreateOrUpdateSessionStateItemAsync(newItem, id, item, item.Timeout, lockCookie, OrigStreamLen);
        }

        /// <inheritdoc />
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        private async Task<GetItemResult> DoGet(HttpContextBase context, string id, bool exclusive, CancellationToken cancellationToken)
        {
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);


            var s = await s_sqlSessionStateRepository.GetSessionStateItemAsync(id, exclusive);
            SessionItem sessionItem = s.Item1;
            SessionStateStoreData data = s.Item2;
            if (sessionItem == null)
            {
                return null;
            }
            if (sessionItem.Item == null && data == null)
            {
                return new GetItemResult(null, sessionItem.Locked, sessionItem.LockAge, sessionItem.LockId, sessionItem.Actions);
            } 

            return new GetItemResult(data, sessionItem.Locked, sessionItem.LockAge, sessionItem.LockId, sessionItem.Actions);
        }

        // We just want to append an 8 char hash from the AppDomainAppId to prevent any session id collisions
        internal static string AppendAppIdHash(string id)
        {
            if (!id.EndsWith(s_appSuffix))
            {
                return id + s_appSuffix;
            }
            return id;
        }
  
        private bool CanPurge()
        {
            return (
                DateTime.UtcNow.Ticks - s_lastSessionPurgeTicks > SessionExpiresFrequencyCheckIntervalTicks
                && Interlocked.CompareExchange(ref s_inPurge, 1, 0) == 0
                );
        }

        private void PurgeIfNeeded()
        {
            // Only check for expired sessions every 30 seconds.
            if (CanPurge())
            {
                Task.Run(PurgeExpiredSessions);
            }
        }

        private void PurgeExpiredSessions()
        {
            try
            {
                s_sqlSessionStateRepository.DeleteExpiredSessions();
                s_lastSessionPurgeTicks = DateTime.UtcNow.Ticks;
            }
            catch
            {
                // Swallow all failures, this is called from an async Task and we don't want to crash
            }
            finally
            {
                Interlocked.CompareExchange(ref s_inPurge, 0, 1);
            }
        }

        private static ConnectionStringSettings GetConnectionString(string connectionstringName)
        {
            if (string.IsNullOrEmpty(connectionstringName))
            {
                return null;
            }
            ConnectionStringSettings conn = ConfigurationManager.ConnectionStrings[connectionstringName];
            if (conn == null)
            {
                throw new ProviderException(
                    String.Format(CultureInfo.CurrentCulture, SR.Connection_string_not_found, connectionstringName));
            }
            return conn;
        }
    }
}
