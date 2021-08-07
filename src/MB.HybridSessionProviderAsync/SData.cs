// --------------------------------------------------------------------------------------------------------
// <copyright file="SData.cs" company="Moody&#39;s Analytics, Inc">
//       Copyright (c) Moody&#39;s Analytics, Inc. All rights reserved.
// 
//       Use of this code is subject to the terms of our license.
//       Re-distribution in any form is strictly prohibited.
//       Any infringement will be prosecuted under applicable laws.
// </copyright>
// <author>Boutros, Michael</author>
// --------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Web.SessionState;

// ReSharper disable UnusedMember.Global

namespace MB.HybridSessionProviderAsync
{
    /// <summary>
    /// Session Data 
    /// </summary>
    public class SData :ICloneable
    {

#if DEBUG
       public
#else
       internal 
#endif
            static ConcurrentDictionary<string, SData> Data = new ConcurrentDictionary<string, SData>();
        public static SData Add(string id, int length, byte[] buff, int timeout)
        {
            var utcDate = DateTime.UtcNow;
            var s = new SData()
            {
                SessionId = id,
                Timeout = timeout,
                SessionItemLong = buff,
                Expires = utcDate.AddMinutes(timeout),
                LockDate = utcDate,
                LockDateLocal = DateTime.Now,
                LockCookie = 1,
                Flags = 1
            };
            Data.TryAdd(id, s);
            return s;
        }

        public static SData Add(string id, SessionStateStoreData itemData, int timeout)
        {
            var utcDate = DateTime.UtcNow;
            var s = new SData()
            {
                SessionId = id,
                Timeout = timeout,
                StoreData = itemData,
                Expires = utcDate.AddMinutes(timeout),
                LockDate = utcDate,
                LockDateLocal = DateTime.Now,
                LockCookie = 1,
                Flags = 1
            };
            Data.TryAdd(id, s);
            return s;
        }

        public SessionStateStoreData StoreData { get; set; }

        public DateTime LockDateLocal { get; set; }

        public string SessionId { get; set; }

        public DateTime Expired { get; set; }

        public DateTime? Expires { get; set; }


        public TimeSpan LockAge { get; set; }

        public DateTime LockDate { get; set; }

        public int LockCookie { get; set; }

        public int Timeout { get; set; }
        public bool Locked { get; set; }
        private byte[] SessionItemLong { get; set; }
        public int Flags { get; set; }

        public void Update(byte[] buf, int timeout)
        {
            AddMinutes(timeout);
            SessionItemLong = buf;
        }

        public void Update(SessionStateStoreData itemData, int timeout)
        {
            AddMinutes(timeout);
            StoreData = itemData;
        }

        public void AddMinutes(int minutes)
        {
            Expires = DateTime.UtcNow.AddMinutes(minutes);
        }


        public byte[] Access(bool updateLockAge = false)
        {
            AddMinutes(Timeout);
            if (updateLockAge)
            {
                //TimeSpan ts = DateTime.UtcNow - LockDate;
                //if (ts > new TimeSpan(0, 0, Sec.ONE_YEAR))
                //{
                //    ts = TimeSpan.Zero;
                //}
                //LockAge = ts;
            }
            else LockAge = new TimeSpan(0);
            return SessionItemLong;
        }
         
        public SData Clone()
        {
            return (SData) MemberwiseClone();
        }

        /// <inheritdoc />
        object ICloneable.Clone()
        {
            return Clone();
        }


    }
}