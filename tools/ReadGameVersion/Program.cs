using AssetsTools.NET.Extra;

if (args.Length != 3)
    throw new ArgumentException("Usage: ReadGameVersion <globalgamemanagers> <classdata.tpk> <output-file>");

var manager = new AssetsManager();
try
{
    var file = manager.LoadAssetsFile(args[0], false);
    // Player builds strip type trees, so use the fetched Unity class definitions.
    manager.LoadClassPackage(args[1]);
    manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
    var settings = file.file.GetAssetsOfType(AssetClassID.PlayerSettings).Single();
    var version = manager.GetBaseField(file, settings)["bundleVersion"].AsString.Trim();
    if (string.IsNullOrEmpty(version) || version.Contains('\r') || version.Contains('\n'))
        throw new InvalidDataException("Expected a nonempty, single-line PlayerSettings.bundleVersion.");

    File.WriteAllText(args[2], version + "\n");
    Console.WriteLine($"Captured game version {version}");
}
finally
{
    manager.UnloadAll();
}
