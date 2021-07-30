﻿using Silky.Transaction.Abstraction;

namespace Silky.Transaction.Configuration
{
    public class DistributedTransactionOptions
    {
        public static string DistributedTransaction = "DistributedTransaction";

        public TransRepositorySupport UndoLogRepository { get; set; } = TransRepositorySupport.Redis;

        public TransactionType TransactionType { get; set; } = TransactionType.Tcc;

        public int ScheduledRecoveryDelay { get; set; } = 30;

        public int ScheduledCleanDelay { get; set; } = 60;

        public int ScheduledPhyDeletedDelay { get; set; } = 600;

        public int ScheduledInitDelay { get; set; } = 10;

        public int RecoverDelayTime { get; set; } = 60;

        public int CleanDelayTime { get; set; } = 60;

        public int Limit { get; set; } = 100;

        public int RetryMax { get; set; } = 10;

        public bool PhyDeleted { get; set; }

        public int StoreDays { get; set; } = 3;
    }
}