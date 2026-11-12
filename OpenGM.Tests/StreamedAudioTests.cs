using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;

namespace OpenGM.Tests;

/// <summary>
/// audio_create_stream registers a loose file the way GameMaker treats it -- streamed off disk --
/// instead of decoding it to PCM up front.
///
/// It used to decode immediately, which is hundreds of megabytes of float samples per long track
/// (a 30-minute stereo one is over 600MB), and a game that registers its soundtrack in its init
/// script asked for every one of them at once. In the browser that threw OutOfMemoryException out
/// of the middle of that script; the script aborted, its globals were left half-built, and the
/// game then drew a black screen forever off the broken state.
/// </summary>
[TestClass]
public class StreamedAudioTests
{
    // Registering is deliberately all these tests do to the AudioManager, and they never tear it
    // down again: UnregisterAudio calls Update(), which sets the OpenAL listener, and a machine
    // with no libopenal (any CI runner without it installed -- macOS ships one, Linux does not)
    // throws DllNotFoundException out of the cleanup and fails a test whose subject never touched
    // audio hardware at all. Registration itself is pure bookkeeping, which is the whole point of
    // this commit, so each test just uses a name of its own.

    [TestMethod]
    public void RegisteringAStreamDecodesNothing()
    {
        var index = AudioManager.RegisterStreamedFile("battle_stream", "battle.ogg");

        Assert.AreNotEqual(-1, index);
        var asset = AudioManager.GetAudioAsset(index);
        Assert.IsNotNull(asset);
        Assert.AreEqual(0, asset.Clip, "nothing should be decoded until the sound is actually played");
        Assert.IsNull(asset.CompressedBytes, "the file on disk is the storage; don't hold a second copy");
        Assert.AreEqual("battle.ogg", asset.FileName);
    }

    /// <summary>
    /// A stream has to stay evictable whatever the lazy-audio toggle says -- it is off by default
    /// on desktop, and the whole point of a stream is that it does not live in memory.
    /// </summary>
    [TestMethod]
    public void AStreamIsAlwaysEvictableMusic()
    {
        var index = AudioManager.RegisterStreamedFile("menu_stream", "mus_menu1.ogg");

        var asset = AudioManager.GetAudioAsset(index)!;
        Assert.IsTrue(asset.Streamed);
        Assert.IsTrue(asset.IsMusic);
        Assert.IsFalse(asset.Permanent, "a stream must stay under the music budget, not pin itself in memory");
    }

    /// <summary>
    /// GameMaker hands back -1 for a file that isn't there. Throwing instead would abort the whole
    /// calling event, which is how one missing music file took a game's entire init script with it.
    /// </summary>
    [TestMethod]
    public void AMissingFileIsMinusOneRatherThanAThrow()
    {
        Assert.AreEqual(-1, AudioManager.RegisterStreamedFile("nope", "definitely-not-here.ogg"));
    }
}
