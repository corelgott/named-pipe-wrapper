
namespace AnonymousPipeWrapper.Threading
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    class Worker
    {
        private readonly TaskScheduler callbackThread;

        private static TaskScheduler CurrentTaskScheduler
        {
            get
            {
                return (SynchronizationContext.Current != null
                            ? TaskScheduler.FromCurrentSynchronizationContext()
                            : TaskScheduler.Default);
            }
        }

        public event WorkerSucceededEventHandler Succeeded;
        public event WorkerExceptionEventHandler Error;

        public Worker() : this(CurrentTaskScheduler)
        {
        }

        public Worker(TaskScheduler callbackThread)
        {
            this.callbackThread = callbackThread;
        }

        public void DoWork(Action action)
        {
            new Task(this.DoWorkImpl, action, CancellationToken.None, TaskCreationOptions.LongRunning).Start();
        }

        private void DoWorkImpl(object oAction)
        {
            var action = (Action)oAction;
            try
            {
                action();
                this.Callback(this.Succeed);
            }
            catch (Exception e)
            {
                this.Callback(() => this.Fail(e));
            }
        }

        private void Succeed()
        {
            if (this.Succeeded != null) this.Succeeded();
        }

        private void Fail(Exception exception)
        {
            if (this.Error != null) this.Error(exception);
        }

        private void Callback(Action action)
        {
            Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, this.callbackThread);
        }
    }

    internal delegate void WorkerSucceededEventHandler();
    internal delegate void WorkerExceptionEventHandler(Exception exception);
}
