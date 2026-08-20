using DXNexus.Contracts;

namespace DXNexus.Bridge.Core;

public static class RemoteTunePolicy
{
    public static string? RejectionReason(
        RemoteTuneCommand command,
        SequencedRadioSnapshot? current,
        DateTimeOffset allowedUntil,
        DateTimeOffset now)
    {
        if (now >= allowedUntil)
            return "Browser tuning is locked. Enable it locally from the Bridge tray menu.";
        if (command.ExpiresAt <= now || command.ExpiresAt > now.AddSeconds(30))
            return "The tune command expired.";
        if (current is null || current.Sequence != command.ExpectedSequence || current.Radio.FrequencyHz != command.ExpectedFrequencyHz)
            return "SDR# changed after this command was created. Refresh and try again.";
        if (command.FrequencyHz is < 1_000 or > 2_000_000_000)
            return "Requested frequency is outside the supported safety bounds.";
        return null;
    }
}
