using System.Diagnostics;
using System.Globalization;
using Steamworks;

internal sealed class SteamWorkshop : IWorkshopClient
{
    public ulong UserId => SteamUser.GetSteamID().m_SteamID;

    public SteamWorkshop()
    {
        // Identify the game only in this process; authentication comes from the desktop client.
        string appId = WorkshopPublisher.AppId.ToString(CultureInfo.InvariantCulture);
        Environment.SetEnvironmentVariable("SteamAppId", appId);
        Environment.SetEnvironmentVariable("SteamGameId", appId);
        var init = SteamAPI.InitEx(out string error);
        if (init != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            throw new InvalidOperationException($"Steam initialization failed: {init}: {error}. Keep Steam running and logged in.");
        if (SteamUtils.GetAppID().m_AppId != WorkshopPublisher.AppId)
        {
            SteamAPI.Shutdown();
            throw new InvalidOperationException("Steam initialized for an unexpected app.");
        }
        Console.WriteLine($"Steam account: {SteamFriends.GetPersonaName()} ({UserId}).");
    }

    public void Dispose() => SteamAPI.Shutdown();

    public ulong CreateItem()
    {
        var result = Wait<CreateItemResult_t>(SteamUGC.CreateItem(new AppId_t(WorkshopPublisher.AppId),
            EWorkshopFileType.k_EWorkshopFileTypeCommunity), TimeSpan.FromMinutes(2));
        if (result.m_eResult != EResult.k_EResultOK)
            throw new InvalidOperationException($"Workshop item creation failed: {result.m_eResult}.");
        ulong id = result.m_nPublishedFileId.m_PublishedFileId;
        WarnIfAgreementRequired(id, result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
        return id;
    }

    public WorkshopItem Query(ulong id)
    {
        var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(id)], 1);
        if (query == UGCQueryHandle_t.Invalid) throw new InvalidOperationException("Could not create the Workshop query.");
        try
        {
            Require(SteamUGC.SetReturnAdditionalPreviews(query, true) && SteamUGC.SetAllowCachedResponse(query, 0), "configure the Workshop query");
            var response = Wait<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), TimeSpan.FromSeconds(30));
            if (response.m_eResult != EResult.k_EResultOK || response.m_unNumResultsReturned != 1)
                throw new InvalidOperationException($"Workshop query failed: {response.m_eResult}; {response.m_unNumResultsReturned} items."
                    + (SteamUser.BLoggedOn() ? "" : " Reconnect the Steam desktop client."));
            if (!SteamUGC.GetQueryUGCResult(query, 0, out var details) || details.m_eResult != EResult.k_EResultOK)
                throw new InvalidOperationException("Could not read the Workshop item; check account access and item ID.");
            var images = new List<WorkshopPreview>();
            for (uint i = 0; i < SteamUGC.GetQueryUGCNumAdditionalPreviews(query, 0); i++)
            {
                if (!SteamUGC.GetQueryUGCAdditionalPreview(query, 0, i, out _, 4096,
                    out string name, 1024, out var type))
                    throw new InvalidOperationException($"Could not read additional preview {i}.");
                if (type == EItemPreviewType.k_EItemPreviewType_Image) images.Add(new(i, name));
            }
            Console.WriteLine($"Workshop item: {details.m_rgchTitle} ({id}); {images.Count} slideshow images.");
            return new(details.m_nConsumerAppID.m_AppId, details.m_ulSteamIDOwner, images);
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    public void Publish(ulong id, WorkshopSettings settings, string contentFolder, string previewFile, string description, string changeNote,
        WorkshopSlideshow slideshow)
    {
        var update = StartUpdate(id);
        Require(SteamUGC.SetItemTitle(update, settings.Title), "set the title");
        Require(SteamUGC.SetItemDescription(update, description), "set the description");
        Require(SteamUGC.SetItemContent(update, contentFolder), "set the content folder");
        Require(SteamUGC.SetItemPreview(update, previewFile), "set the preview image");
        string? staging = null;
        try
        {
            if (slideshow.Uploads.Length > 0)
                staging = Directory.CreateTempSubdirectory("tuck-workshop-slideshow-").FullName;
            foreach (var upload in slideshow.Uploads)
            {
                string path = Path.Combine(staging!, upload.Image.Name);
                File.WriteAllBytes(path, upload.Image.Bytes);
                bool accepted = upload.Index is uint index
                    ? SteamUGC.UpdateItemPreviewFile(update, index, path)
                    : SteamUGC.AddItemPreviewFile(update, path, EItemPreviewType.k_EItemPreviewType_Image);
                Require(accepted, $"upload slideshow image {upload.Image.Path}");
            }
            foreach (uint index in slideshow.Removals)
                Require(SteamUGC.RemoveItemPreview(update, index), $"remove slideshow image at index {index}");
            Console.WriteLine("Publishing mod content, metadata and slideshow...");
            Submit(update, id, changeNote);
        }
        finally
        {
            if (staging != null) Directory.Delete(staging, recursive: true);
        }
    }

    private static UGCUpdateHandle_t StartUpdate(ulong id)
    {
        var update = SteamUGC.StartItemUpdate(new AppId_t(WorkshopPublisher.AppId), new PublishedFileId_t(id));
        if (update == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Could not start the Workshop update.");
        return update;
    }

    private static void Submit(UGCUpdateHandle_t update, ulong id, string? changeNote)
    {
        var call = SteamUGC.SubmitItemUpdate(update, changeNote);
        if (call == SteamAPICall_t.Invalid) throw new InvalidOperationException("Steam rejected the Workshop submission.");
        SubmitItemUpdateResult_t result;
        try { result = Wait<SubmitItemUpdateResult_t>(call, TimeSpan.FromMinutes(10)); }
        catch
        {
            Console.Error.WriteLine($"An update was submitted for {id}, but its outcome is unknown. Check Steam before retrying.");
            throw;
        }
        if (result.m_eResult != EResult.k_EResultOK)
            throw new InvalidOperationException($"Workshop upload failed: {result.m_eResult}.");
        WarnIfAgreementRequired(id, result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
    }

    internal static void WarnIfAgreementRequired(ulong id, bool needsAgreement)
    {
        if (needsAgreement)
            Console.Error.WriteLine($"Warning: Accept the Steam Workshop agreement at https://steamcommunity.com/sharedfiles/filedetails/?id={id}. "
                + "The item remains hidden until acceptance. Accepting the agreement does not require re-uploading.");
    }

    private static void Require(bool success, string operation)
    {
        if (!success) throw new InvalidOperationException($"Steam could not {operation}.");
    }

    private static T Wait<T>(SteamAPICall_t call, TimeSpan timeout) where T : struct
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
        if (failed) throw new IOException("Steam reported an I/O failure.");
        return result;
    }
}
