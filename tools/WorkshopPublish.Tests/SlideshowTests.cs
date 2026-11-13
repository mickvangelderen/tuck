using System.Security.Cryptography;

public sealed class SlideshowTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tuck-slideshow-test-").FullName;

    [Fact]
    public void FilesAreOrderedNumericallyAndNamedByContentHash()
    {
        byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        foreach (string name in new[] { "10-last.gif", "2-second.PNG", "01-first.jpeg" })
            File.WriteAllBytes(Path.Combine(_directory, name), bytes);
        File.WriteAllText(Path.Combine(_directory, ".gitkeep"), "");

        var images = WorkshopSlideshow.Load(_directory)!;

        Assert.Equal(new[] { "01-first.jpeg", "2-second.PNG", "10-last.gif" }, images.Select(image => Path.GetFileName(image.Path)));
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(new[] { hash + ".jpg", hash + ".png", hash + ".gif" }, images.Select(image => image.Name));
        Assert.All(images, image => Assert.Equal(bytes, image.Bytes));
    }

    [Theory]
    [InlineData("demo.gif", 32, "start with a number")]
    [InlineData("1-demo.mp4", 32, "PNG, JPG or GIF")]
    [InlineData("1-demo.webp", 32, "PNG, JPG or GIF")]
    [InlineData("1-demo.gif", 1_000_000, "1 MB")]
    [InlineData("1-demo.gif", 0, "16 bytes")]
    public void InvalidSlideshowInputsAreRejected(string name, int bytes, string message)
    {
        File.WriteAllBytes(Path.Combine(_directory, name), new byte[bytes]);
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => WorkshopSlideshow.Load(_directory)).Message);
    }

    [Fact]
    public void MissingAndIntentionallyEmptyFoldersHaveDifferentMeanings()
    {
        Assert.Null(WorkshopSlideshow.Load(Path.Combine(_directory, "missing")));
        File.WriteAllText(Path.Combine(_directory, ".gitkeep"), "");
        Assert.Empty(WorkshopSlideshow.Load(_directory)!);
    }

    [Fact]
    public void ReorderingAndRemovalPreserveVideosAndBecomeANoOpOnRetry()
    {
        var previews = new List<string> { "video:youtube", "old.gif", "b.gif", "video:other", "a.gif", "stale.gif" };
        WorkshopImage[] desired = [Image("a.gif"), Image("b.gif")];
        var plan = WorkshopSlideshow.Plan(Images(previews), desired);

        Assert.Equal(new uint[] { 5, 4 }, plan.Removals);
        Assert.Equal(1U, Assert.Single(plan.Uploads).Index);
        Apply(previews, plan);
        Assert.Equal(new[] { "video:youtube", "a.gif", "b.gif", "video:other" }, previews);

        var retry = WorkshopSlideshow.Plan(Images(previews), desired);
        Assert.Empty(retry.Removals);
        Assert.Empty(retry.Uploads);
    }

    [Fact]
    public void NewImagesAreAppendedInDesiredOrderWithoutUploadingMatchingSlots()
    {
        var previews = new List<string> { "a.gif", "video:youtube" };
        WorkshopImage[] desired = [Image("a.gif"), Image("c.gif"), Image("b.gif")];
        var plan = WorkshopSlideshow.Plan(Images(previews), desired);
        Assert.Empty(plan.Removals);
        Assert.All(plan.Uploads, upload => Assert.Null(upload.Index));
        Apply(previews, plan);
        Assert.Equal(new[] { "a.gif", "video:youtube", "c.gif", "b.gif" }, previews);
    }

    [Fact]
    public void SwappingTwoImagesReplacesTheirSlotsInOrder()
    {
        var previews = new List<string> { "a.gif", "video:youtube", "b.gif" };
        var plan = WorkshopSlideshow.Plan(Images(previews), [Image("b.gif"), Image("a.gif")]);
        Apply(previews, plan);
        Assert.Equal(new[] { "b.gif", "video:youtube", "a.gif" }, previews);
    }

    [Fact]
    public void DuplicateStaleImagesAreRemovedUsingTheirDistinctIndices()
    {
        var previews = new List<string> { "a.gif", "a.gif", "video:youtube", "a.gif" };
        var plan = WorkshopSlideshow.Plan(Images(previews), [Image("a.gif")]);
        Assert.Equal(new uint[] { 3, 1 }, plan.Removals);
        Assert.Empty(plan.Uploads);
        Apply(previews, plan);
        Assert.Equal(new[] { "a.gif", "video:youtube" }, previews);
    }

    private static WorkshopImage Image(string name) => new(name, name, []);

    private static WorkshopPreview[] Images(List<string> previews) => previews
        .Select((name, index) => new WorkshopPreview((uint)index, name))
        .Where(preview => !preview.Name.StartsWith("video:", StringComparison.Ordinal)).ToArray();

    private static void Apply(List<string> previews, WorkshopSlideshow plan)
    {
        foreach (var upload in plan.Uploads)
            if (upload.Index is uint index) previews[(int)index] = upload.Image.Name;
            else previews.Add(upload.Image.Name);
        foreach (uint index in plan.Removals) previews.RemoveAt((int)index);
    }

    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(_directory));
        Directory.Delete(_directory, recursive: true);
    }
}
