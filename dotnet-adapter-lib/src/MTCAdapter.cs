/*
 * Copyright Copyright 2012, System Insights, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *       http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 */

using System;

namespace MTConnect
{
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml;

    /// <summary>
    /// An MTConnect adapter
    /// </summary>
    public class Adapter : MultiClientStreamPort
    {
        // format string for SHDR timestamps
        private const string SHDR_TIME_FORMAT = "yyyy-MM-dd\\THH:mm:ss.fffK";

        /// <summary>
        /// All the data items we're tracking.
        /// </summary>
        private ArrayList mDataItems = new ArrayList();

        /// <summary>
        /// The send changed has begun and we are tracking conditions.
        /// </summary>
        bool mBegun = false;

        public bool Verbose { set; get; }

        /// <summary>
        /// Create an adapter. Defaults the heartbeat to 10 seconds and the 
        /// port to 7878
        /// </summary>
        /// <param name="aPort">The optional port number (default: 7878)</param>
        public Adapter(int aPort = 7878, bool verbose = false) : base("SHDR", aPort, 10000)
        {
            Verbose = verbose;            
        }

        public override void OnNewClient(Stream client)
        {
            SendAllTo(client);
        }

        /// <summary>
        /// Add a data item to the adapter.
        /// </summary>
        /// <param name="aDI">The data item.</param>
        public void AddDataItem(DataItem aDI)
        {
            mDataItems.Add(aDI);
        }

        /// <summary>
        /// Remove all data items.
        /// </summary>
        public void RemoveAllDataItems()
        {
            mDataItems.Clear();
        }

        /// <summary>
        /// Remove a data item from the adapter.
        /// </summary>
        /// <param name="aItem"></param>
        public void RemoveDataItem(DataItem aItem)
        {
            int ind = mDataItems.IndexOf(aItem);
            if (ind >= 0)
                mDataItems.RemoveAt(ind);
        }

        /// <summary>
        /// Make all data items unavailable
        /// </summary>
        public void Unavailable()
        {
            foreach (DataItem di in mDataItems)
                di.Unavailable();
        }

        /// <summary>
        /// The asks all data items to begin themselves for collection. Only 
        /// required for conditions and should not be called if you are not 
        /// planning on adding all the conditions before you send. If you skip this
        /// the adapter will not perform the mark and sweep.
        /// </summary>
        public void Begin()
        {
            mBegun = true;
            foreach (DataItem di in mDataItems) di.Begin();
        }

        /// <summary>
        /// Send only the objects that need have changed to the clients.
        /// </summary>
        /// <param name="timestamp">optional timestamp to use (defaults to null meaning 'now')</param>
        public void SendChanged(String timestamp = null)
        {
            if (mBegun)
                foreach (DataItem di in mDataItems) di.Prepare();

            // Separate out the data items into those that are on one line and those
            // need separate lines.
            List<DataItem> together = new List<DataItem>();
            List<DataItem> separate = new List<DataItem>();
            foreach (DataItem di in mDataItems)
            {
                List<DataItem> list = di.ItemList();
                if (di.NewLine)
                    separate.AddRange(list);
                else
                    together.AddRange(list);
            }

            // Compone all the same line data items onto one line.
            if (timestamp == null)
            {
                DateTime now = DateTime.UtcNow;
                timestamp = now.ToString(SHDR_TIME_FORMAT);
            }
            if (together.Count > 0)
            {
                string line = "";
                foreach (DataItem di in together)
                {
                    string item = "|" + di.ToString();
                    if (line.Length > 0 && (line.Length + item.Length) > 100)
                    {
                        SendToAll(timestamp + line + "\r\n");
                        line = "";
                    }
                    line += item;
                }
                if (line.Length > 0)
                {
                    SendToAll(timestamp + line + "\r\n");
                }
            }

            // Now write out all the separate lines
            if (separate.Count > 0)
            {
                foreach (DataItem di in separate)
                {
                    SendToAll(timestamp + "|" + di.ToString() + "\r\n");
                }
            }

            // Flush the output
            FlushAll();

            // Cleanup
            foreach (DataItem di in mDataItems) di.Cleanup();
            mBegun = false;
        }

        /// <summary>
        /// Send a new asset to the Agent
        /// </summary>
        /// <param name="asset">The asset</param>
        public void AddAsset(Asset asset)
        {

            StringBuilder result = new StringBuilder();
            
            DateTime now = DateTime.UtcNow;
            result.Append(now.ToString(SHDR_TIME_FORMAT));
            result.Append("|@ASSET@|");
            result.Append(asset.AssetId);
            result.Append('|');
            result.Append(asset.GetMTCType());
            result.Append("|--multiline--ABCD\r\n");

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.OmitXmlDeclaration = true;

            XmlWriter writer = XmlWriter.Create(result, settings);
            asset.ToXml(writer);
            writer.Close();
            result.Append("\r\n--multiline--ABCD\r\n");

            SendToAll(result.ToString());
        }

        /// <summary>
        /// Send all the data items, regardless if they have changed to one
        /// client. Used for the initial data dump.
        /// TODO: DRY out with SendChanged.
        /// </summary>
        /// <param name="aClient">The network stream of the client</param>
        public void SendAllTo(Stream aClient)
        {
            lock (aClient)
            {
                List<DataItem> together = new List<DataItem>();
                List<DataItem> separate = new List<DataItem>();
                foreach (DataItem di in mDataItems)
                {
                    List<DataItem> list = di.ItemList(true);
                    if (di.NewLine)
                        separate.AddRange(list);
                    else
                        together.AddRange(list);
                }


                DateTime now = DateTime.UtcNow;
                String timestamp = now.ToString(SHDR_TIME_FORMAT);

                String line = timestamp;
                foreach (DataItem di in together)
                    line += "|" + di.ToString();
                line += "\r\n";
                WriteToClient(aClient, line);

                foreach (DataItem di in separate)
                {
                    line = timestamp;
                    line += "|" + di.ToString() + "\r\n";
                    WriteToClient(aClient, line);
                }

                aClient.Flush();
            }
        }

    }
}