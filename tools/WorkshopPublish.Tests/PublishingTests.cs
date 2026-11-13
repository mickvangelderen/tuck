using System.Diagnostics;
using System.Net;
using System.Text.Json;

public sealed class PublishingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tuck-publish-test-").FullName;
    private readonly FakeWorkshop _steam = new();
    private readonly FakeMediaHost _media = new();
    private readonly HttpClient _http;
    private string SettingsPath => Path.Combine(_root, "workshop.json");
    private string Output => Path.Combine(_root, "output");
    private string Receipt => Path.Combine(Output, "publishedfileid.txt");
    private string ImagePath => Path.Combine(_root, "image.png");
    private string Readme => Path.Combine(_root, "description.md");
    private string Changelog => Path.Combine(_root, "CHANGELOG.md");
    private string Manifest => Path.Combine(_root, "mod", "manifest.xml");

    public PublishingTests()
    {
        _http = new HttpClient(_media);
        File.WriteAllBytes(ImagePath, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        File.WriteAllText(Readme, "# Tuck\n\nDescription.\n\n![demo](image.png)\n");
        Directory.CreateDirectory(Path.Combine(_root, "mod"));
        File.WriteAllText(Manifest, "<Manifest><ModVersion>1.0.0</ModVersion></Manifest>");
        File.WriteAllText(Changelog, "# Changelog\n\n## [Unreleased]\n\n## [1.0.0] - 2026-11-09\n\nTest release\n");
        WriteSettings(42);
        Git("init");
        Git("config", "user.name", "Publisher Test");
        Git("config", "user.email", "publisher@example.invalid");
        Git("config", "commit.gpgsign", "false");
        Git("config", "core.hooksPath", Path.Combine(_root, "no-hooks"));
        Git("remote", "add", "origin", "git@github.com:example/tuck.git");
        CommitImage("image.png");
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
        Assert.Contains($"[img]https://raw.githubusercontent.com/example/tuck/{Git("rev-parse", "HEAD")}/image.png[/img]", text);
        Assert.Empty(_media.Requests);
        Assert.Equal("[h2][1.0.0] - 2026-11-09[/h2]\n\nTest release\n",
            File.ReadAllText(Path.Combine(Output, "change-note.txt")));
    }

    [Fact]
    public void CheckVerifiesPublicMediaAndOwnershipWithoutPublishing()
    {
        Run("check");
        Assert.Equal(["query"], _steam.Calls);
        Assert.Single(_media.Requests);
    }

    [Fact]
    public void NewItemIsSavedAndPublishedWithCommitPinnedUrlsInOneRun()
    {
        WriteSettings(0);
        Run("publish");
        Assert.Equal(42UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
        Assert.Equal(["create", "query", "publish"], _steam.Calls);
        Assert.Contains($"[img]https://raw.githubusercontent.com/example/tuck/{Git("rev-parse", "HEAD")}/image.png[/img]", _steam.Description);
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
        Assert.Equal(["create", "query", "publish"], _steam.Calls);
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
    public void RetryAfterContentFailureReusesTheItem()
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
    public void DescriptionLimitCountsEveryImageOccurrenceBeforeConnecting(string mode)
    {
        WriteSettings(0);
        File.WriteAllText(Readme, new string('x', 7000) + "\n\n"
            + string.Concat(Enumerable.Repeat("![image](image.png)\n\n", 10)));
        var error = Assert.Throws<InvalidDataException>(() => Run(mode, () => throw new Exception("Must not connect.")));
        Assert.Contains("Rendered description", error.Message);
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
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
    public void DistinctPathsResolveIndependentlyAndRepeatedUrlsAreCheckedOnce()
    {
        File.Copy(ImagePath, Path.Combine(_root, "copy.png"));
        CommitImage("copy.png");
        File.AppendAllText(Readme, "\n![copy](copy.png)\n\n![repeat](image.png)\n");
        Run("publish");
        Assert.Equal(2, _media.Requests.Count);
        Assert.Contains("/copy.png[/img]", _steam.Description);
        Assert.Equal(3, _steam.Description!.Split("[img]").Length - 1);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("publish")]
    public void UnavailablePublicMediaStopsBeforeCreatingAnItem(string mode)
    {
        WriteSettings(0);
        _media.Status = HttpStatusCode.NotFound;
        Assert.Contains("Push the commit", Assert.Throws<InvalidOperationException>(() => Run(mode)).Message);
        Assert.Empty(_steam.Calls);
        Assert.Equal(0UL, WorkshopSettings.Load(SettingsPath).PublishedFileId);
        Assert.False(File.Exists(Receipt));
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

    [Theory]
    [InlineData("modified")]
    [InlineData("staged")]
    [InlineData("staged-only")]
    [InlineData("untracked")]
    [InlineData("added")]
    [InlineData("assume-unchanged")]
    public void UncommittedMediaStopsBeforeAnyNetworkRequest(string state)
    {
        string path = Path.Combine(_root, "demo.gif");
        byte[] original = File.ReadAllBytes(ImagePath);
        File.WriteAllBytes(path, original);
        if (state is not ("untracked" or "added")) CommitImage("demo.gif");
        if (state == "assume-unchanged") Git("update-index", "--assume-unchanged", "demo.gif");
        File.AppendAllText(path, "changed");
        if (state is "staged" or "staged-only" or "added") Git("add", "demo.gif");
        if (state == "staged-only") File.WriteAllBytes(path, original);
        File.WriteAllText(Readme, "![demo](demo.gif)");

        Assert.Throws<InvalidOperationException>(() => Run("publish"));
        Assert.Empty(_media.Requests);
        Assert.Empty(_steam.Calls);
    }

    [Fact]
    public void PreparationAlsoRejectsDirtyMedia()
    {
        File.WriteAllBytes(Path.Combine(_root, "demo.gif"), File.ReadAllBytes(ImagePath));
        CommitImage("demo.gif");
        File.AppendAllText(Path.Combine(_root, "demo.gif"), "changed");
        File.WriteAllText(Readme, "![demo](demo.gif)");
        Assert.Contains("uncommitted", Assert.Throws<InvalidOperationException>(() => Run("prepare")).Message);
    }

    [Theory]
    [InlineData("https://github.com/example/tuck.git")]
    [InlineData("https://github.com/example/tuck")]
    [InlineData("git@github.com:example/tuck.git")]
    [InlineData("ssh://git@github.com/example/tuck.git")]
    public void OriginUrlsAndEscapedMediaPathsProduceCommitPinnedRawUrls(string remote)
    {
        Git("remote", "set-url", "origin", remote);
        string directory = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        string path = Path.Combine(directory, "demo [1] #é.gif");
        File.Copy(ImagePath, path);
        CommitImage("media/demo [1] #é.gif");

        var urls = GitHubMedia.Resolve(directory, [path]);
        Assert.Equal($"https://raw.githubusercontent.com/example/tuck/{Git("rev-parse", "HEAD")}/media/demo%20%5B1%5D%20%23%C3%A9.gif", urls[path]);
    }

    [Theory]
    [InlineData("https://gitlab.com/example/tuck.git")]
    [InlineData("https://github.com.evil.invalid/example/tuck.git")]
    public void NonGitHubOriginsAreRejected(string remote)
    {
        Git("remote", "set-url", "origin", remote);
        Assert.Contains("GitHub repository", Assert.Throws<InvalidOperationException>(() => Run("prepare")).Message);
    }

    [Fact]
    public void MediaOutsideTheRepositoryIsRejected()
    {
        string path = Path.GetFullPath(Path.Combine(_root, "..", "outside.gif"));
        Assert.Contains("inside the Git repository", Assert.Throws<InvalidOperationException>(() =>
            GitHubMedia.Resolve(_root, [path])).Message);
    }

    [Fact]
    public void DescriptionImagesCanExceedSteamPreviewLimit()
    {
        File.WriteAllBytes(Path.Combine(_root, "large.gif"), new byte[1_000_001]);
        CommitImage("large.gif");
        File.WriteAllText(Readme, "![large](large.gif)");
        Run("publish");
        Assert.Contains("/large.gif[/img]", _steam.Description);
    }

    [Fact]
    public void CoverImageStillHasSteamPreviewLimit()
    {
        File.WriteAllBytes(ImagePath, new byte[1_000_000]);
        Assert.Contains("1 MB", Assert.Throws<ArgumentException>(() => Run("prepare")).Message);
    }

    [Fact]
    public void UnrelatedUntrackedFilesDoNotBlockPublishing()
    {
        File.WriteAllText(Path.Combine(_root, "unused.gif"), "work in progress");
        Run("publish");
        Assert.Equal(["query", "publish"], _steam.Calls);
    }

    [Fact]
    public void SlideshowIsSeparateFromDescriptionMediaAndPublishedInTheSameSubmission()
    {
        string directory = Directory.CreateDirectory(Path.Combine(_root, "media", "slideshow")).FullName;
        string slide = Path.Combine(directory, "01-slide.gif");
        File.WriteAllBytes(slide, Enumerable.Repeat((byte)42, 32).ToArray());
        _steam.Images.Add(new(1, "old-description-image.gif")); // A video occupies index 0.
        _steam.Images.Add(new(2, "extra.gif"));

        Run("publish");

        Assert.Equal(["query", "publish"], _steam.Calls);
        Assert.Equal(new uint[] { 2 }, _steam.Slideshow!.Removals);
        var upload = Assert.Single(_steam.Slideshow.Uploads);
        Assert.Equal(1U, upload.Index);
        Assert.Equal(WorkshopImage.Load(slide).Name, upload.Image.Name);
        Assert.DoesNotContain(upload.Image.Name, _steam.Description);
        Assert.Single(_media.Requests);
    }

    [Fact]
    public void MissingSlideshowFolderLeavesExistingImagesAlone()
    {
        _steam.Images.Add(new(0, "keep.gif"));
        Run("publish");
        Assert.Empty(_steam.Slideshow!.Uploads);
        Assert.Empty(_steam.Slideshow.Removals);
    }

    [Fact]
    public void EmptySlideshowFolderRemovesImagesButNotTheVideoBetweenThem()
    {
        Directory.CreateDirectory(Path.Combine(_root, "media", "slideshow"));
        _steam.Images.Add(new(0, "first.gif"));
        _steam.Images.Add(new(2, "last.gif"));
        Run("publish");
        Assert.Equal(new uint[] { 2, 0 }, _steam.Slideshow!.Removals);
        Assert.Empty(_steam.Slideshow.Uploads);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("check")]
    [InlineData("publish")]
    public void InvalidSlideshowFileStopsBeforeNetworkAccess(string mode)
    {
        string directory = Directory.CreateDirectory(Path.Combine(_root, "media", "slideshow")).FullName;
        File.WriteAllBytes(Path.Combine(directory, "01-large.gif"), new byte[1_000_000]);
        Assert.Contains("1 MB", Assert.Throws<ArgumentException>(() => Run(mode)).Message);
        Assert.Empty(_steam.Calls);
        Assert.Empty(_media.Requests);
    }

    [Fact]
    public void CheckDoesNotApplySlideshowChanges()
    {
        Directory.CreateDirectory(Path.Combine(_root, "media", "slideshow"));
        _steam.Images.Add(new(0, "remove.gif"));
        Run("check");
        Assert.Equal(["query"], _steam.Calls);
        Assert.Null(_steam.Slideshow);
        Assert.Single(_steam.Images);
    }

    private void WriteSettings(ulong id) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
        new WorkshopSettings { PublishedFileId = id, Title = "Tuck" },
        WorkshopSettings.JsonOptions));

    private void Run(string mode, Func<IWorkshopClient>? connect = null) => WorkshopPublisher.Run(mode,
        SettingsPath, Readme, Changelog, Path.Combine(_root, "mod"), ImagePath, Output, connect ?? (() => _steam), _http);

    private void CommitImage(string path)
    {
        Git("add", "--", path);
        Git("commit", "-m", "Add media");
    }

    private string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult().Trim();
    }

    public void Dispose()
    {
        _http.Dispose();
        // Git objects are read-only on Windows. Only clean up this test's temporary directory.
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(_root));
        foreach (string path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeMediaHost : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(Status));
        }
    }

    private sealed class FakeWorkshop : IWorkshopClient
    {
        public List<string> Calls { get; } = [];
        public List<WorkshopPreview> Images { get; } = [];
        public WorkshopSlideshow? Slideshow;
        public uint AppId = WorkshopPublisher.AppId;
        public ulong OwnerId = 7;
        public ulong ItemId = 42;
        public ulong UserId => 7;
        public bool NeedsAgreement, FailPublish;
        public string? Description;
        public string? ChangeNote;
        public Action? BeforeCreate;

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
            return new(AppId, OwnerId, Images);
        }

        public void Publish(ulong id, WorkshopSettings settings, string contentFolder, string previewFile, string description, string changeNote,
            WorkshopSlideshow slideshow)
        {
            Assert.Equal(ItemId, id);
            Assert.True(File.Exists(Path.Combine(contentFolder, "manifest.xml")));
            Assert.True(File.Exists(previewFile));
            Calls.Add("publish");
            if (FailPublish) throw new IOException("Simulated upload failure.");
            Description = description;
            ChangeNote = changeNote;
            Slideshow = slideshow;
        }

        public void Dispose() { }
    }
}
