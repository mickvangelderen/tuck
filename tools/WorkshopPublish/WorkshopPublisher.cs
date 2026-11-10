using System.Security.Cryptography;

internal sealed record WorkshopImage(string Path, string Name, byte[] Bytes)
{
    public static WorkshopImage Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 16 || file.Length >= 1_000_000)
            throw new ArgumentException($"Image must exist and be between 16 bytes and 1 MB (exclusive): {path}");
        string extension = file.Extension.ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif"))
            throw new ArgumentException($"Use a PNG, JPG or GIF image: {path}");
        if (extension == ".jpeg") extension = ".jpg";
        byte[] bytes = File.ReadAllBytes(path);
        return new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension, bytes);
    }
}

internal sealed record WorkshopItem(uint AppId, ulong OwnerId, IReadOnlyDictionary<string, string> ImageUrls);

internal interface IWorkshopClient : IDisposable
{
    ulong UserId { get; }
    ulong CreateItem();
    WorkshopItem Query(ulong id);
    void UploadImages(ulong id, IReadOnlyList<WorkshopImage> images);
    void Publish(ulong id, WorkshopSettings settings, string contentFolder, string previewFile, string description);
}

internal static class WorkshopPublisher
{
    public const uint AppId = 4348910;

    public static void Run(string mode, string settingsPath, string readmePath, string contentFolder,
        string previewFile, string outputFolder, Func<IWorkshopClient> connect)
    {
        // Resolve upload paths and validate inputs before connecting to Steam.
        settingsPath = Path.GetFullPath(settingsPath);
        contentFolder = Path.GetFullPath(contentFolder);
        previewFile = Path.GetFullPath(previewFile);
        outputFolder = Path.GetFullPath(outputFolder);
        var settings = WorkshopSettings.Load(settingsPath);
        if (!File.Exists(Path.Combine(contentFolder, "manifest.xml")))
            throw new InvalidDataException("The staged mod is missing manifest.xml.");
        _ = WorkshopImage.Load(previewFile);
        var description = new WorkshopDescription(readmePath);
        string textOnly = description.Render();
        var images = description.Images.Select(WorkshopImage.Load).ToArray();
        // Render placeholders so repeated images, BBCode tags, and spacing all count.
        string estimatedUrl = new('x', WorkshopLimits.EstimatedImageUrlBytes);
        var estimatedUrls = images.ToDictionary(image => image.Path, _ => estimatedUrl, StringComparer.OrdinalIgnoreCase);
        int estimatedBytes = WorkshopLimits.Validate("Estimated description", description.Render(estimatedUrls),
            WorkshopLimits.DescriptionMaxBytes);
        Console.WriteLine($"Estimated description: {estimatedBytes:N0}/{WorkshopLimits.DescriptionMaxBytes:N0} UTF-8 bytes "
            + $"(allowing {WorkshopLimits.EstimatedImageUrlBytes} bytes per image URL).");
        Directory.CreateDirectory(outputFolder);
        string descriptionPath = Path.Combine(outputFolder, "description.txt");
        File.WriteAllText(descriptionPath, textOnly);
        Console.WriteLine($"{settings.Title}: {settings.Visibility}; content: {contentFolder}");
        Console.WriteLine($"Description preview: {descriptionPath}");
        if (mode == "prepare") return;

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
        var urls = item.ImageUrls;
        var missing = images.DistinctBy(image => image.Name).Where(image => !urls.ContainsKey(image.Name)).ToArray();
        foreach (var image in images)
            Console.WriteLine($"{(urls.ContainsKey(image.Name) ? "Reusing" : "Missing")}: {Path.GetFileName(image.Path)}");
        if (mode == "check" && missing.Length > 0)
        {
            Console.WriteLine($"Check complete: {missing.Length} new image(s) needed. No changes submitted.");
            return;
        }
        if (missing.Length > 0)
        {
            steam.UploadImages(id, missing);
            urls = steam.Query(id).ImageUrls;
        }
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in images)
        {
            if (!urls.TryGetValue(image.Name, out string? url))
                throw new InvalidOperationException($"Steam has not returned a URL for {image.Name}. Run -CheckOnly before retrying.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException($"Steam returned an invalid image URL: {url}");
            resolved.Add(image.Path, url);
        }
        string rendered = description.Render(resolved);
        int actualBytes = WorkshopLimits.Validate("Rendered description", rendered, WorkshopLimits.DescriptionMaxBytes);
        Console.WriteLine($"Rendered description: {actualBytes:N0}/{WorkshopLimits.DescriptionMaxBytes:N0} UTF-8 bytes.");
        File.WriteAllText(descriptionPath, rendered);
        if (mode == "check")
        {
            Console.WriteLine("Check complete. No changes submitted.");
            return;
        }
        steam.Publish(id, settings, contentFolder, previewFile, rendered);
        Console.WriteLine($"Workshop upload confirmed: https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
    }

}
