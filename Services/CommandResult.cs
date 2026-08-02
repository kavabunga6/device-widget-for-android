namespace AndroidWidget.Services;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
    public string BestMessage => string.IsNullOrWhiteSpace(StandardError)
        ? StandardOutput.Trim()
        : StandardError.Trim();
}
