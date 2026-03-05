using System.Text.Json;

namespace AuraMark.Core;

public static class IpcTypes
{
    public const string Init = "Init";
    public const string Update = "Update";
    public const string Command = "Command";
    public const string Ack = "Ack";
    public const string Error = "Error";

    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        Init,
        Update,
        Command,
        Ack,
        Error,
    };
}

public static class IpcCommands
{
    public const string ToggleSidebar = "ToggleSidebar";
    public const string ToggleSourceMode = "ToggleSourceMode";
    public const string FreezeInput = "FreezeInput";
    public const string ResumeInput = "ResumeInput";
    public const string ReplaceAll = "ReplaceAll";
    public const string ScrollToHeading = "ScrollToHeading";
    public const string SetImmersive = "SetImmersive";
    public const string InsertCodeBlock = "InsertCodeBlock";
}

public static class IpcLimits
{
    public const int MaxContentBytes = 10 * 1024 * 1024;
}

public sealed class HostCommand
{
    public string Name { get; set; } = string.Empty;

    public string? Content { get; set; }

    public int? Index { get; set; }

    public bool? Value { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static bool TryParse(string json, out HostCommand? command)
    {
        command = null;

        try
        {
            command = JsonSerializer.Deserialize<HostCommand>(json);
            return command is not null && !string.IsNullOrWhiteSpace(command.Name);
        }
        catch
        {
            return false;
        }
    }
}
