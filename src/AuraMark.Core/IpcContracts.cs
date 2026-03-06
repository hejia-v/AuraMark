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
    public const string E2eSetMarkdown = "E2eSetMarkdown";
    public const string ScrollToHeading = "ScrollToHeading";
    public const string SetImmersive = "SetImmersive";
    public const string InsertCodeBlock = "InsertCodeBlock";
    public const string SetTitle = "SetTitle";
}

public static class IpcLimits
{
    public const int MaxContentBytes = 10 * 1024 * 1024;
}

public static class ErrorCodes
{
    public const string SaveDenied = "E_SAVE_DENIED";
    public const string SaveIo = "E_SAVE_IO";
    public const string IpcParse = "E_IPC_PARSE";
    public const string SyncConflict = "E_SYNC_CONFLICT";
}

public sealed class HostCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name { get; set; } = string.Empty;

    public string? Content { get; set; }

    public int? Index { get; set; }

    public bool? Value { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryParse(string json, out HostCommand? command)
    {
        command = null;

        try
        {
            command = JsonSerializer.Deserialize<HostCommand>(json, JsonOptions);
            return command is not null && !string.IsNullOrWhiteSpace(command.Name);
        }
        catch
        {
            return false;
        }
    }
}
