using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AndroidWidget.Core.Abstractions;
using AndroidWidget.Core.Devices;
using AndroidWidget.Core.Operations;

namespace AndroidWidget.Infrastructure.Windows;

public sealed class WindowsDesktopIntegration : IDesktopIntegration
{
    private const int MyComputerShellFolder = 17;

    public OperationResult OpenMtpDevice(AndroidDevice device)
    {
        object? shell = null;
        object? computer = null;
        object? items = null;
        var portableItems = new List<MtpShellItem>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return OperationResult.Failure("Windows Shell недоступен.");

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return OperationResult.Failure("Не удалось запустить Windows Shell.");

            computer = ((dynamic)shell).NameSpace(MyComputerShellFolder);
            if (computer is null)
                return OperationResult.Failure("Не удалось открыть «Этот компьютер».");

            items = ((dynamic)computer).Items();
            var count = (int)((dynamic)items).Count;
            for (var index = 0; index < count; index++)
            {
                object? item = ((dynamic)items).Item(index);
                if (item is null)
                    continue;

                dynamic shellItem = item;
                if (!(bool)shellItem.IsFolder || (bool)shellItem.IsFileSystem)
                {
                    ReleaseComObject(item);
                    continue;
                }

                var name = (string?)shellItem.Name ?? string.Empty;
                portableItems.Add(new MtpShellItem(item, name, MatchScore(name, device)));
            }

            var selected = portableItems
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            selected ??= portableItems.Count == 1 ? portableItems[0] : null;
            if (selected is null)
            {
                var detected = portableItems.Count == 0
                    ? "MTP-устройства не обнаружены."
                    : $"Обнаружены: {string.Join(", ", portableItems.Select(item => item.Name))}.";
                return OperationResult.Failure(
                    $"Windows не нашла MTP-объект «{device.DisplayName}». " +
                    "Подключите телефон по USB и выберите режим «Передача файлов (MTP)». " + detected);
            }

            ((dynamic)selected.ComObject).InvokeVerb();
            return OperationResult.Success($"Открыт MTP-корень «{selected.Name}».");
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"Не удалось открыть MTP-корень телефона: {ex.Message}");
        }
        finally
        {
            foreach (var item in portableItems)
                ReleaseComObject(item.ComObject);
            ReleaseComObject(items);
            ReleaseComObject(computer);
            ReleaseComObject(shell);
        }
    }

    public OperationResult OpenFile(string path) => OpenUri(path);

    public OperationResult OpenFolder(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    public OperationResult RevealFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return OperationResult.Failure("Файл не найден: " + fullPath);

            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return OperationResult.Failure("Папка файла не найдена: " + fullPath);

            // Opening the resolved directory is more reliable than Explorer's
            // /select syntax, whose parsing changes when a path contains spaces.
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static OperationResult OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private static int MatchScore(string shellName, AndroidDevice device)
    {
        var normalizedShellName = Normalize(shellName);
        if (normalizedShellName.Length == 0)
            return 0;

        var candidates = new[]
            {
                device.DisplayName,
                device.Model,
                $"{device.Manufacturer} {device.Model}",
                $"{device.Brand} {device.Model}",
                device.DeviceCode,
                device.Serial
            }
            .Select(Normalize)
            .Where(candidate => candidate.Length >= 3 && candidate != "android")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var best = 0;
        foreach (var candidate in candidates)
        {
            if (normalizedShellName == candidate)
                best = Math.Max(best, 1000 + candidate.Length);
            else if (normalizedShellName.Contains(candidate, StringComparison.Ordinal))
                best = Math.Max(best, 700 + candidate.Length);
            else if (candidate.Contains(normalizedShellName, StringComparison.Ordinal) && normalizedShellName.Length >= 5)
                best = Math.Max(best, 500 + normalizedShellName.Length);
        }
        return best;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private sealed record MtpShellItem(object ComObject, string Name, int Score);
}
