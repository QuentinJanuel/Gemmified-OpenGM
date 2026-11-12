using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;

namespace OpenGM.Tests;

/// <summary>
/// The audio builtins tolerate an index that names no sound, the way real GameMaker does.
///
/// They used to throw KeyNotFoundException instead, and a throw out of a builtin aborts the whole
/// calling event -- so one stray audio_sound_pitch(-1) took out everything after it in that event.
/// Games pass -1 around freely: it is what every audio function that failed hands back.
/// </summary>
[TestClass]
public class AudioBuiltinRobustnessTests
{
    /// <summary>
    /// The -1 above then gets passed straight back into the audio builtins by games that don't
    /// check it, so those have to tolerate it the way real GameMaker does.
    /// </summary>
    [TestMethod]
    public void TheAssetAccessorsIgnoreAnIndexThatNamesNoSound()
    {
        AudioManager.SetAssetPitch(-1, 2);
        AudioManager.SetAssetGain(-1, 2);
        AudioManager.SetAssetOffset(-1, 2);

        Assert.AreEqual(1, AudioManager.GetAssetPitch(-1));
        Assert.AreEqual(0, AudioManager.GetAssetOffset(-1));
        Assert.IsNull(AudioManager.GetAudioAsset(-1));
    }

    [TestMethod]
    public void PlayingAnIndexThatNamesNoSoundReturnsMinusOneInsteadOfThrowing()
    {
        Assert.AreEqual(-1, AudioManager.audio_play_sound(-1, 0, false, 1, 0, 1));
        Assert.AreEqual(-1, AudioManager.audio_play_sound(987654, 0, false, 1, 0, 1));
    }
}
