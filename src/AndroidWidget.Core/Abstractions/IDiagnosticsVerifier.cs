namespace AndroidWidget.Core.Abstractions;

public interface IDiagnosticsVerifier
{
    bool VerifyScrcpyBundle(out string details);
    bool VerifyCompanionBundle(out string details);
    bool VerifySmsParser();
    bool VerifyWirelessPairingParser();
}
