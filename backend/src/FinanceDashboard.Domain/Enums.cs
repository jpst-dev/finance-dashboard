namespace FinanceDashboard.Domain;

public enum AccountType { Checking, CreditCard, Wallet }
public enum CategoryKind { Expense, Income }
public enum TransactionState { Normal, PendingClassification }
public enum TransactionOrigin { Manual, Imported }
public enum CheckpointOrigin { LedgerBal, Manual }
public enum ImportSource { NubankChecking, NubankCard, ItauChecking, ItauCard }
public enum CardKind { Physical, Virtual, VirtualRecurring, Other }
public enum ImportBatchState { Preview, Confirmed, Undone }
