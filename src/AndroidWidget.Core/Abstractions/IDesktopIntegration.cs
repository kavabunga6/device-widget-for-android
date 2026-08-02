using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Core.Abstractions;

public interface IDesktopIntegration
{
    OperationResult OpenMtpDevice(AndroidDevice device);
    OperationResult OpenFile(string path);
    OperationResult OpenFolder(string path);
    OperationResult RevealFile(string path);
}
