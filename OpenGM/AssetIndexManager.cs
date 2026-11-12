namespace OpenGM;

// TODO "Exact value must be adapted depending on GameMaker version."
// https://github.com/UnderminersTeam/Underanalyzer/blob/36f19d67de5f0e51376b0199be120f0508d1d931/Underanalyzer/IGameContext.cs#L16
public enum AssetType
{
    objects,
    sprites,
    sounds,
    rooms,
    backgrounds,
    paths,
    scripts,
    fonts,
    timelines,
    shaders,
    sequences,
    animcurves,
    particlesystems,
    roominstances
}

public static class AssetIndexManager
{
    private static Dictionary<AssetType, Dictionary<string, int>> _assetList = new();
    private static Dictionary<string, int> _nameToIndex = new();

    public static void LoadAssetIndexes(BinaryReader reader)
    {
        Console.Write($"Loading asset order...");

        _assetList.Clear();
        _nameToIndex.Clear();

        var lines = reader.ReadString().SplitLines();
        var headerLineNumber = 0;
        AssetType currentAssetType = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("@@") && line.EndsWith("@@"))
            {
                headerLineNumber = i;
                currentAssetType = Enum.Parse<AssetType>(line.Trim('@'));
                continue;
            }

            if (line == "(null)")
            {
                continue;
            }

            if (!_assetList.ContainsKey(currentAssetType))
            {
                _assetList.Add(currentAssetType, new Dictionary<string, int>());
            }

            _assetList[currentAssetType].Add(line, i - headerLineNumber - 1);
            _nameToIndex.Add(line, i - headerLineNumber - 1);
        }
        Console.WriteLine($" Done!");
    }

    public static int GetIndex(string name)
    {
        return _nameToIndex.TryGetValue(name, out var index) ? index : -1;
    }

    public static int GetIndex(AssetType type, string name)
    {
        // A game with no assets of a type at all has no entry for it -- common for a GMS1.4 game
        // asked about sequences or animcurves. That's "no such asset", not an exception.
        return _assetList.TryGetValue(type, out var assets) && assets.TryGetValue(name, out var index)
            ? index
            : -1;
    }

    public static string GetName(AssetType type, int index)
    {
        return _assetList[type].First(x => x.Value == index).Key;
    }

    public static int Register(AssetType type, string name)
    {
        if (!_assetList.ContainsKey(type))
        {
            _assetList.Add(type, new Dictionary<string, int>());
        }

        if (_assetList[type].TryGetValue(name, out var index))
        {
            return index;
        }

        // DefaultIfEmpty because a game can register the first asset of a type it shipped none of
        // (timeline_add in a game with no editor-authored timelines, say) -- Max() on an empty
        // sequence throws, and the right answer there is that the new asset gets index 0.
        var highestIndex = _assetList[type].Values.DefaultIfEmpty(-1).Max();
        _assetList[type].Add(name, highestIndex + 1);
        _nameToIndex.Add(name, highestIndex + 1);
        return highestIndex + 1;
    }

    public static void Unregister(AssetType type, string name)
    {
        if (!_assetList.ContainsKey(type))
        {
            return;
        }

        if (!_assetList[type].ContainsKey(name))
        {
            return;
        }

        _assetList[type].Remove(name);
        _nameToIndex.Remove(name);
    }
}
