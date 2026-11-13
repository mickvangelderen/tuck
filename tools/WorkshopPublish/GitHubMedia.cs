using System.Diagnostics;
using System.Text.RegularExpressions;

internal static class GitHubMedia
{
    public static IReadOnlyDictionary<string, string> Resolve(string directory, IReadOnlyList<string> paths)
    {
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (paths.Count == 0) return urls;

        string root = Git(directory, "rev-parse", "--show-toplevel");
        string commit = Git(root, "rev-parse", "HEAD");
        string remote = Git(root, "remote", "get-url", "origin");
        var match = Regex.Match(remote, @"\A(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([\w.-]+)/([\w.-]+?)(?:\.git)?/?\z");
        if (!match.Success)
            throw new InvalidOperationException("Media hosting requires origin to be a GitHub repository (HTTPS or SSH).");
        string repository = $"{match.Groups[1].Value}/{match.Groups[2].Value}";

        foreach (string path in paths)
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
                throw new InvalidOperationException($"Description media must be inside the Git repository: {path}");
            if (!File.Exists(path))
                throw new ArgumentException($"Description media does not exist: {path}");

            string entry = Git(root, "ls-tree", "--format=%(objectmode) %(objectname)", commit, "--", relative);
            if (!(entry.StartsWith("100644 ", StringComparison.Ordinal) || entry.StartsWith("100755 ", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Description media must be a regular file committed in HEAD: {relative}");
            if (Git(root, "status", "--porcelain", "--untracked-files=all", "--", relative).Length != 0 ||
                Git(root, "hash-object", "--", relative) != entry[7..])
                throw new InvalidOperationException($"Description media has uncommitted changes: {relative}. Commit it before preparing a release.");

            string encodedPath = string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));
            urls.Add(path, $"https://raw.githubusercontent.com/{repository}/{commit}/{encodedPath}");
        }
        Console.WriteLine($"Description media: {repository} at {commit}.");
        return urls;
    }

    public static void VerifyAvailable(IEnumerable<string> urls, HttpClient client)
    {
        foreach (string url in urls.Distinct())
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException($"Cannot fetch description media publicly: {url}. "
                    + "Push the commit to the public origin repository before checking or publishing.", error);
            }
        }
    }

    private static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--literal-pathspecs");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git {arguments[0]} failed: {error.GetAwaiter().GetResult().Trim()}");
        return output.GetAwaiter().GetResult().Trim();
    }
}
