// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNet.SessionState;

using MB.HybridSessionProviderAsync.Resources;
namespace MB.HybridSessionProviderAsync
{
    /// <summary>
    /// Async version of SqlSessionState provider
    /// </summary>
    public class MemorySessionProviderAsync : SessionProviderAsyncMBBase
    {
        

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

        protected override async Task<GetItemResult> DoGet(HttpContextBase context, string id, bool exclusive, CancellationToken cancellationToken)
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




    }
}
