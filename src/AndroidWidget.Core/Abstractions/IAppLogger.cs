namespace AndroidWidget.Core.Abstractions;

public interface IAppLogger
{
    string FilePath { get; }
    void Write(string message);
}
