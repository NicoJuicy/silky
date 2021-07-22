﻿using Silky.Rpc.Transport;

namespace Silky.Transaction
{
    public static class RpcContextExtensions
    {
        public static TransactionContext GetTransactionContext(this RpcContext rpcContext)
        {
            var transactionContext =
                rpcContext.GetAttachment(AttachmentKeys.TransactionContext) as TransactionContext;
            return transactionContext;
        }

        public static void SetTransactionContext(this RpcContext rpcContext, TransactionContext transactionContext)
        {
            rpcContext.SetAttachment(AttachmentKeys.TransactionContext, transactionContext);
        }
    }
}