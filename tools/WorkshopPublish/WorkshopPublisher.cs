internal sealed record WorkshopItem(uint AppId, ulong OwnerId, IReadOnlyList<WorkshopPreview> Images);

internal interface IWorkshopClient : IDisposable
{
    ulong UserId { get; }
    ulong CreateItem();
    WorkshopItem Query(ulong id);
    void Publish(ulong id, WorkshopSettings settings, string contentFolder, string previewFile, string description, string changeNote,
        WorkshopSlideshow slideshow);
}

internal static class WorkshopPublisher
{
    public const uint AppId = 4348910;

    public static void Run(string mode, string settingsPath, string readmePath, string changelogPath, string contentFolder,
        string previewFile, string outputFolder, Func<IWorkshopClient> connect, HttpClient? mediaClient = null)
    {
        // Resolve upload paths and validate inputs before connecting to Steam.
        settingsPath = Path.GetFullPath(settingsPath);
        contentFolder = Path.GetFullPath(contentFolder);
        previewFile = Path.GetFullPath(previewFile);
        outputFolder = Path.GetFullPath(outputFolder);
        var settings = WorkshopSettings.Load(settingsPath);
        if (!File.Exists(Path.Combine(contentFolder, "manifest.xml")))
            throw new InvalidDataException("The staged mod is missing manifest.xml.");
        string changeNote = WorkshopReleaseNotes.Load(changelogPath, Path.Combine(contentFolder, "manifest.xml"));
        WorkshopImage.ValidatePreview(previewFile);
        var slides = WorkshopSlideshow.Load(Path.Combine(Path.GetDirectoryName(settingsPath)!, "media", "slideshow"));
        var description = new WorkshopDescription(readmePath);
        var urls = GitHubMedia.Resolve(Path.GetDirectoryName(Path.GetFullPath(readmePath))!, description.Images);
        string rendered = description.Render(urls);
        int actualBytes = WorkshopLimits.Validate("Rendered description", rendered, WorkshopLimits.DescriptionMaxBytes);
        Console.WriteLine($"Rendered description: {actualBytes:N0}/{WorkshopLimits.DescriptionMaxBytes:N0} UTF-8 bytes.");
        Directory.CreateDirectory(outputFolder);
        string descriptionPath = Path.Combine(outputFolder, "description.txt");
        File.WriteAllText(descriptionPath, rendered);
        string changeNotePath = Path.Combine(outputFolder, "change-note.txt");
        File.WriteAllText(changeNotePath, changeNote);
        Console.WriteLine($"{settings.Title}; content: {contentFolder}");
        Console.WriteLine($"Description preview: {descriptionPath}");
        Console.WriteLine($"Change note preview: {changeNotePath}");
        if (mode == "prepare") return;

        using var defaultClient = mediaClient == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        GitHubMedia.VerifyAvailable(urls.Values, mediaClient ?? defaultClient!);

        string receiptPath = Path.Combine(outputFolder, "publishedfileid.txt");
        ulong id = settings.PublishedFileId;
        if (File.Exists(receiptPath))
        {
            if (!ulong.TryParse(File.ReadAllText(receiptPath).Trim(), out ulong recovered) || recovered == 0)
                throw new InvalidDataException($"Invalid recovery ID in {receiptPath}.");
            if (id != 0 && id != recovered)
                throw new InvalidOperationException($"Workshop ID conflict: settings have {id}, {receiptPath} has {recovered}.");
            id = recovered;
            if (mode == "publish")
            {
                WorkshopSettings.SaveId(settingsPath, id);
                File.Delete(receiptPath);
            }
        }
        if (mode == "check" && id == 0)
        {
            Console.WriteLine("No Workshop item exists yet. Publishing will create one. No changes submitted.");
            return;
        }

        using var steam = connect();
        if (id == 0)
        {
            id = steam.CreateItem();
            if (id == 0) throw new InvalidOperationException("Steam returned an empty Workshop ID.");
            Console.WriteLine($"Created Workshop item {id}. Recovery ID: {receiptPath}");
            // Record the ID before uploads, so retries reuse the item.
            File.WriteAllText(receiptPath, id + "\n");
            WorkshopSettings.SaveId(settingsPath, id);
            File.Delete(receiptPath);
        }

        var item = steam.Query(id);
        if (item.AppId != AppId) throw new InvalidOperationException("Workshop item belongs to a different game.");
        if (item.OwnerId != steam.UserId) throw new InvalidOperationException("The active Steam account does not own this Workshop item.");
        var slideshow = WorkshopSlideshow.Plan(item.Images, slides);
        if (mode == "check")
        {
            Console.WriteLine("Check complete. No changes submitted.");
            return;
        }
        steam.Publish(id, settings, contentFolder, previewFile, rendered, changeNote, slideshow);
        Console.WriteLine($"Workshop upload confirmed: https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
    }

}
