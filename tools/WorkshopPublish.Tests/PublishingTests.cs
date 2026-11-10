using System.Text.Json;

public sealed class PublishingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tuck-publish-test-").FullName;
    private readonly FakeWorkshop _steam = new();
    private string SettingsPath => Path.Combine(_root, "workshop.json");
    private string Output => Path.Combine(_root, "output");
    private string Receipt => Path.Combine(Output, "publishedfileid.txt");
    private string ImagePath => Path.Combine(_root, "image.png");
    private string Readme => Path.Combine(_root, "description.md");

    public PublishingTests()
    {
        File.WriteAllBytes(ImagePath, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        File.WriteAllText(Readme, "# Tuck\n\nDescription.\n\n![demo](image.png)\n");
        Directory.CreateDirectory(Path.Combine(_root, "mod"));
        File.WriteAllText(Path.Combine(_root, "mod", "manifest.xml"), "<Manifest />");
        WriteSettings(42);
    }

    [Fact]
    public void PreparationIsOfflineAndDoesNotCreateAnItem()
    {
        WriteSettings(0);
        Run("prepare", () => throw new Exception("Preparation must not connect to Steam."));
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
        string text = File.ReadAllText(Path.Combine(Output, "description.txt"));
        Assert.Contains("Description.", text);
        Assert.DoesNotContain("[img]", text);
    }

    [Fact]
    public void CheckDoesNotUploadMissingImagesOrPublish()
    {
        Run("check");
        Assert.Equal(["query"], _steam.Calls);
    }

    [Fact]
    public void NewItemIsSavedBeforeImagesAndPublishedWithTheirUrlsInOneRun()
    {
        WriteSettings(0);
        _steam.BeforeUpload = () =>
        {
            Assert.Equal(42UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
            Assert.False(File.Exists(Receipt));
        };
        Run("publish");
        Assert.Equal(["create", "query", "images", "query", "publish"], _steam.Calls);
        Assert.Contains("[img]https://example.com/", _steam.Description);
        Assert.Equal(_steam.Description, File.ReadAllText(Path.Combine(Output, "description.txt")));
        Assert.Equal(WorkshopVisibility.Private, _steam.Settings!.Visibility);
        Assert.Equal("Test release", _steam.Settings.ChangeNote);
    }

    [Fact]
    public void PendingAgreementDoesNotPreventSavingTheIdOrPublishing()
    {
        WriteSettings(0);
        _steam.NeedsAgreement = true;
        Run("publish");
        Assert.Equal(42UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
        Assert.Equal(["create", "query", "images", "query", "publish"], _steam.Calls);
        Assert.NotNull(_steam.Description);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AgreementFlagOnlyProducesAnActionableWarning(bool needsAgreement)
    {
        using var warnings = new StringWriter();
        var previousError = Console.Error;
        try
        {
            Console.SetError(warnings);
            SteamWorkshop.WarnIfAgreementRequired(42, needsAgreement);
        }
        finally { Console.SetError(previousError); }
        if (needsAgreement)
        {
            Assert.Contains("Warning:", warnings.ToString());
            Assert.Contains("https://steamcommunity.com/sharedfiles/filedetails/?id=42", warnings.ToString());
            Assert.Contains("does not require re-uploading", warnings.ToString());
        }
        else Assert.Equal("", warnings.ToString());
    }

    [Fact]
    public void RetryAfterContentFailureReusesTheItemAndUploadedImages()
    {
        WriteSettings(0);
        _steam.FailPublish = true;
        Assert.Throws<IOException>(() => Run("publish"));
        _steam.Calls.Clear();
        _steam.FailPublish = false;
        Run("publish");
        Assert.Equal(["query", "publish"], _steam.Calls);
    }

    [Fact]
    public void RecoveryReceiptRestoresIdWithoutCreatingAnotherItem()
    {
        WriteSettings(0);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Receipt, "42\n");
        Run("publish");
        Assert.Equal(42UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.DoesNotContain("create", _steam.Calls);
        Assert.False(File.Exists(Receipt));
    }

    [Fact]
    public void ResettingIdAfterSuccessfulCreationCreatesANewItem()
    {
        WriteSettings(0);
        Run("publish");
        WriteSettings(0);
        _steam.ItemId = 43;
        _steam.Calls.Clear();
        _steam.Urls.Clear();
        Run("publish");
        Assert.Equal("create", _steam.Calls[0]);
        Assert.Equal(43UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
    }

    [Fact]
    public void ReceiptSurvivesFailedSettingsWriteAndIsConsumedOnRetry()
    {
        WriteSettings(0);
        Directory.CreateDirectory(SettingsPath + ".tmp");
        Assert.Throws<UnauthorizedAccessException>(() => Run("publish"));
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.Equal("42", File.ReadAllText(Receipt).Trim());

        Directory.Delete(SettingsPath + ".tmp");
        _steam.Calls.Clear();
        Run("publish");
        Assert.DoesNotContain("create", _steam.Calls);
        Assert.Equal(42UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
    }

    [Theory]
    [InlineData("publish", false)]
    [InlineData("check", true)]
    public void AlreadySavedReceiptIsOnlyRemovedWhenPublishing(string mode, bool receiptRemains)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Receipt, "42\n");
        Run(mode);
        Assert.Equal(receiptRemains, File.Exists(Receipt));
    }

    [Fact]
    public void CheckDoesNotSaveARecoveredId()
    {
        WriteSettings(0);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Receipt, "42\n");
        Run("check");
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.True(File.Exists(Receipt));
        Assert.Equal(["query"], _steam.Calls);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("publish")]
    public void DescriptionEstimateCountsEveryImageOccurrenceBeforeConnecting(string mode)
    {
        WriteSettings(0);
        File.WriteAllText(Readme, new string('x', 7000) + "\n\n"
            + string.Concat(Enumerable.Repeat("![image](image.png)\n\n", 4)));
        var error = Assert.Throws<InvalidDataException>(() => Run(mode, () => throw new Exception("Must not connect.")));
        Assert.Contains("Estimated description", error.Message);
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
    }

    [Fact]
    public void LongerThanEstimatedSteamUrlsStillPreventOversizedPublication()
    {
        File.WriteAllText(Readme, new string('x', 7400) + "\n\n![image](image.png)\n");
        _steam.ImageUrlOverride = "https://example.com/" + new string('x', 1000);
        var error = Assert.Throws<InvalidDataException>(() => Run("publish"));
        Assert.Contains("Rendered description", error.Message);
        Assert.Equal(["query", "images", "query"], _steam.Calls);
    }

    [Fact]
    public void DescriptionLimitCountsUtf8BytesRatherThanCharacters()
    {
        File.WriteAllText(Readme, new string('\u00e9', 4000));
        Assert.Throws<InvalidDataException>(() => Run("publish", () => throw new Exception("Must not connect.")));
    }

    [Theory]
    [InlineData("title", 64, false)]
    [InlineData("title", 65, true)]
    [InlineData("changeNote", 3999, false)]
    [InlineData("changeNote", 4000, true)]
    public void MetadataLimitsAreValidatedOfflineInUtf8Bytes(string field, int characters, bool tooLong)
    {
        string value = new('\u00e9', characters);
        var settings = WorkshopSettings.Load(SettingsPath);
        settings = field == "title" ? settings with { Title = value } : settings with { ChangeNote = value + "x" };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, WorkshopSettings.JsonOptions));
        void Prepare() => Run("prepare", () => throw new Exception("Must not connect."));
        if (tooLong) Assert.Throws<InvalidDataException>(Prepare);
        else Prepare();
    }

    [Fact]
    public void ConflictingRecoveryIdStopsBeforeConnecting()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Receipt, "99\n");
        Assert.Contains("ID conflict", Assert.Throws<InvalidOperationException>(() =>
            Run("publish", () => throw new Exception("Must not connect."))).Message);
        Assert.Equal(42UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(4348910, 8)]
    public void WrongGameOrOwnerPreventsAllUpdates(uint appId, ulong ownerId)
    {
        _steam.AppId = appId;
        _steam.OwnerId = ownerId;
        Assert.Throws<InvalidOperationException>(() => Run("publish"));
        Assert.Equal(["query"], _steam.Calls);
    }

    [Fact]
    public void IdenticalImageBytesAreUploadedOnceButResolveBothPaths()
    {
        File.Copy(ImagePath, Path.Combine(_root, "copy.png"));
        File.AppendAllText(Readme, "\n![copy](copy.png)\n");
        Run("publish");
        Assert.Equal(1, _steam.UploadedImageCount);
        Assert.Equal(2, _steam.Description!.Split("[img]").Length - 1);
    }

    [Fact]
    public void MissingImageUrlAfterUploadPreventsPublishingIncompleteDescription()
    {
        _steam.ReturnUploadedUrls = false;
        Assert.Contains("not returned a URL", Assert.Throws<InvalidOperationException>(() => Run("publish")).Message);
        Assert.DoesNotContain("publish", _steam.Calls);
    }

    [Fact]
    public void SettingsChangedDuringCreationAreNotOverwritten()
    {
        WriteSettings(0);
        _steam.BeforeCreate = () => WriteSettings(99);
        Assert.Contains("ID conflict", Assert.Throws<InvalidOperationException>(() => Run("publish")).Message);
        Assert.Equal(99UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.Equal("42", File.ReadAllText(Receipt).Trim());
        Assert.Equal(["create"], _steam.Calls);
    }

    [Fact]
    public void InvalidLocalInputFailsBeforeCreatingAnItem()
    {
        WriteSettings(0);
        File.WriteAllText(Readme, "Description.\n\n![missing](missing.png)");
        Assert.Throws<ArgumentException>(() => Run("publish", () => throw new Exception("Must not connect.")));
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
    }

    private void WriteSettings(ulong id) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
        new WorkshopSettings { PublishedFileId = id, Title = "Tuck", Visibility = WorkshopVisibility.Private, ChangeNote = "Test release" },
        WorkshopSettings.JsonOptions));

    private void Run(string mode, Func<IWorkshopClient>? connect = null) => WorkshopPublisher.Run(mode,
        SettingsPath, Readme, Path.Combine(_root, "mod"), ImagePath, Output, connect ?? (() => _steam));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeWorkshop : IWorkshopClient
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, string> Urls { get; } = new(StringComparer.OrdinalIgnoreCase);
        public uint AppId = WorkshopPublisher.AppId;
        public ulong OwnerId = 7;
        public ulong ItemId = 42;
        public ulong UserId => 7;
        public bool NeedsAgreement, FailPublish;
        public bool ReturnUploadedUrls = true;
        public int UploadedImageCount;
        public string? Description;
        public string? ImageUrlOverride;
        public WorkshopSettings? Settings;
        public Action? BeforeCreate, BeforeUpload;

        public ulong CreateItem()
        {
            Calls.Add("create");
            BeforeCreate?.Invoke();
            SteamWorkshop.WarnIfAgreementRequired(ItemId, NeedsAgreement);
            return ItemId;
        }

        public WorkshopItem Query(ulong id)
        {
            Assert.Equal(ItemId, id);
            Calls.Add("query");
            return new(AppId, OwnerId, new Dictionary<string, string>(Urls));
        }

        public void UploadImages(ulong id, IReadOnlyList<WorkshopImage> images)
        {
            Assert.Equal(ItemId, id);
            BeforeUpload?.Invoke();
            Calls.Add("images");
            UploadedImageCount += images.Count;
            if (ReturnUploadedUrls)
                foreach (var image in images) Urls.Add(image.Name, ImageUrlOverride ?? "https://example.com/" + image.Name);
        }

        public void Publish(ulong id, WorkshopSettings settings, string contentFolder, string previewFile, string description)
        {
            Assert.Equal(ItemId, id);
            Assert.True(File.Exists(Path.Combine(contentFolder, "manifest.xml")));
            Assert.True(File.Exists(previewFile));
            Calls.Add("publish");
            if (FailPublish) throw new IOException("Simulated upload failure.");
            Description = description;
            Settings = settings;
        }

        public void Dispose() { }
    }
}
