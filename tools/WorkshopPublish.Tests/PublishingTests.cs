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
    private string Changelog => Path.Combine(_root, "CHANGELOG.md");
    private string Manifest => Path.Combine(_root, "mod", "manifest.xml");

    public PublishingTests()
    {
        File.WriteAllBytes(ImagePath, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        File.WriteAllText(Readme, "# Tuck\n\nDescription.\n\n![demo](image.png)\n");
        Directory.CreateDirectory(Path.Combine(_root, "mod"));
        File.WriteAllText(Manifest, "<Manifest><ModVersion>1.0.0</ModVersion></Manifest>");
        File.WriteAllText(Changelog, "# Changelog\n\n## [Unreleased]\n\n## [1.0.0] - 2026-11-09\n\nTest release\n");
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
        Assert.Equal("[h2][1.0.0] - 2026-11-09[/h2]\n\nTest release\n",
            File.ReadAllText(Path.Combine(Output, "change-note.txt")));
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
        Assert.Equal(File.ReadAllText(Path.Combine(Output, "change-note.txt")), _steam.ChangeNote);
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
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void TitleLimitsAreValidatedOfflineInUtf8Bytes(int characters, bool tooLong)
    {
        string value = new('\u00e9', characters);
        var settings = WorkshopSettings.Load(SettingsPath);
        settings = settings with { Title = value };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, WorkshopSettings.JsonOptions));
        void Prepare() => Run("prepare", () => throw new Exception("Must not connect."));
        if (tooLong) Assert.Throws<InvalidDataException>(Prepare);
        else Prepare();
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("check")]
    [InlineData("publish")]
    public void UnreleasedChangesStopBeforeConnectingOrSavingAnId(string mode)
    {
        WriteSettings(0);
        File.WriteAllText(Changelog, "## [Unreleased]\n\n- Not released yet.\n\n## [1.0.0]\n\nReleased.");
        var error = Assert.Throws<InvalidDataException>(() => Run(mode, () => throw new Exception("Must not connect.")));
        Assert.Contains("Unreleased section must be empty", error.Message);
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("check")]
    [InlineData("publish")]
    public void VersionMismatchStopsBeforeConnectingEvenIfAnOlderReleaseMatches(string mode)
    {
        File.WriteAllText(Changelog, "## [Unreleased]\n\n## [1.1.0]\n\nNew release.\n\n## [1.0.0]\n\nOld release.");
        var error = Assert.Throws<InvalidDataException>(() => Run(mode, () => throw new Exception("Must not connect.")));
        Assert.Contains("'1.1.0' does not match manifest ModVersion '1.0.0'", error.Message);
    }

    [Theory]
    [InlineData("# Changelog", "## [Unreleased]")]
    [InlineData("## [1.0.0]\n\n## [Unreleased]", "## [Unreleased]")]
    [InlineData("## [Unreleased]", "versioned release section")]
    [InlineData("## [Unreleased]\n\n## Other\n\n## [1.0.0]", "does not match")]
    public void MissingOrMisorderedSectionsPreventPublication(string markdown, string expectedError)
    {
        File.WriteAllText(Changelog, markdown);
        var error = Assert.Throws<InvalidDataException>(() => Run("publish", () => throw new Exception("Must not connect.")));
        Assert.Contains(expectedError, error.Message);
    }

    [Fact]
    public void MissingManifestVersionPreventsPublication()
    {
        File.WriteAllText(Manifest, "<Manifest />");
        Assert.Contains("must specify a ModVersion", Assert.Throws<InvalidDataException>(() =>
            Run("publish", () => throw new Exception("Must not connect."))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheLatestReleaseIsPublishedWithSteamFormattingAndResolvedLinks(bool olderRelease)
    {
        File.WriteAllText(Manifest, "<Manifest><ModVersion>1.1.0-beta.1</ModVersion></Manifest>");
        File.WriteAllText(Changelog, "# Changelog\n\n<!--\n## [VERSION] - YYYY-MM-DD\nRelease instructions.\n-->\n\n"
            + "## [Unreleased]\n \t\n## [1.1.0-beta.1] - 2026-11-11\n\n- Added **hotkeys**.\n  With tooltips.\n"
            + (olderRelease ? "\n## [1.0.0] - 2026-11-09\n\nOld release.\n" : "")
            + "\n[1.1.0-beta.1]: https://example.com/release\n");
        Run("publish");
        Assert.Equal("[h2][url=https://example.com/release]1.1.0-beta.1[/url] - 2026-11-11[/h2]\n\n"
            + "[list]\n[*]Added [b]hotkeys[/b]. With tooltips.\n[/list]\n", _steam.ChangeNote);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderedChangeNoteLimitIncludesFormattingAndCountsUtf8Bytes(bool tooLong)
    {
        const string heading = "## [Unreleased]\n\n## [1.0.0]\n\n";
        File.WriteAllText(Changelog, heading + "x");
        int overhead = System.Text.Encoding.UTF8.GetByteCount(WorkshopReleaseNotes.Load(Changelog, Manifest)) - 1;
        int bodyBytes = WorkshopLimits.ChangeNoteMaxBytes - overhead;
        string body = new('\u00e9', bodyBytes / 2);
        if (bodyBytes % 2 != 0) body += "x";
        File.WriteAllText(Changelog, heading + body + (tooLong ? "x" : ""));
        void Prepare() => Run("prepare", () => throw new Exception("Must not connect."));
        if (tooLong) Assert.Contains("Change note", Assert.Throws<InvalidDataException>(Prepare).Message);
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
        new WorkshopSettings { PublishedFileId = id, Title = "Tuck" },
        WorkshopSettings.JsonOptions));

    private void Run(string mode, Func<IWorkshopClient>? connect = null) => WorkshopPublisher.Run(mode,
        SettingsPath, Readme, Changelog, Path.Combine(_root, "mod"), ImagePath, Output, connect ?? (() => _steam));

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
        public string? ChangeNote;
        public string? ImageUrlOverride;
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

        public void Publish(ulong id, WorkshopSettings settings, string contentFolder, string previewFile, string description, string changeNote)
        {
            Assert.Equal(ItemId, id);
            Assert.True(File.Exists(Path.Combine(contentFolder, "manifest.xml")));
            Assert.True(File.Exists(previewFile));
            Calls.Add("publish");
            if (FailPublish) throw new IOException("Simulated upload failure.");
            Description = description;
            ChangeNote = changeNote;
        }

        public void Dispose() { }
    }
}
