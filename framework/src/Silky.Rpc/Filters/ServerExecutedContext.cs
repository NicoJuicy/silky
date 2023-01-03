using System;
using System.Runtime.ExceptionServices;
using Silky.Rpc.Runtime.Server;

namespace Silky.Rpc.Filters
{
    public class ServerExecutedContext : ServiceEntryContext
    {
        private Exception? _exception;
        private ExceptionDispatchInfo? _exceptionDispatchInfo;

        public ServerExecutedContext(ServiceEntryContext context)
            : base(context)
        {
        }

        public virtual ExceptionDispatchInfo? ExceptionDispatchInfo
        {
            get { return _exceptionDispatchInfo; }

            set
            {
                _exception = null;
                _exceptionDispatchInfo = value;
            }
        }

        public virtual Exception? Exception
        {
            get
            {
                if (_exception == null && _exceptionDispatchInfo != null)
                {
                    return _exceptionDispatchInfo.SourceException;
                }
                else
                {
                    return _exception;
                }
            }

            set
            {
                _exceptionDispatchInfo = null;
                _exception = value;
            }
        }

        public virtual bool ExceptionHandled { get; set; }

        public virtual object Result { get; set; } = default;
    }
}