namespace AndroidWidget.Core.Operations;

public sealed record OperationResult(int ExitCode, string StandardOutput = "", string StandardError = "")
{
    public bool IsSuccess => ExitCode == 0;

    public string BestMessage => string.IsNullOrWhiteSpace(StandardError)
        ? string.IsNullOrWhiteSpace(StandardOutput) ? $"Код завершения: {ExitCode}" : StandardOutput.Trim()
        : StandardError.Trim();

    public static OperationResult Success(string message = "") => new(0, message);

    public static OperationResult Failure(string message, int exitCode = 1) => new(exitCode, "", message);
}
