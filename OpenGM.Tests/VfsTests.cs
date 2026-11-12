using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenGM.Tests;

/// <summary>
/// The browser build's filesystem (OpenGM.Wasm/Vfs.cs) against the GEMS reference
/// implementation: every expected value below is that implementation's own output,
/// quirks included, because the store keys are the player's saves.
/// </summary>
[TestClass]
public class VfsTests
{
    [TestMethod]
        [DataRow("save1", "gems:file:game-1:game:save1")]
        [DataRow("Data\\save1", "gems:file:game-1:game:Data/save1")]
        [DataRow("\\save1", "gems:file:game-1:game:save1")]
        [DataRow("/save1", "gems:file:game-1:game:save1")]
        [DataRow("D:\\x\\save1", "gems:file:game-1:game:x/save1")]
        [DataRow("C:\\probe\\p_drive.txt", "gems:file:game-1:game:probe/p_drive.txt")]
        [DataRow("c:", "gems:file:game-1:game:")]
        [DataRow("Z:foo", "gems:file:game-1:game:foo")]
        [DataRow("1:foo", "gems:file:game-1:game:1%3Afoo")]
        [DataRow("/game", "gems:file:game-1:game:")]
        [DataRow("/game/", "gems:file:game-1:game:")]
        [DataRow("/GAME/Save/Save1", "gems:file:game-1:game:Save/Save1")]
        [DataRow("/game\\p_wd.txt", "gems:file:game-1:game:p_wd.txt")]
        [DataRow("/gamex/a", "gems:file:game-1:game:gamex/a")]
        [DataRow("/appdata", "gems:file:game-1:appdata:")]
        [DataRow("/appdata/X/y", "gems:file:game-1:appdata:X/y")]
        [DataRow("/APPDATA/X/Y", "gems:file:game-1:appdata:X/Y")]
        [DataRow("/temp/a", "gems:file:game-1:temp:a")]
        [DataRow("/TEMP", "gems:file:game-1:temp:")]
        [DataRow("/temp", "gems:file:game-1:temp:")]
        [DataRow("/tempx/a", "gems:file:game-1:game:tempx/a")]
        [DataRow("..\\p_up.txt", "gems:file:game-1:game:p_up.txt")]
        [DataRow("a/../../b", "gems:file:game-1:game:b")]
        [DataRow("./a/./b//c\\\\d", "gems:file:game-1:game:a/b/c/d")]
        [DataRow("PCase\\P_CASE.TXT", "gems:file:game-1:game:PCase/P_CASE.TXT")]
        [DataRow("pcase\\p_case.txt", "gems:file:game-1:game:pcase/p_case.txt")]
        [DataRow("C:\\Users\\player\\AppData\\Local\\Temp\\gm_ttt_1\\p_tmp.txt", "gems:file:game-1:temp:gm_ttt_1/p_tmp.txt")]
        [DataRow("C:\\Users\\player\\AppData\\Local\\Temp\\", "gems:file:game-1:temp:")]
        [DataRow("C:\\Users\\player\\AppData\\Local\\Temp", "gems:file:game-1:appdata:Temp")]
        [DataRow("C:\\Users\\player\\AppData\\Local", "gems:file:game-1:game:Users/player/AppData/Local")]
        [DataRow("C:\\Users\\player\\AppData\\Local\\", "gems:file:game-1:appdata:")]
        [DataRow("C:\\Users\\player\\AppData\\Roaming\\probe\\p_tmp3.txt", "gems:file:game-1:appdata:probe/p_tmp3.txt")]
        [DataRow("C:\\Users\\player\\AppData\\Local\\probe\\p_env_local.txt", "gems:file:game-1:appdata:probe/p_env_local.txt")]
        [DataRow("C:\\Users\\player\\AppData\\Roaming\\..\\..\\x", "gems:file:game-1:appdata:x")]
        [DataRow("x\\AppData\\Local\\Temp\\..\\..\\y", "gems:file:game-1:temp:y")]
        [DataRow("C:\\Documents and Settings\\player\\Local Settings\\Temp\\x", "gems:file:game-1:temp:x")]
        [DataRow("C:\\Documents and Settings\\player\\Local Settings\\Application Data\\X\\a", "gems:file:game-1:appdata:X/a")]
        [DataRow("C:\\Documents and Settings\\player\\Application Data\\Y\\b", "gems:file:game-1:appdata:Y/b")]
        [DataRow("x/application data/y/appdata/local/z", "gems:file:game-1:appdata:z")]
        [DataRow("x/appdata/roaming/y/appdata/local/z", "gems:file:game-1:appdata:z")]
        [DataRow("C:\\game\\Save\\save1", "gems:file:game-1:game:game/Save/save1")]
        [DataRow("C:\\gamesave1", "gems:file:game-1:game:gamesave1")]
        [DataRow("C:\\game", "gems:file:game-1:game:game")]
        [DataRow("c:/GAME/x", "gems:file:game-1:game:GAME/x")]
        [DataRow("/appdata/YoYoYo_Engine/Data/save1", "gems:file:game-1:appdata:YoYoYo_Engine/Data/save1")]
        [DataRow("/appdata/YoYoYo_Engine/C:/probe/p_drive.txt", "gems:file:game-1:appdata:YoYoYo_Engine/C%3A/probe/p_drive.txt")]
        [DataRow("/appdata/Probe/../p_up.txt", "gems:file:game-1:appdata:p_up.txt")]
        [DataRow("/appdata/Probe/../../../x", "gems:file:game-1:appdata:x")]
        [DataRow("/game/../x", "gems:file:game-1:game:x")]
        [DataRow("/game/data.win", "gems:file:game-1:game:data.win")]
        [DataRow("/appdata/tijit_engine_2/Data/cfg", "gems:file:game-1:appdata:tijit_engine_2/Data/cfg")]
        [DataRow("save/renex² engine.cfg", "gems:file:game-1:game:save/renex%C2%B2%20engine.cfg")]
        [DataRow("セーブ\\データ1.sav", "gems:file:game-1:game:%E3%82%BB%E3%83%BC%E3%83%96/%E3%83%87%E3%83%BC%E3%82%BF1.sav")]
        [DataRow("a:b%c#d?e f+g", "gems:file:game-1:game:b%25c%23d%3Fe%20f%2Bg")]
        [DataRow("~!*'()-_.", "gems:file:game-1:game:~!*'()-_.")]
        [DataRow("tab\there", "gems:file:game-1:game:tab%09here")]
        [DataRow("new\nline", "gems:file:game-1:game:new%0Aline")]
        [DataRow("名前/\u0130stanbul\\AppData\\Local\\z", "gems:file:game-1:appdata:")]
        [DataRow("\u0130\\appdata\\local\\temp\\q", "gems:file:game-1:temp:")]
        [DataRow("\u0130\u0130/appdata/local/abc", "gems:file:game-1:appdata:c")]
        [DataRow("\u212Aey\\AppData\\Roaming\\k", "gems:file:game-1:appdata:k")]
        [DataRow("ΣΑΒΕ\\ΣΑΒΕΣ.txt", "gems:file:game-1:game:%CE%A3%CE%91%CE%92%CE%95/%CE%A3%CE%91%CE%92%CE%95%CE%A3.txt")]
        [DataRow("😀/AppData/Roaming/😀.sav", "gems:file:game-1:appdata:%F0%9F%98%80.sav")]
        [DataRow("", "gems:file:game-1:game:")]
        [DataRow("/", "gems:file:game-1:game:")]
        [DataRow("\\\\server\\share\\f", "gems:file:game-1:game:server/share/f")]
        [DataRow("C:\\Users\\player\\AppData\\LocalLow\\x", "gems:file:game-1:game:Users/player/AppData/LocalLow/x")]
        [DataRow("C:\\Users\\player\\AppData\\Local Settings\\Temp\\y", "gems:file:game-1:temp:y")]
        [DataRow("dir/", "gems:file:game-1:game:dir")]
        [DataRow("a//b", "gems:file:game-1:game:a/b")]
        [DataRow(".", "gems:file:game-1:game:")]
        [DataRow("..", "gems:file:game-1:game:")]
        [DataRow("...", "gems:file:game-1:game:...")]
        [DataRow("a/.../b", "gems:file:game-1:game:a/.../b")]
        [DataRow("x:y:z", "gems:file:game-1:game:y%3Az")]
        [DataRow("%41", "gems:file:game-1:game:%2541")]
        [DataRow("a%2Fb", "gems:file:game-1:game:a%252Fb")]
        [DataRow("spaces  in  name .txt", "gems:file:game-1:game:spaces%20%20in%20%20name%20.txt")]
        [DataRow("UPPER.TXT", "gems:file:game-1:game:UPPER.TXT")]
        [DataRow("ı.txt", "gems:file:game-1:game:%C4%B1.txt")]
        [DataRow("ß/ẞ", "gems:file:game-1:game:%C3%9F/%E1%BA%9E")]
        [DataRow("Ǆ", "gems:file:game-1:game:%C7%84")]
        [DataRow("\u0000nul", "gems:file:game-1:game:%00nul")]
    public void KeysMatchTheTypeScript(string raw, string key)
        => Assert.AreEqual(key, new Vfs("game-1", [], (_, _) => { }, () => { }).StoreKey("file", Vfs.ParsePath(raw)), raw);

    // Not DataRows: an attribute's strings are stored as UTF-8, which has no lone surrogate.
    [TestMethod]
    public void ALoneSurrogateThrowsAsEncodeURIComponentDid()
    {
        var vfs = new Vfs("g", [], (_, _) => { }, () => { });
        foreach (var raw in new[] { "\uD800", "a\uDC00b" })
        {
            Assert.ThrowsException<System.Text.EncoderFallbackException>(() => vfs.StoreKey("file", Vfs.ParsePath(raw)));
        }
    }

    /// <summary>A scripted session: each step's answer, then what the host was told, in order.</summary>
    [TestMethod]
    public void ASessionMatchesTheTypeScript()
    {
        string[] expected =
        [
"read old -> 111,108,100 | ",
            "read OLD -> null | ",
            "exists OLD -> false | ",
            "read music -> 2,3 | ",
            "dirExists MUSIC -> true | ",
            "dirExists save -> true | ",
            "dirExists empty -> false | ",
            "dirExists Empty -> true | ",
            "dirExists root -> true | ",
            "dirExists x(other game) -> false | ",
            "write Save\\a -> undefined | gems:gone:g:game:Save/a=- gems:file:g:game:Save/a=aGk= save",
            "write empty -> undefined | gems:gone:g:game:Save/e=- gems:file:g:game:Save/e= save",
            "read save/A -> null | ",
            "read Save/a -> 104,105 | ",
            "write temp -> undefined | ",
            "read temp -> 9 | ",
            "dirExists temp -> true | ",
            "createDir temp -> undefined | ",
            "dirExists temp d -> true | ",
            "createDir Dir -> undefined | gems:dir:g:game:Dir=1",
            "createDir root -> undefined | ",
            "dirExists dir -> false | ",
            "removeDir root -> undefined | gems:dir:g:game:=-",
            "removeDir Dir -> undefined | gems:dir:g:game:Dir=-",
            "removeDir temp d -> undefined | ",
            "remove old -> true | gems:file:g:game:old=- save",
            "remove missing -> false | gems:file:g:game:missing=- save",
            "remove data.win -> true | gems:file:g:game:DATA.WIN=- gems:gone:g:game:DATA.WIN=1 save",
            "exists data.win -> true | ",
            "read data.win -> 1 | ",
            "write data.win -> undefined | gems:gone:g:game:data.win=- gems:file:g:game:data.win=Bw== save",
            "read DATA.WIN -> null | ",
            "exists DATA.WIN -> false | ",
            "remove temp ext -> true | ",
            "exists temp ext -> true | ",
            "dirExists temp after -> true | ",
            "write temp ext -> undefined | ",
            "read temp ext -> 5 | ",
            "drop Save/a -> undefined | ",
            "exists Save/a -> false | ",
            "dirExists Save -> true | ",
            "write deep -> undefined | gems:gone:g:appdata:Y/deep/er/f=- gems:file:g:appdata:Y/deep/er/f=AQID save",
            "dirExists Y/DEEP -> true | ",
            "dirExists Y/deep/er/f -> false | ",
        ];
        var log = new List<string>();
        var seed = new Dictionary<string, string>
        {
            ["gems:file:g:game:old"] = "b2xk",
            ["gems:file:g:game:Save/Up"] = "VVA=",
            ["gems:dir:g:appdata:Empty"] = "1",
            ["gems:file:other:game:x"] = "eA==",
            ["gems:meta:g"] = "{}",
        };
        var vfs = new Vfs("g", seed, (k, v) => log.Add($"{k}={v ?? "-"}"), () => log.Add("save"));
        vfs.AddBundle(Vfs.ParsePath("/game/data.win"), () => [1]);
        vfs.AddBundle(Vfs.ParsePath("/game/Music/BGM.ogg"), () => [2, 3]);
        vfs.AddBundle(Vfs.ParsePath("/temp/ext.dll"), () => [4]);

        var actual = new List<string>();
        void Step(string what, Func<object?> run)
        {
            log.Clear();
            var answer = run() switch
            {
                null => "null",
                byte[] bytes => string.Join(",", bytes),
                bool flag => flag ? "true" : "false",
                var other => other.ToString(),
            };
            actual.Add($"{what} -> {answer} | {string.Join(" ", log)}");
        }
        object? Do(Action action)
        {
            action();
            return "undefined";
        }
        VPath P(string raw) => Vfs.ParsePath(raw);

        Step("read old", () => vfs.Read(P("old")));
        Step("read OLD", () => vfs.Read(P("OLD")));
        Step("exists OLD", () => vfs.Exists(P("OLD")));
        Step("read music", () => vfs.Read(P("music\\bgm.OGG")));
        Step("dirExists MUSIC", () => vfs.DirectoryExists(P("MUSIC")));
        Step("dirExists save", () => vfs.DirectoryExists(P("save")));
        Step("dirExists empty", () => vfs.DirectoryExists(P("/appdata/empty")));
        Step("dirExists Empty", () => vfs.DirectoryExists(P("/appdata/Empty")));
        Step("dirExists root", () => vfs.DirectoryExists(P("/appdata")));
        Step("dirExists x(other game)", () => vfs.DirectoryExists(P("/game/x")));
        Step("write Save\\a", () => Do(() => vfs.Write(P("Save\\a"), [104, 105])));
        Step("write empty", () => Do(() => vfs.Write(P("Save\\e"), [])));
        Step("read save/A", () => vfs.Read(P("save/A")));
        Step("read Save/a", () => vfs.Read(P("Save/a")));
        Step("write temp", () => Do(() => vfs.Write(P("/temp/x"), [9])));
        Step("read temp", () => vfs.Read(P("/temp/x")));
        Step("dirExists temp", () => vfs.DirectoryExists(P("/TEMP")));
        Step("createDir temp", () => Do(() => vfs.CreateDirectory(P("/temp/d"))));
        Step("dirExists temp d", () => vfs.DirectoryExists(P("/temp/d")));
        Step("createDir Dir", () => Do(() => vfs.CreateDirectory(P("Dir"))));
        Step("createDir root", () => Do(() => vfs.CreateDirectory(P("/game"))));
        Step("dirExists dir", () => vfs.DirectoryExists(P("dir")));
        Step("removeDir root", () => Do(() => vfs.RemoveDirectory(P("/game/"))));
        Step("removeDir Dir", () => Do(() => vfs.RemoveDirectory(P("Dir"))));
        Step("removeDir temp d", () => Do(() => vfs.RemoveDirectory(P("/temp/d"))));
        Step("remove old", () => vfs.Remove(P("old")));
        Step("remove missing", () => vfs.Remove(P("missing")));
        Step("remove data.win", () => vfs.Remove(P("DATA.WIN")));
        Step("exists data.win", () => vfs.Exists(P("data.win")));
        Step("read data.win", () => vfs.Read(P("data.win")));
        Step("write data.win", () => Do(() => vfs.Write(P("data.win"), [7])));
        Step("read DATA.WIN", () => vfs.Read(P("DATA.WIN")));
        Step("exists DATA.WIN", () => vfs.Exists(P("DATA.WIN")));
        Step("remove temp ext", () => vfs.Remove(P("/temp/EXT.dll")));
        Step("exists temp ext", () => vfs.Exists(P("/temp/ext.dll")));
        Step("dirExists temp after", () => vfs.DirectoryExists(P("/temp")));
        Step("write temp ext", () => Do(() => vfs.Write(P("/temp/ext.dll"), [5])));
        Step("read temp ext", () => vfs.Read(P("/temp/ext.dll")));
        Step("drop Save/a", () => Do(() => vfs.Drop("gems:file:g:game:Save/a")));
        Step("exists Save/a", () => vfs.Exists(P("Save/a")));
        Step("dirExists Save", () => vfs.DirectoryExists(P("Save")));
        Step("write deep", () => Do(() => vfs.Write(P("/appdata/Y/deep/er/f"), [1, 2, 3])));
        Step("dirExists Y/DEEP", () => vfs.DirectoryExists(P("/appdata/y/DEEP")));
        Step("dirExists Y/deep/er/f", () => vfs.DirectoryExists(P("/appdata/Y/deep/er/f")));

        CollectionAssert.AreEqual(expected, actual, string.Join("\n", actual));
    }
}
