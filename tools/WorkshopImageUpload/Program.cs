using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Steamworks;

// description <readme> <output> [<workshop-id>]: resolve/upload its images, then render BBCode.
// Without a Workshop ID, render an offline text-only preview without connecting to Steam.
// <workshop-id> <image> [--check-only]: resolve one image, or only report whether it exists.
bool descriptionMode = args.FirstOrDefault() == "description";
bool validArguments = descriptionMode ? args.Length is 3 or 4
    : args.Length == 2 || (args.Length == 3 && args[2] == "--check-only");
if (!validArguments)
{
    Console.Error.WriteLine("Usage: WorkshopImageUpload description <readme> <output> [<workshop-id>]\n"
        + "   or: WorkshopImageUpload <workshop-id> <image> [--check-only]");
    return 1;
}

const uint appId = 4348910;
bool initialized = false, submitted = false;
string? staging = null;
try
{
    WorkshopDescription? description = descriptionMode ? new(args[1]) : null;
    string? output = descriptionMode ? Path.GetFullPath(args[2]) : null;
    // Validate all Markdown before submitting any images.
    string? textOnly = description?.Render();
    if (descriptionMode && args.Length == 3)
    {
        File.WriteAllText(output!, textOnly!, new UTF8Encoding(false));
        Console.WriteLine("Prepared text-only description. Images are resolved from Steam during publishing.");
        return 0;
    }
    if (!ulong.TryParse(args[descriptionMode ? 3 : 0], out ulong itemId) || itemId == 0)
        throw new ArgumentException("An existing, nonzero Workshop item ID is required to upload images.");

    var paths = description?.Images ?? [Path.GetFullPath(args[1])];
    var images = paths.Select(path =>
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 16 || file.Length >= 1_000_000)
            throw new ArgumentException($"Image must exist and be between 16 bytes and 1 MB (exclusive): {path}");
        string extension = file.Extension.ToLowerInvariant();
        if (!new[] { ".png", ".jpg", ".jpeg", ".gif" }.Contains(extension))
            throw new ArgumentException($"Use a PNG, JPG or GIF image: {path}");
        if (extension == ".jpeg") extension = ".jpg";
        byte[] bytes = File.ReadAllBytes(path);
        string name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension;
        return (Path: path, Bytes: bytes, Name: name);
    }).ToArray();

    // Steam finds steam_appid.txt in the current directory. Resolve input paths first.
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    // Use the desktop client's logged-in account; the App ID is not a credential.
    var init = SteamAPI.InitEx(out string error);
    if (init != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
        throw new InvalidOperationException($"Steam initialization failed: {init}: {error}");
    initialized = true;
    if (SteamUtils.GetAppID().m_AppId != appId)
        throw new InvalidOperationException($"Steam initialized for unexpected app {SteamUtils.GetAppID()}.");
    Console.WriteLine($"Steam account: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()}); app {appId}.");

    var id = new PublishedFileId_t(itemId);
    var before = Query(id);
    Console.WriteLine($"Workshop item: {before.Details.m_rgchTitle} ({itemId}); {before.Previews.Count} image previews.");
    if (before.Details.m_nConsumerAppID.m_AppId != appId)
        throw new InvalidOperationException("Workshop item belongs to a different game.");
    if (before.Details.m_ulSteamIDOwner != SteamUser.GetSteamID().m_SteamID)
        throw new InvalidOperationException("The active Steam account does not own this Workshop item.");

    // Steam's original filenames are the index. No local upload history is needed.
    var urls = before.Previews.GroupBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Url, StringComparer.OrdinalIgnoreCase);
    var missing = images.DistinctBy(image => image.Name).Where(image => !urls.ContainsKey(image.Name)).ToArray();
    foreach (var image in images)
        Console.WriteLine($"{(urls.ContainsKey(image.Name) ? "Reusing" : "Missing")}: {Path.GetFileName(image.Path)} -> {image.Name}");
    if (!descriptionMode && args.Length == 3)
    {
        foreach (var image in images.Where(image => urls.ContainsKey(image.Name)))
            Console.WriteLine($"Steam image URL: {urls[image.Name]}");
        Console.WriteLine("Check complete. No changes submitted.");
        return 0;
    }

    if (missing.Length > 0)
    {
        staging = Directory.CreateTempSubdirectory("tuck-workshop-images-").FullName;
        var update = SteamUGC.StartItemUpdate(new AppId_t(appId), id);
        if (update == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Could not start the Workshop update.");
        foreach (var image in missing)
        {
            string staged = Path.Combine(staging, image.Name);
            File.WriteAllBytes(staged, image.Bytes);
            if (!SteamUGC.AddItemPreviewFile(update, staged, EItemPreviewType.k_EItemPreviewType_Image))
                throw new InvalidOperationException($"Steam rejected the image: {image.Path}");
        }
        Console.WriteLine($"Uploading {missing.Length} new image(s)...");
        var call = SteamUGC.SubmitItemUpdate(update, null);
        submitted = call != SteamAPICall_t.Invalid;
        var uploaded = Wait<SubmitItemUpdateResult_t>(call, TimeSpan.FromMinutes(2));
        if (uploaded.m_eResult != EResult.k_EResultOK)
            throw new InvalidOperationException($"Upload failed: {uploaded.m_eResult}.");
        Console.WriteLine("Steam confirmed the image upload.");
        if (uploaded.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            throw new InvalidOperationException($"Accept the Workshop agreement at https://steamcommunity.com/sharedfiles/filedetails/?id={itemId}, then retry.");
        foreach (var preview in Query(id).Previews) urls.TryAdd(preview.FileName, preview.Url);
    }

    var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var image in images)
    {
        if (!urls.TryGetValue(image.Name, out string? url))
            throw new InvalidOperationException($"Steam has not returned a URL for {image.Name}. Query with --check-only before retrying.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException($"Steam returned an invalid image URL: {url}");
        resolved.Add(image.Path, url);
        Console.WriteLine($"Steam image URL: {url}");
    }
    if (description != null)
        File.WriteAllText(output!, description.Render(resolved), new UTF8Encoding(false));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    if (submitted)
        Console.Error.WriteLine("An image update was submitted. Steam will be queried again on the next run.");
    return 1;
}
finally
{
    if (initialized) SteamAPI.Shutdown();
    if (staging != null) Directory.Delete(staging, recursive: true);
}

static T Wait<T>(SteamAPICall_t call, TimeSpan timeout) where T : struct
{
    if (call == SteamAPICall_t.Invalid) throw new InvalidOperationException("Steam returned an invalid API call.");
    bool complete = false, failed = false;
    T result = default;
    using var callback = CallResult<T>.Create((value, ioFailure) =>
    {
        result = value;
        failed = ioFailure;
        complete = true;
    });
    callback.Set(call);
    var timer = Stopwatch.StartNew();
    while (!complete)
    {
        if (timer.Elapsed >= timeout) throw new TimeoutException($"Steam timed out waiting for {typeof(T).Name}.");
        SteamAPI.RunCallbacks();
        if (!complete) Thread.Sleep(50);
    }
    if (failed) throw new IOException($"Steam reported an I/O failure for {typeof(T).Name}.");
    return result;
}

static (SteamUGCDetails_t Details, List<(string FileName, string Url)> Previews) Query(PublishedFileId_t id)
{
    var query = SteamUGC.CreateQueryUGCDetailsRequest([id], 1);
    if (query == UGCQueryHandle_t.Invalid) throw new InvalidOperationException("Could not create the Workshop query.");
    try
    {
        if (!SteamUGC.SetReturnAdditionalPreviews(query, true) || !SteamUGC.SetAllowCachedResponse(query, 0))
            throw new InvalidOperationException("Could not configure the Workshop query.");
        var response = Wait<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), TimeSpan.FromSeconds(30));
        if (response.m_eResult != EResult.k_EResultOK || response.m_unNumResultsReturned != 1)
            throw new InvalidOperationException($"Workshop query failed: {response.m_eResult}; {response.m_unNumResultsReturned} items."
                + (SteamUser.BLoggedOn() ? "" : " Steam has no backend connection; reconnect the desktop client."));
        if (!SteamUGC.GetQueryUGCResult(query, 0, out var details) || details.m_eResult != EResult.k_EResultOK)
            throw new InvalidOperationException("Could not read the Workshop item; check account access and item ID.");
        var previews = new List<(string FileName, string Url)>();
        for (uint i = 0; i < SteamUGC.GetQueryUGCNumAdditionalPreviews(query, 0); i++)
        {
            if (!SteamUGC.GetQueryUGCAdditionalPreview(query, 0, i, out string url, 4096,
                out string fileName, 1024, out var type))
                throw new InvalidOperationException($"Could not read additional preview {i}.");
            if (type == EItemPreviewType.k_EItemPreviewType_Image) previews.Add((fileName, url));
        }
        return (details, previews);
    }
    finally { SteamUGC.ReleaseQueryUGCRequest(query); }
}
