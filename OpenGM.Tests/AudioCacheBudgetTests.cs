using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;

namespace OpenGM.Tests;

/// <summary>
/// The music budget is the whole audio cache policy now -- there is no lazy on/off switch.
///
/// There used to be one, and on desktop it was off, which did not merely mean "decode eagerly": it
/// set Permanent on every music asset, so they dropped their compressed bytes and could never be
/// evicted. The budget and the LRU were dead code on every desktop. The budget is enforced
/// everywhere now, and the platform difference is only how big it is.
/// </summary>
[TestClass]
public class AudioCacheBudgetTests
{
    // No UnregisterAudio here either -- see StreamedAudioTests for why touching it needs OpenAL.
    [TestCleanup]
    public void TearDown()
    {
        // Back to the desktop default the shell sends (audio-cache-policy.ts).
        AudioManager.SetAudioCachePolicy(256 * 1024, 5.0, 768L * 1024 * 1024);
    }

    /// <summary>
    /// Music is evictable on every platform. Nothing may set Permanent on it -- that is the flag
    /// that made the LRU inert, and the reason a desktop had no protection at all.
    /// </summary>
    [TestMethod]
    public void MusicIsNeverPinnedPermanentlyWhateverThePolicy()
    {
        AudioManager.SetAudioCachePolicy(256 * 1024, 5.0, 768L * 1024 * 1024);

        var index = AudioManager.RegisterStreamedFile("budget_music", "battle.ogg");
        var asset = AudioManager.GetAudioAsset(index)!;

        Assert.IsTrue(asset.IsMusic);
        Assert.IsFalse(asset.Permanent, "music must stay evictable, with or without a lazy mode");
    }

    /// <summary>
    /// A zero or negative budget means "unset", not "evict everything" -- the shell always sends
    /// real numbers, and a game must not be silently left with no resident audio if it ever
    /// doesn't.
    /// </summary>
    [TestMethod]
    public void AnUnsetPolicyFallsBackToDefaultsRatherThanZero()
    {
        AudioManager.SetAudioCachePolicy(0, 0, 0);

        // The defaults are private, so this is observed the only way it shows: a stream still
        // registers and classifies normally rather than being treated as over budget on sight.
        var index = AudioManager.RegisterStreamedFile("defaults_music", "mus_menu1.ogg");
        var asset = AudioManager.GetAudioAsset(index)!;

        Assert.IsTrue(asset.IsMusic);
        Assert.IsFalse(asset.Permanent);
    }
}
