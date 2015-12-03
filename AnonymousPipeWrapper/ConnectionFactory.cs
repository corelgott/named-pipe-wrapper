namespace AnonymousPipeWrapper
{
    using System.IO.Pipes;

    static class ConnectionFactory
    {
        private static int lastId;

        public static AnonymousPipeConnection<TRead, TWrite> CreateConnection<TRead, TWrite>(PipeStream pipeStream)
            where TRead : class
            where TWrite : class
        {
            return new AnonymousPipeConnection<TRead, TWrite>(++lastId, "Client " + lastId, pipeStream);
        }
    }
}