namespace AnonymousPipeWrapper
{
    using System;
    using System.IO.Pipes;
    using System.Threading;

    using AnonymousPipeWrapper.Threading;

    /// <summary>
    /// Wraps a <see cref="NamedPipeClientStream"/>.
    /// </summary>
    /// <typeparam name="TReadWrite">Reference type to read from and write to the named pipe</typeparam>
    public class AnonymousPipeClient<TReadWrite> : AnonymousPipeClient<TReadWrite, TReadWrite> where TReadWrite : class
    {
        /// <summary>
        /// Constructs a new <c>AnonymousPipeClient</c> to connect to the <see cref="NamedPipeNamedPipeServer{TReadWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        public AnonymousPipeClient(string pipeName) : base(pipeName)
        {
        }
    }

    /// <summary>
    /// Wraps a <see cref="NamedPipeClientStream"/>.
    /// </summary>
    /// <typeparam name="TRead">Reference type to read from the named pipe</typeparam>
    /// <typeparam name="TWrite">Reference type to write to the named pipe</typeparam>
    public class AnonymousPipeClient<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        /// <summary>
        /// Gets or sets whether the client should attempt to reconnect when the pipe breaks
        /// due to an error or the other end terminating the connection.
        /// Default value is <c>true</c>.
        /// </summary>
        public bool AutoReconnect { get; set; }

        /// <summary>
        /// Gets or sets how long the client waits between a reconnection attempt.
        /// Default value is <c>0</c>.
        /// </summary>
        public int AutoReconnectDelay { get; set; }



        /// <summary>
        /// Invoked whenever a message is received from the server.
        /// </summary>
        public event ConnectionMessageEventHandler<TRead, TWrite> ServerMessage;

        /// <summary>
        /// Invoked when the client disconnects from the server (e.g., the pipe is closed or broken).
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> Disconnected;

        /// <summary>
        /// Invoked whenever an exception is thrown during a read or write operation on the named pipe.
        /// </summary>
        public event PipeExceptionEventHandler Error;

        private readonly string pipeName;
        private AnonymousPipeConnection<TRead, TWrite> pipeConnection;

        private readonly AutoResetEvent connected = new AutoResetEvent(false);
        private readonly AutoResetEvent disconnected = new AutoResetEvent(false);

        private volatile bool closedExplicitly;

        /// <summary>
        /// Constructs a new <c>AnonymousPipeClient</c> to connect to the <see cref="AnonymousPipeServer{TReadWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        public AnonymousPipeClient(string pipeName)
        {
            this.pipeName = pipeName;
            this.AutoReconnect = true;
        }

        /// <summary>
        /// Connects to the named pipe server asynchronously.
        /// This method returns immediately, possibly before the connection has been established.
        /// </summary>
        public void Start(bool waitForconnection = false)
        {
            this.closedExplicitly = false;
            var worker = new Worker();
            worker.Error += this.OnError;
            worker.DoWork(this.ListenSync);
        }

        /// <summary>
        ///     Sends a message to the server over a named pipe.
        /// </summary>
        /// <param name="message">Message to send to the server.</param>
        public void PushMessage(TWrite message)
        {
            if (this.pipeConnection != null) this.pipeConnection.PushMessage(message);
        }

        /// <summary>
        /// Closes the named pipe.
        /// </summary>
        public void Stop()
        {
            this.closedExplicitly = true;
            if (this.pipeConnection != null) this.pipeConnection.Close();
        }

        #region Wait for connection/disconnection

        public void WaitForConnection()
        {
            this.connected.WaitOne();
        }

        public void WaitForConnection(int millisecondsTimeout)
        {
            this.connected.WaitOne(millisecondsTimeout);
        }

        public void WaitForConnection(TimeSpan timeout)
        {
            this.connected.WaitOne(timeout);
        }

        public void WaitForDisconnection()
        {
            this.disconnected.WaitOne();
        }

        public void WaitForDisconnection(int millisecondsTimeout)
        {
            this.disconnected.WaitOne(millisecondsTimeout);
        }

        public void WaitForDisconnection(TimeSpan timeout)
        {
            this.disconnected.WaitOne(timeout);
        }

        #endregion

        #region Private methods



        private void ListenSync()
        {
            // Get the name of the data pipe that should be used from now on by this AnonymousPipeClient
            var handshake = PipeClientFactory.Connect<string, string>(this.pipeName);
            var dataPipeName = handshake.ReadObject();
            handshake.Close();

            // Connect to the actual data pipe
            var dataPipe = PipeClientFactory.CreateAndConnectPipe(dataPipeName);

            // Create a Connection object for the data pipe
            this.pipeConnection = ConnectionFactory.CreateConnection<TRead, TWrite>(dataPipe);
            this.pipeConnection.Disconnected += this.OnDisconnected;
            this.pipeConnection.ReceiveMessage += this.OnReceiveMessage;
            this.pipeConnection.Error += this.ConnectionOnError;
            this.pipeConnection.Open();

            this.connected.Set();
        }

        private void OnDisconnected(AnonymousPipeConnection<TRead, TWrite> connection)
        {
            if (this.Disconnected != null) this.Disconnected(connection);

            this.disconnected.Set();

            // Reconnect
            if (this.AutoReconnect && !this.closedExplicitly)
            {
                Thread.Sleep(this.AutoReconnectDelay);
                this.Start();
            }
        }

        private void OnReceiveMessage(AnonymousPipeConnection<TRead, TWrite> connection, TRead message)
        {
            if (this.ServerMessage != null) this.ServerMessage(connection, message);
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

        #endregion
    }
}
