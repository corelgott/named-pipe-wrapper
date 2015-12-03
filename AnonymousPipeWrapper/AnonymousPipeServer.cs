namespace AnonymousPipeWrapper
{
    using System.IO.Pipes;

    /// <summary>
    /// Wraps a <see cref="NamedPipeServerStream"/> and provides multiple simultaneous client connection handling.
    /// </summary>
    /// <typeparam name="TReadWrite">Reference type to read from and write to the named pipe</typeparam>
    public class AnonymousPipeServer<TReadWrite> : Server<TReadWrite, TReadWrite> where TReadWrite : class
    {
        /// <summary>
        /// Constructs a new <c>AnonymousPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        public AnonymousPipeServer(string pipeName)
            : base(pipeName)
        {
        }

        /// <summary>
        /// Constructs a new <c>AnonymousPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        /// <param name="bufferSize">Size of input and output buffer</param>
        /// <param name="security">And object that determine the access control and audit security for the pipe</param>
        public AnonymousPipeServer(string pipeName, int bufferSize, PipeSecurity security)
            : base(pipeName, bufferSize, security)
        { }
    }
}
