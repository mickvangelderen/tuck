using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using AssetsTools.NET.Extra;

if (args.Length != 5)
    throw new ArgumentException("Usage: ReadGameVersion <globalgamemanagers> <classdata.tpk> <Assembly-CSharp.dll> <game-version-output> <game-display-version-output>");

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

    var displayVersion = ReadDisplayVersion(args[2]);
    File.WriteAllText(args[3], version + "\n");
    File.WriteAllText(args[4], displayVersion + "\n");
    Console.WriteLine($"Captured game version {version}, display version {displayVersion}");
}
finally
{
    manager.UnloadAll();
}

static string ReadDisplayVersion(string assemblyPath)
{
    // Read the constant without loading the game's types or running its code.
    using var stream = File.OpenRead(assemblyPath);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    var playerStore = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
        .Single(type => metadata.GetString(type.Name) == "PlayerStore" && metadata.GetString(type.Namespace) == "");
    var field = playerStore.GetFields().Select(metadata.GetFieldDefinition)
        .Single(field => metadata.GetString(field.Name) == "VERSION_NUMBER");
    var constantHandle = field.GetDefaultValue();
    if (constantHandle.IsNil)
        throw new InvalidDataException("Expected PlayerStore.VERSION_NUMBER to be a string constant.");
    var constant = metadata.GetConstant(constantHandle);
    if (constant.TypeCode != ConstantTypeCode.String)
        throw new InvalidDataException("Expected PlayerStore.VERSION_NUMBER to be a string constant.");
    var reader = metadata.GetBlobReader(constant.Value);
    var version = reader.ReadUTF16(reader.Length).Trim();
    const string prefix = "VERSION ";
    if (version.StartsWith(prefix, StringComparison.Ordinal))
        version = version[prefix.Length..].Trim();
    if (string.IsNullOrEmpty(version) || version.Contains('\r') || version.Contains('\n'))
        throw new InvalidDataException("Expected a nonempty, single-line PlayerStore.VERSION_NUMBER.");
    return version;
}
