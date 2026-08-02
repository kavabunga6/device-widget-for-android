using AndroidWidget.Core.Operations;
using AndroidWidget.Core.Settings;

namespace AndroidWidget.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler? Changed;
    void Update(Func<AppSettings, AppSettings> update);
    OperationResult SetAutoStart(bool enabled);
}
