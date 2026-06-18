namespace Spydersoft.FileStore.Contracts;

public enum FileStatus
{
    Pending = 0,
    Confirmed = 1,
    Deleted = 2,
}

public enum DocumentVersionStatus
{
    Pending = 0,
    Confirmed = 1,
    Deleted = 2,
}

public enum RetentionPolicy
{
    KeepAll = 0,
    KeepLatest = 1,
    KeepN = 2,
}
