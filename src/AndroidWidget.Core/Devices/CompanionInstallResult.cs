using AndroidWidget.Core.Operations;

namespace AndroidWidget.Core.Devices;

public enum CompanionInstallFailureKind
{
    None,
    SignatureMismatch,
    Other
}

public sealed record CompanionInstallResult(OperationResult Operation, CompanionInstallFailureKind FailureKind)
{
    public bool IsSuccess => Operation.IsSuccess;
    public string BestMessage => Operation.BestMessage;

    public static CompanionInstallResult From(OperationResult operation) =>
        new(operation, operation.IsSuccess ? CompanionInstallFailureKind.None : CompanionInstallFailureKind.Other);
}
