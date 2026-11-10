using System.Text.Json;
using System.Text.Json.Serialization;

internal enum WorkshopVisibility { Public, FriendsOnly, Private, Unlisted }

internal sealed record WorkshopSettings
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<WorkshopVisibility>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public required ulong PublishedFileId { get; init; }
    public required string Title { get; init; }
    public required WorkshopVisibility Visibility { get; init; }
    public required string ChangeNote { get; init; }

    public static WorkshopSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<WorkshopSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Workshop settings are empty.");
        if (string.IsNullOrWhiteSpace(settings.Title) || settings.ChangeNote == null || !Enum.IsDefined(settings.Visibility))
            throw new InvalidDataException("Workshop settings require a title, change note, and valid visibility.");
        WorkshopLimits.Validate("Title", settings.Title, WorkshopLimits.TitleMaxBytes);
        WorkshopLimits.Validate("Change note", settings.ChangeNote, WorkshopLimits.ChangeNoteMaxBytes);
        return settings;
    }

    public static void SaveId(string path, ulong id)
    {
        // Preserve metadata edited while Steam was creating the item.
        var current = Load(path);
        if (current.PublishedFileId == id) return;
        if (current.PublishedFileId != 0 || id == 0)
            throw new InvalidOperationException($"Workshop ID conflict: settings have {current.PublishedFileId}, recovered ID is {id}.");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(current with { PublishedFileId = id }, JsonOptions) + "\n");
        File.Move(temporary, path, overwrite: true);
        Console.WriteLine($"Saved Workshop ID {id} in {path}. Commit this change.");
    }
}
