using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MTConnect
{
    class ClientCollection 
    {
        private ConcurrentDictionary<Stream, bool> collection = new ConcurrentDictionary<Stream, bool>();

        public int Count { get { return collection.Count; } }

        /// <summary>
        /// Snapshot list of client streams in this collection
        /// </summary>
        public IEnumerable<Stream> List { get { return collection.Keys; } }

        /// <summary>
        /// (try to) remove a client stream
        /// </summary>
        /// <param name="client">the stream to remove</param>
        /// <returns>true if removal succeeded, false if client not found</returns>
        public bool Remove(Stream client)
        {
            bool ignore;
            return client != null &&
                    collection.TryRemove(client, out ignore);
        }

        internal void Add(Stream client)
        {
            // returns false if the same key exists already
            // i.e. client is already in the collection.
            // We don't care so we ignore that case.
            collection.TryAdd(client, true);
        }
    }

    public class MultiClientStreamPort
    {
        /// <summary>
        /// The listening thread for new connections
        /// </summary>
        protected Thread mListenThread;

        private List<Action<Stream>> connectHandlers = new List<Action<Stream>>();

        public void OnConnect(Action<Stream> handler)
        {
            connectHandlers.Add(handler);
        }

        /// <summary>
        /// A list of all the client connections.
        /// </summary>
        private ClientCollection mClients = new ClientCollection();

        /// <summary>
        /// A count of client threads.
        /// </summary>
        /// <remarks>The initial count of 1 represents the main thread.</remarks>
        private CountdownEvent mActiveClients = new CountdownEvent(1);

        /// <summary>
        /// The port the adapter will be listening on.
        /// </summary>
        /// <remarks>only takes effect at Start</remarks>
        public int Port;

        /// <summary>
        /// The human-readable name of this subscribable port
        /// (for logging & debugging)
        /// </summary>
        private string Name;

        /// <summary>
        /// The heartbeat interval.
        /// </summary>
        int HeartbeatInterval;

        /// <summary>
        /// The * PONG ... text
        /// </summary>
        byte[] PONG;

        /// <summary>
        /// Indicates if this port service is currently running.
        /// </summary>
        public bool Running { get { return mRunning; } }

        public bool Working { get; private set; }

        /// <summary>
        /// Get our server port. Used for testing when port 
        /// # is 0.
        /// </summary>
        public int ServerPort
        {
            get { return ((System.Net.IPEndPoint)mListener.LocalEndpoint).Port; }
        }

        /// <summary>
        /// Create an adapter. Defaults the heartbeat to 10 seconds and the 
        /// port to 7878
        /// </summary>
        /// <param name="name">human-friendly name for this source, like "SHDR" or "News"</param>
        /// <param name="aPort">The optional port number (default: 7878)</param>
        /// <param name="heartbeat">time (ms) before dropping an idle connection. 0 = never.</param>
        public MultiClientStreamPort(string name, int port, int heartbeat = 0)
        {
            this.Name = name;
            this.Port = port;
            Heartbeat = heartbeat;
            Working = false;
        }

        /// <summary>
        /// called when a new client has been accepted.
        /// Derive and override to handle.
        /// </summary>
        /// <param name="client">The new client (as a stream)</param>
        public virtual void OnNewClient(Stream client)
        {
        }

        /// <summary>
        /// A flag to indicate the adapter is still running.
        /// </summary>
        private bool mRunning = false;

        /// <summary>
        /// The server socket.
        /// </summary>
        private TcpListener mListener;

        /// <summary>
        /// The ascii encoder for creating the messages.
        /// </summary>
        ASCIIEncoding mEncoder = new ASCIIEncoding();

        /// <summary>
        /// The is the socket server listening thread. Creats a new client and 
        /// starts a heartbeat client thread to implement the ping/pong protocol.
        /// </summary>
        private void ListenForClients()
        {
            // this.mRunning is probably true at this point, but set it anyway
            mRunning = true;
            Thread.CurrentThread.Name = string.Format("ListenForClients({0})", Name);
            try
            {
                // Start listening on our port.
                // This will throw a SocketException if the port is in use
                mListener.Start();
                Console.WriteLine("{0} server: active on port {1}", this.Name, this.Port);
                while (mRunning)
                {
                    Working = true;
                    // block until a client has connected to the server
                    TcpClient tcpClient = mListener.AcceptTcpClient();
                    try
                    {
                        // get the underlying two-way stream.
                        // this can throw if the new client dropped the connection immediately!
                        Stream client = tcpClient.GetStream();
                        // New active client, add to collection
                        AddClient(client);
                        // create a thread to handle ping-pong with the new client 
                        new Thread(new ParameterizedThreadStart(HeartbeatThread)).Start(tcpClient);
                        // fire a client-connected event
                        FireConnectEvent(client);
                    } catch (Exception ex)
                    {
                        // uh-oh, something blew up...
                        Console.WriteLine("{0} server: client acceptance threw {1}", Name, ex.Message);
                        // pretend it didn't happen.
                        // (Can this throw too?)
						if (tcpClient != null) tcpClient.Close();
                        //MONO tcpClient?.Close();
                    }
                }
            }
            catch (Exception e)
            {
                mRunning = false;
                SocketException sox = e as SocketException;
                if (sox != null && sox.ErrorCode == 10004)
                {
                    // "A blocking operation was interrupted by a call to WSACancelBlockingCall"
                    Console.WriteLine("{0} server: connection-wait cancelled.", Name);
                }
                else
                {
                    Console.WriteLine("{0} server: {1}", Name, e.Message);
                }
            }
            Debug.Assert(!mRunning);
            Working = false;
            mListener.Stop();
            Console.WriteLine("{0} server: ListenForClients ended.", Name);
            Console.Out.Flush();
        }

        private void FireConnectEvent(Stream stream)
        {
            foreach (var handler in this.connectHandlers)
            {
                handler(stream);
            }
            OnNewClient(stream);
        }

        /// <summary>
        /// Start the listener thread.
        /// </summary>
        public void Start()
        {
            if (!mRunning)
            {
                mListener = new TcpListener(IPAddress.Any, Port);
                mListenThread = new Thread(ListenForClients);
                mListenThread.IsBackground = true;
                mListenThread.Start();
            }
        }

        /// <summary>
        /// Stop the listener thread and shutdown all client connections.
        /// </summary>
        public void Stop()
        {
            mRunning = false;
            // "close the listener. Any unaccepted connection requests in the
            // queue will be lost. Remote hosts waiting for a connection to
            // be accepted will throw a SocketException":
            try
            {
				if (mListener != null) {
					mListener.Stop();
				}
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0} server: ignoring mListener.Stop exception: {1}", this.Name, ex.Message);
            }
            // Wait some seconds for the client-listener thread to exit.
            if (mListenThread != null)
            {
                mListenThread.Join(4000);
                mListenThread = null;
            }
            // close all active clients
            foreach (var client in mClients.List)
            {
                DropClient(client);
            }
            if (mActiveClients.CurrentCount > 0)
            {
                try
                {
                    mActiveClients.Signal();
                }
                catch (InvalidOperationException)
                {
                    // ignore
                }
            }
            while (mActiveClients.CurrentCount > 0)
            {
                // Wait for all client threads to exit.
                Console.WriteLine("{0} server: waiting for {1} client threads to stop",
                    Name, mActiveClients.CurrentCount);
                mActiveClients.Wait(300);
            }
        }

        /// <summary>
        /// Send a string of text to all clients.
        /// </summary>
        /// <param name="line">A line of text</param>
        public void SendToAll(string line)
        {
            if (!line.EndsWith("\r\n"))
            {
                line += "\r\n";
            }
            Console.Write("{0}: {1}", Name, line);
            byte[] message = mEncoder.GetBytes(line.ToCharArray());
            foreach (Stream client in mClients.List)
            {
                lock (client)
                {
                    WriteToClient(client, message);
                }
            }
        }

        public void WriteToClient(Stream aClient, string line)
        {
            var message = mEncoder.GetBytes(line.ToCharArray());
            WriteToClient(aClient, message);
        }

        /// <summary>
        /// Send text to a client as a byte array. Handles execptions and 
        /// remove the client from the list of clients if the write fails. 
        /// Also makes sure the client connection is closed when it fails.
        /// </summary>
        /// <param name="client">The client to send the message to</param>
        /// <param name="aMessage">The message</param>
        protected void WriteToClient(Stream client, byte[] aMessage)
        {
            try
            {
                client.Write(aMessage, 0, aMessage.Length);
            }
#pragma warning disable 0168
            catch (Exception e)
#pragma warning restore 0168
            {
#if !DEBUG
                Console.WriteLine("{0} server client-write, {1}", Name, e.Message);
#endif
                DropClient(client);
            }
        }

        /// <summary>
        /// Add a client (bidirectional TCP stream) to the active clients collection
        /// </summary>
        /// <param name="client">client/stream to add</param>
        private void AddClient(Stream client)
        {
            mClients.Add(client);
            Console.WriteLine("{0} server: new client! clients => {1}", Name, mClients.Count);
        }

        /// <summary>
        /// Drop an active client connection by removing the client/stream from the clients collection and closing/disposing the client
        /// (which closes any underlying socket)
        /// </summary>
        /// <remarks>Has no effect if the client is not in the active client table</remarks>
        /// <param name="client">the client stream to drop</param>
        private void DropClient(Stream client)
        {
            if (mClients.Remove(client))
            {
                //Console.WriteLine("{0} server: Dropping client...", Name);
                try
                {
                    lock (client)
                    {
                        client.Dispose();
                    }
                }
                catch (Exception f)
                {
                    Console.WriteLine("{0} server: ignoring client.Dispose exception: {1}", Name, f.Message);
                }
                Console.WriteLine("{0} server: clients => {1}", Name, mClients.Count);
            }
        }

        /// <summary>
        /// Flush all the communications to all the clients
        /// </summary>
        public void FlushAll()
        {
            foreach (Stream client in mClients.List)
            {
                try
                {
                    client.Flush();
                } catch (Exception ex)
                {
                    Console.WriteLine("{0} server: dropping client after Flush exception {1}", this.Name, ex.Message);
                    DropClient(client);
                }
            }
        }

        /// <summary>
        /// Receive data from a client and implement heartbeat ping/pong protocol.
        /// </summary>
        /// <param name="aClient">The client who sent the text</param>
        /// <param name="aLine">The line of text</param>
        private bool Receive(Stream aClient, String aLine)
        {
            bool heartbeat = false;
            if (aLine.StartsWith("* PING") && HeartbeatInterval > 0)
            {
                heartbeat = true;
                lock (aClient)
                {
                    // Console.WriteLine("Received PING, sending PONG");
                    WriteToClient(aClient, PONG);
                    aClient.Flush();
                }
            }

            return heartbeat;
        }

        /// <summary>
        /// This is a method to set the heartbeat interval given in milliseconds.
        /// </summary>
        private int Heartbeat
        {
            get { return HeartbeatInterval; }
            set
            {
                HeartbeatInterval = value;
                ASCIIEncoding encoder = new ASCIIEncoding();
                PONG = encoder.GetBytes("* PONG " + HeartbeatInterval.ToString() + "\n");
            }
        }

        /// <summary>
        /// The heartbeat thread for a client. This thread receives data from a client, 
        /// closes the socket when it fails, and handles communication timeouts when 
        /// the client does not send a heartbeat within 2x the heartbeat frequency. 
        /// 
        /// When the heartbeat is not received, the client is assumed to be unresponsive
        /// and the connection is closed. Waits for one ping to be received before
        /// enforcing the timeout. 
        /// </summary>
        /// <param name="client">The client we are communicating with.</param>
        private void HeartbeatThread(object obj)
        {
            Thread.CurrentThread.Name = string.Format("{0} server (Heartbeat)", Name);
            TcpClient tcpClient = (TcpClient)obj;
            NetworkStream client = null;
            mActiveClients.AddCount();
            try
            {
                // get the bidirectional stream associated with the TCP connection.
                // NB this can throw, if the connection has already been lost.
                client = tcpClient.GetStream();
                ArrayList readList = new ArrayList();
                bool heartbeatActive = false;

                byte[] message = new byte[4096];
                ASCIIEncoding encoder = new ASCIIEncoding();
                int length = 0;

                while (mRunning && tcpClient.Connected)
                {
                    try
                    {
                        readList.Clear();
                        readList.Add(tcpClient.Client);
                        if (HeartbeatInterval > 0 && heartbeatActive)
                        {
                            // * 2 for twice the heartbeat interval,
                            // * 1000 because it's in mics 
                            Socket.Select(readList, null, null, HeartbeatInterval * 2 * 1000);
                        }
                        if (readList.Count == 0 && heartbeatActive)
                        {
                            Console.WriteLine("{0} server: timeout, closing connection", Name);
                            break;
                        }

                        // blocks until a client sends a message OR
                        // the client disconnects or the socket fails
                        int bytesRead = client.Read(message, length, 4096 - length);
                        if (bytesRead == 0)
                        {
                            // the client has disconnected (or connection broke?)
                            Console.WriteLine("{0} server: client disconnected (0 bytes read)", Name);
                            break;
                        }
                        // See if we have a line
                        int pos = length;
                        length += bytesRead;
                        int eol = 0;
                        for (int i = pos; i < length; i++)
                        {
                            if (message[i] == '\n')
                            {
                                String line = encoder.GetString(message, eol, i);
                                if (Receive(client, line)) heartbeatActive = true;
                                eol = i + 1;
                            }
                        }

                        // Remove the lines that have been processed.
                        if (eol > 0)
                        {
                            length = length - eol;
                            // Shift the message array to remove the lines.
                            if (length > 0)
                                Array.Copy(message, eol, message, 0, length);
                        }
                    }
                    catch (Exception e)
                    {
                        // a socket error has occured
                        Console.WriteLine("{0} server: {1}", Name, e.Message);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("{0} server: {1}", Name, e.Message);
            }
            finally
            {
                try
                {
                    DropClient(client);
					if (tcpClient != null) {
                        tcpClient.Close();
					}
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0} server: client close exception: {1}", Name, e.Message);
                }
                // Signal that this thread is ending
                mActiveClients.Signal();
            }
            Console.WriteLine("{0} server: HeartbeatClient thread exit.", this.Name);
        }
    }
}
