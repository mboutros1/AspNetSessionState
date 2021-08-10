// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
    public class HybridSessionProviderAsync : SessionProviderAsyncMBBase
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

            SerializeStoreData(item, SqlSessionStateRepositoryUtil.DefaultItemLength, out var buf, out var length, s_compressionEnabled);
            await s_sqlSessionStateRepository.CreateUninitializedSessionItemAsync(id, length, buf, timeout);
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
            byte[] buf;
            int length;
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

            try
            {
                SerializeStoreData(item, SqlSessionStateRepositoryUtil.DefaultItemLength, out buf, out length, s_compressionEnabled);
            }
            catch
            {
                if (!newItem)
                {
                    await ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
                }
                throw;
            }

            lockCookie = (int?)lockId ?? 0;

            await s_sqlSessionStateRepository.CreateOrUpdateSessionStateItemAsync(newItem, id, buf, length, item.Timeout, lockCookie, OrigStreamLen);
        }

        protected override async Task<GetItemResult> DoGet(HttpContextBase context, string id, bool exclusive, CancellationToken cancellationToken)
        {
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);

            SessionStateStoreData data;
            var s = await s_sqlSessionStateRepository.GetSessionStateItemAsync(id, exclusive);
            var sessionItem = s.Item1;
            if (sessionItem == null)
            {
                return null;
            }
            if (sessionItem.Item == null)
            {
                return new GetItemResult(null, sessionItem.Locked, sessionItem.LockAge, sessionItem.LockId, sessionItem.Actions);
            }

            using (var stream = new MemoryStream(sessionItem.Item))
            {
                data = DeserializeStoreData(context, stream, s_compressionEnabled);
                OrigStreamLen = (int)stream.Position;
            }

            return new GetItemResult(data, sessionItem.Locked, sessionItem.LockAge, sessionItem.LockId, sessionItem.Actions);
        }

        // Internal code copied from SessionStateUtility
        internal static void SerializeStoreData(
            SessionStateStoreData item,
            int initialStreamSize,
            out byte[] buf,
            out int length,
            bool compressionEnabled)
        {
            using (MemoryStream s = new MemoryStream(initialStreamSize))
            {
                Serialize(item, s);
                if (compressionEnabled)
                {
                    byte[] serializedBuffer = s.GetBuffer();
                    int serializedLength = (int)s.Length;
                    // truncate the MemoryStream so we can write the compressed data in it
                    s.SetLength(0);
                    // compress the serialized bytes
                    using (DeflateStream zipStream = new DeflateStream(s, CompressionMode.Compress, true))
                    {
                        zipStream.Write(serializedBuffer, 0, serializedLength);
                    }
                    // if the session state tables have ANSI_PADDING disabled, last )s are trimmed.
                    // This shouldn't happen, but to be sure, we are padding with an extra byte
                    s.WriteByte(0xff);
                }
                buf = s.GetBuffer();
                length = (int)s.Length;
            }
        }

        private static void Serialize(SessionStateStoreData item, Stream stream)
        {
            bool hasItems = true;
            bool hasStaticObjects = true;

            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(item.Timeout);

            if (item.Items == null || item.Items.Count == 0)
            {
                hasItems = false;
            }
            writer.Write(hasItems);

            if (item.StaticObjects == null || item.StaticObjects.NeverAccessed)
            {
                hasStaticObjects = false;
            }
            writer.Write(hasStaticObjects);

            if (hasItems)
            {
                ((SessionStateItemCollection)item.Items).Serialize(writer);
            }

            if (hasStaticObjects)
            {
                item.StaticObjects.Serialize(writer);
            }

            // Prevent truncation of the stream
            writer.Write((byte)0xff);
        }

        internal static SessionStateStoreData DeserializeStoreData(HttpContextBase context, Stream stream, bool compressionEnabled)
        {
            if (compressionEnabled)
            {
                // apply the compression decorator on top of the stream
                // the data should not be bigger than 4GB - compression doesn't work for more than that
                using (DeflateStream zipStream = new DeflateStream(stream, CompressionMode.Decompress, true))
                {
                    return Deserialize(context, zipStream);
                }
            }
            return Deserialize(context, stream);
        }

        private static SessionStateStoreData Deserialize(HttpContextBase context, Stream stream)
        {
            int timeout;
            SessionStateItemCollection sessionItems;
            bool hasItems;
            bool hasStaticObjects;
            HttpStaticObjectsCollection staticObjects;
            byte eof;

            Debug.Assert(context != null);

            try
            {
                BinaryReader reader = new BinaryReader(stream);

                timeout = reader.ReadInt32();
                hasItems = reader.ReadBoolean();
                hasStaticObjects = reader.ReadBoolean();

                if (hasItems)
                {
                    sessionItems = SessionStateItemCollection.Deserialize(reader);
                }
                else
                {
                    sessionItems = new SessionStateItemCollection();
                }

                if (hasStaticObjects)
                {
                    staticObjects = HttpStaticObjectsCollection.Deserialize(reader);
                }
                else
                {
                    staticObjects = GetSessionStaticObjects(context.ApplicationInstance.Context);
                }

                eof = reader.ReadByte();
                if (eof != 0xff)
                {
                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
                }
            }
            catch (EndOfStreamException)
            {
                throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
            }

            return new SessionStateStoreData(sessionItems, staticObjects, timeout);
        }
    }
}
