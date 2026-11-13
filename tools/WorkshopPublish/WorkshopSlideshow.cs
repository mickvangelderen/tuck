using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal sealed record WorkshopImage(string Path, string Name, byte[] Bytes)
{
    public static void ValidatePreview(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 16 || file.Length >= 1_000_000)
            throw new ArgumentException($"Image must exist and be between 16 bytes and 1 MB (exclusive): {path}");
        if (file.Extension.ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif"))
            throw new ArgumentException($"Use a PNG, JPG or GIF image: {path}");
    }

    public static WorkshopImage Load(string path)
    {
        ValidatePreview(path);
        byte[] bytes = File.ReadAllBytes(path);
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".jpeg") extension = ".jpg";
        string name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension;
        return new(path, name, bytes);
    }
}

// Index is the original Steam preview index, including any interleaved videos.
internal sealed record WorkshopPreview(uint Index, string Name);
internal sealed record SlideshowUpload(uint? Index, WorkshopImage Image);

internal sealed record WorkshopSlideshow(uint[] Removals, SlideshowUpload[] Uploads)
{
    public static WorkshopImage[]? Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"Slideshow unchanged: {directory} does not exist.");
            return null;
        }

        var files = Directory.GetFiles(directory).Where(path => System.IO.Path.GetFileName(path) != ".gitkeep")
            .Select(path =>
            {
                string prefix = Regex.Match(System.IO.Path.GetFileName(path), @"^[0-9]+").Value;
                if (!int.TryParse(prefix, out int order))
                    throw new ArgumentException($"Slideshow filenames must start with a number, e.g. 01-demo.gif: {path}");
                return (Path: path, Order: order);
            })
            .OrderBy(file => file.Order).ThenBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => WorkshopImage.Load(file.Path)).ToArray();
        Console.WriteLine($"Slideshow: {files.Length} image(s) from {directory}.");
        foreach (var image in files) Console.WriteLine($"  {System.IO.Path.GetFileName(image.Path)} -> {image.Name}");
        return files;
    }

    public static WorkshopSlideshow Plan(IReadOnlyList<WorkshopPreview> existing, IReadOnlyList<WorkshopImage>? desired)
    {
        if (desired == null) return new([], []);

        var uploads = new List<SlideshowUpload>();
        for (int i = 0; i < desired.Count; i++)
        {
            if (i >= existing.Count) uploads.Add(new(null, desired[i]));
            else if (!string.Equals(existing[i].Name, desired[i].Name, StringComparison.Ordinal))
                uploads.Add(new(existing[i].Index, desired[i]));
        }
        // Removing from the end also works if Steam shifts indices after each removal.
        uint[] removals = existing.Skip(desired.Count).Select(image => image.Index).OrderDescending().ToArray();
        Console.WriteLine($"Slideshow changes: {uploads.Count(upload => upload.Index == null)} add, "
            + $"{uploads.Count(upload => upload.Index != null)} replace, {removals.Length} remove.");
        return new(removals, uploads.ToArray());
    }
}
