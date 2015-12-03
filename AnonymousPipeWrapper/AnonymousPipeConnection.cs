namespace AnonymousPipeWrapper
{
    using System;
    using System.Collections.Generic;
    using System.IO.Pipes;
    using System.Runtime.InteropServices;
    using System.Runtime.Serialization;
    using System.Threading;

    using AnonymousPipeWrapper.IO;
    using AnonymousPipeWrapper.Threading;

    /// <summary>
    /// Represents a connection between a named pipe client and server.
    /// </summary>
    /// <typeparam name="TRead">Reference type to read from the named pipe</typeparam>
    /// <typeparam name="TWrite">Reference type to write to the named pipe</typeparam>
    public class AnonymousPipeConnection<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        /// <summary>
        /// Gets the connection's unique identifier.
        /// </summary>
        public readonly int Id;

        /// <summary>
        /// Gets the connection's name.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Gets the connection's handle.
        /// </summary>
        public readonly SafeHandle Handle;

        /// <summary>
        /// Gets a value indicating whether the pipe is connected or not.
        /// </summary>
        public bool IsConnected { get { return this.streamWrapper.IsConnected; } }

        /// <summary>
        /// Invoked when the named pipe connection terminates.
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> Disconnected;

        /// <summary>
        /// Invoked whenever a message is received from the other end of the pipe.
        /// </summary>
        public event ConnectionMessageEventHandler<TRead, TWrite> ReceiveMessage;

        /// <summary>
        /// Invoked when an exception is thrown during any read/write operation over the named pipe.
        /// </summary>
        public event ConnectionExceptionEventHandler<TRead, TWrite> Error;

        private readonly PipeStreamWrapper<TRead, TWrite> streamWrapper;

        private readonly AutoResetEvent writeSignal = new AutoResetEvent(false);
        private readonly Queue<TWrite> writeQueue = new Queue<TWrite>();

        private bool notifiedSucceeded;

        internal AnonymousPipeConnection(int id, string name, PipeStream serverStream)
        {
            this.Id = id;
            this.Name = name;
            this.Handle = serverStream.SafePipeHandle;
            this.streamWrapper = new PipeStreamWrapper<TRead, TWrite>(serverStream);
        }

        /// <summary>
        /// Begins reading from and writing to the named pipe on a background thread.
        /// This method returns immediately.
        /// </summary>
        public void Open()
        {
            var readWorker = new Worker();
            readWorker.Succeeded += this.OnSucceeded;
            readWorker.Error += this.OnError;
            readWorker.DoWork(this.ReadPipe);

            var writeWorker = new Worker();
            writeWorker.Succeeded += this.OnSucceeded;
            writeWorker.Error += this.OnError;
            writeWorker.DoWork(this.WritePipe);
        }

        /// <summary>
        /// Adds the specified <paramref name="message"/> to the write queue.
        /// The message will be written to the named pipe by the background thread
        /// at the next available opportunity.
        /// </summary>
        /// <param name="message"></param>
        public void PushMessage(TWrite message)
        {
            this.writeQueue.Enqueue(message);
            this.writeSignal.Set();
        }

        /// <summary>
        /// Closes the named pipe connection and underlying <c>PipeStream</c>.
        /// </summary>
        public void Close()
        {
            this.CloseImpl();
        }

        /// <summary>
        ///     Invoked on the background thread.
        /// </summary>
        private void CloseImpl()
        {
            this.streamWrapper.Close();
            this.writeSignal.Set();
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        private void OnSucceeded()
        {
            // Only notify observers once
            if (this.notifiedSucceeded)
                return;

            this.notifiedSucceeded = true;

            if (this.Disconnected != null) this.Disconnected(this);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        /// <param name="exception"></param>
        private void OnError(Exception exception)
        {
            if (this.Error != null) this.Error(this, exception);
        }

        /// <summary>
        ///     Invoked on the background thread.
        /// </summary>
        /// <exception cref="SerializationException">An object in the graph of type parameter <typeparamref name="TRead"/> is not marked as serializable.</exception>
        private void ReadPipe()
        {
            while (this.IsConnected && this.streamWrapper.CanRead)
            {
                var obj = this.streamWrapper.ReadObject();
                if (obj == null)
                {
                    this.CloseImpl();
                    return;
                }
                if (this.ReceiveMessage != null) this.ReceiveMessage(this, obj);
            }
        }

        /// <summary>
        ///     Invoked on the background thread.
        /// </summary>
        /// <exception cref="SerializationException">An object in the graph of type parameter <typeparamref name="TWrite"/> is not marked as serializable.</exception>
        private void WritePipe()
        {
            while (this.IsConnected && this.streamWrapper.CanWrite)
            {
                this.writeSignal.WaitOne();
                while (this.writeQueue.Count > 0)
                {
                    this.streamWrapper.WriteObject(this.writeQueue.Dequeue());
                    this.streamWrapper.WaitForPipeDrain();
                }
            }
        }
    }
}