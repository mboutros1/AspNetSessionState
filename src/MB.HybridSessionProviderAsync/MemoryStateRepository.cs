using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.SessionState;


namespace MB.HybridSessionProviderAsync
{
    internal class MemoryStateRepository : ISqlSessionStateRepository
    {
        public void CreateSessionStateTable()
        {
            return;
        }

        public void DeleteExpiredSessions()
        {
            var expiredSessions = SData.Data.Values.Where(h => h.Expires < DateTime.UtcNow).Select(h => h.SessionId).ToList();
            foreach (var ess in expiredSessions)
            {
                SData.Data.TryRemove(ess, out _);
            }

        }

        public Task<(SessionItem,SessionStateStoreData)> GetSessionStateItemAsync(string id, bool exclusive)
        {
            SessionStateStoreData storeData;
            SData.Data.TryGetValue(id, out var orgValue);
            if (orgValue == null)
            { 
                return Task.FromResult<(SessionItem, SessionStateStoreData)>((null, null));
            }
            byte[] buf = orgValue.Access(true);

            var value = orgValue.Clone();
            int actions = 0;
            var wasLocked = value.Locked;

            if (exclusive)
            {
                if ((value.Flags & 1) != 0)
                    value.Flags = orgValue.Flags = value.Flags & ~1;
                else
                    value.Flags = orgValue.Flags = 0;
                var utc = DateTime.UtcNow;
                value.LockAge = wasLocked ? TimeSpan.Zero : (utc - value.LockDate);
                value.LockCookie = wasLocked ? 0 : value.LockCookie;
                value.LockDate = orgValue.LockDate = wasLocked ? orgValue.LockDate : utc;
                value.LockDateLocal = orgValue.LockDateLocal = wasLocked ? orgValue.LockDateLocal : DateTime.Now;
                value.Locked = orgValue.Locked = true;
                value.LockCookie = wasLocked ? value.LockCookie : value.LockCookie + 1;
                buf = wasLocked ? null : buf;
            }
            else
            {
                buf = wasLocked ? null : buf;

                if ((value.Flags & 1) != 0)
                {
                    value.Flags = orgValue.Flags = value.Flags & ~1;
                    actions = 1;
                }
                else
                {
                    actions = 0;
                }
            }
            if (wasLocked)
            {
                var lockAge = value.LockAge;

                if (lockAge > new TimeSpan(0, 0, Sec.ONE_YEAR))
                {
                    lockAge = TimeSpan.Zero;
                }

                value.LockAge = lockAge;
            }

            storeData = value.StoreData;
            return Task.FromResult((new SessionItem(buf, true, value.LockAge, value.LockCookie, (SessionStateActions)actions),storeData));
        }



        public Task CreateOrUpdateSessionStateItemAsync(bool newItem, string id, byte[] buf, int length, int timeout, int lockCookie,
            int orginalStreamLen)
        {
            if (newItem)
                SData.Add(id, length, buf, timeout);
            else
            {
                SData.Data.TryGetValue(id, out var value);
                if (value == null)
                    SData.Add(id, length, buf, timeout);
                else
                    value.Update(buf, timeout);
            }

            return Task.CompletedTask;
        }

        public Task CreateOrUpdateSessionStateItemAsync(bool newItem, string id, SessionStateStoreData itemData, int timeout,
            int lockCookie, int origStreamLen)
        {
            if (newItem)
                SData.Add(id, itemData, timeout);
            else
            {
                SData.Data.TryGetValue(id, out var value);
                if (value == null)
                    SData.Add(id, itemData, timeout);
                else
                    value.Update(itemData, timeout);
            }

            return Task.CompletedTask;
        }


        public Task ResetSessionItemTimeoutAsync(string id)
        {
            SData.Data.TryGetValue(id, out var value);
            value?.Access();
            return Task.CompletedTask;

        }

        public Task RemoveSessionItemAsync(string id, object lockId)
        {
            SData.Data.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task ReleaseSessionItemAsync(string id, object lockId)
        {
            SData.Data.TryRemove(id, out var value);
            if (value != null)
            {
                value.Locked = false;
                value.Access();
            }

            return Task.CompletedTask;
        }

        public Task CreateUninitializedSessionItemAsync(string id, int length, byte[] buf, int timeout)
        {
            SData.Add(id, length, buf, timeout);
            return Task.CompletedTask;
        }

        public Task CreateUninitializedSessionItemAsync(string id, SessionStateStoreData item, int timeout)
        {
            throw new NotImplementedException();
        }

       
    }
}