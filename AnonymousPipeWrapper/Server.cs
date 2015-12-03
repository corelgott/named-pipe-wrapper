namespace AnonymousPipeWrapper
{
    using System;
    using System.Collections.Generic;
    using System.IO.Pipes;
    using System.Linq;

    using AnonymousPipeWrapper.IO;
    using AnonymousPipeWrapper.Threading;

    /// <summary>
    /// Wraps a <see cref="NamedPipeServerStream"/> and provides multiple simultaneous client connection handling.
    /// </summary>
    /// <typeparam name="TRead">Reference type to read from the named pipe</typeparam>
    /// <typeparam name="TWrite">Reference type to write to the named pipe</typeparam>
    public class Server<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        /// <summary>
        /// Invoked whenever a client connects to the server.
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> ClientConnected;

        /// <summary>
        /// Invoked whenever a client disconnects from the server.
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> ClientDisconnected;

        /// <summary>
        /// Invoked whenever a client sends a message to the server.
        /// </summary>
        public event ConnectionMessageEventHandler<TRead, TWrite> ClientMessage;

        /// <summary>
        /// Invoked whenever an exception is thrown during a read or write operation.
        /// </summary>
        public event PipeExceptionEventHandler Error;

        private readonly string pipeName;
        private readonly int bufferSize;
        private readonly PipeSecurity security;
        private readonly List<AnonymousPipeConnection<TRead, TWrite>> connections = new List<AnonymousPipeConnection<TRead, TWrite>>();

        private int nextPipeId;

        private volatile bool shouldKeepRunning;
        private volatile bool isRunning;

        /// <summary>
        /// Constructs a new <c>AnonymousPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        public Server(string pipeName)
        {
            this.pipeName = pipeName;
        }

        /// <summary>
        /// Constructs a new <c>AnonymousPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        /// <param name="bufferSize">Size of input and output buffer</param>
        /// <param name="security">And object that determine the access control and audit security for the pipe</param>
        public Server(string pipeName, int bufferSize, PipeSecurity security)
        {
            this.pipeName = pipeName;
            this.bufferSize = bufferSize;
            this.security = security;
        }

        /// <summary>
        /// Begins listening for client connections in a separate background thread.
        /// This method returns immediately.
        /// </summary>
        public void Start()
        {
            this.shouldKeepRunning = true;
            var worker = new Worker();
            worker.Error += this.OnError;
            worker.DoWork(this.ListenSync);
        }

        /// <summary>
        /// Sends a message to all connected clients asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        public void PushMessage(TWrite message)
        {
            lock (this.connections)
            {
                foreach (var client in this.connections)
                {
                    client.PushMessage(message);
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific client asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetId">Specific client ID to send to.</param>
        public void PushMessage(TWrite message, int targetId)
        {
            lock (this.connections)
            {
                // Can we speed this up with Linq or does that add overhead?
                foreach (var client in this.connections)
                {
                    if (client.Id == targetId)
                    {
                        client.PushMessage(message);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific clients asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetIds">A list of client ID's to send to.</param>
        public void PushMessage(TWrite message, List<int> targetIds)
        {
            lock (this.connections)
            {
                // Can we speed this up with Linq or does that add overhead?
                foreach (var client in this.connections)
                {
                    if (targetIds.Contains(client.Id))
                    {
                        client.PushMessage(message);
                    }
                }
            }
        }


        /// <summary>
        /// Sends a message to a specific clients asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetIds">An array of client ID's to send to.</param>
        public void PushMessage(TWrite message, int[] targetIds)
        {
            this.PushMessage(message, targetIds.ToList());
        }

        /// <summary>
        /// Sends a message to a specific client asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetName">Specific client name to send to.</param>
        public void PushMessage(TWrite message, string targetName)
        {
            lock (this.connections)
            {
                // Can we speed this up with Linq or does that add overhead?
                foreach (var client in this.connections)
                {
                    if (client.Name.Equals(targetName))
                    {
                        client.PushMessage(message);
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Sends a message to a specific client asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetNames">A list of client names to send to.</param>
        public void PushMessage(TWrite message, List<string> targetNames)
        {
            lock (this.connections)
            {
                foreach (var client in this.connections)
                {
                    if (targetNames.Contains(client.Name))
                    {
                        client.PushMessage(message);
                    }
                }
            }
        }


        /// <summary>
        /// Closes all open client connections and stops listening for new ones.
        /// </summary>
        public void Stop()
        {
            this.shouldKeepRunning = false;

            lock (this.connections)
            {
                foreach (var client in this.connections.ToArray())
                {
                    client.Close();
                }
            }

            // If background thread is still listening for a client to connect,
            // initiate a dummy connection that will allow the thread to exit.
            var dummyClient = new AnonymousPipeClient<TRead, TWrite>(this.pipeName);
            dummyClient.Start();
            dummyClient.WaitForConnection(TimeSpan.FromSeconds(2));
            dummyClient.Stop();
            dummyClient.WaitForDisconnection(TimeSpan.FromSeconds(2));
        }

        #region Private methods

        private void ListenSync()
        {
            this.isRunning = true;
            while (this.shouldKeepRunning) {
                this.WaitForConnection(); }
            this.isRunning = false;
        }

        private void WaitForConnection()
        {
            NamedPipeServerStream handshakePipe = null;
            NamedPipeServerStream dataPipe = null;
            AnonymousPipeConnection<TRead, TWrite> connection = null;

            var connectionPipeName = this.GetNextConnectionPipeName();

            try
            {
                // Send the client the name of the data pipe to use
                handshakePipe = this.CreateAndConnectPipe();
                var handshakeWrapper = new PipeStreamWrapper<string, string>(handshakePipe);
                handshakeWrapper.WriteObject(connectionPipeName);
                handshakeWrapper.WaitForPipeDrain();
                handshakeWrapper.Close();

                // Wait for the client to connect to the data pipe
                dataPipe = this.CreatePipe(connectionPipeName);
                dataPipe.WaitForConnection();

                // Add the client's connection to the list of connections
                connection = ConnectionFactory.CreateConnection<TRead, TWrite>(dataPipe);
                connection.ReceiveMessage += this.ClientOnReceiveMessage;
                connection.Disconnected += this.ClientOnDisconnected;
                connection.Error += this.ConnectionOnError;
                connection.Open();

                lock (this.connections) { this.connections.Add(connection); }

                this.ClientOnConnected(connection);
            }
                // Catch the IOException that is raised if the pipe is broken or disconnected.
            catch (Exception e)
            {
                Console.Error.WriteLine("Named pipe is broken or disconnected: {0}", e);

                Cleanup(handshakePipe);
                Cleanup(dataPipe);

                this.ClientOnDisconnected(connection);
            }
        }

        private NamedPipeServerStream CreateAndConnectPipe()
        {
            return this.security == null
                       ? PipeServerFactory.CreateAndConnectPipe(this.pipeName)
                       : PipeServerFactory.CreateAndConnectPipe(this.pipeName, this.bufferSize, this.security);
        }

        private NamedPipeServerStream CreatePipe(string connectionPipeName)
        {
            return this.security == null
                       ? PipeServerFactory.CreatePipe(connectionPipeName)
                       : PipeServerFactory.CreatePipe(connectionPipeName, this.bufferSize, this.security);
        }

        private void ClientOnConnected(AnonymousPipeConnection<TRead, TWrite> connection)
        {
            if (this.ClientConnected != null) this.ClientConnected(connection);
        }

        private void ClientOnReceiveMessage(AnonymousPipeConnection<TRead, TWrite> connection, TRead message)
        {
            if (this.ClientMessage != null) this.ClientMessage(connection, message);
        }

        private void ClientOnDisconnected(AnonymousPipeConnection<TRead, TWrite> connection)
        {
            if (connection == null)
                return;

            lock (this.connections)
            {
                this.connections.Remove(connection);
            }

            if (this.ClientDisconnected != null) this.ClientDisconnected(connection);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        private void ConnectionOnError(AnonymousPipeConnection<TRead, TWrite> connection, Exception exception)
        {
            this.OnError(exception);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        /// <param name="exception"></param>
        private void OnError(Exception exception)
        {
            if (this.Error != null) this.Error(exception);
        }

        private string GetNextConnectionPipeName()
        {
            return string.Format("{0}_{1}", this.pipeName, ++this.nextPipeId);
        }

        private static void Cleanup(NamedPipeServerStream pipe)
        {
            if (pipe == null) return;
            using (var x = pipe)
            {
                x.Close();
            }
        }

        #endregion
    }
}