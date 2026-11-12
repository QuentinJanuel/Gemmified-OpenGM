using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.IO;

namespace OpenGM.Tests;

/// <summary>
/// AudioManager.ChangeGain used to log "Audio gain lerping not implemented!" and snap the gain
/// instantly whenever a fade duration was requested -- so every sound_fade/audio_sound_gain call
/// with a nonzero time argument (a common way to fade music in/out) happened instantly instead of
/// smoothly. The actual OpenAL plumbing (AL.GetSource/AL.Source) needs a real audio context to
/// test, so this covers the pure interpolation math it was factored out into
/// (AudioManager.StepGainLerp), which is what Update() steps once per frame.
/// </summary>
[TestClass]
public class AudioGainLerpTests
{
    [TestMethod]
    public void HalfwayThroughIsHalfwayBetweenStartAndTarget()
    {
        var (gain, done) = AudioManager.StepGainLerp(startGain: 0.0, targetGain: 1.0, elapsedMs: 500, totalMs: 1000);

        Assert.AreEqual(0.5, gain, 0.0001);
        Assert.IsFalse(done, "halfway through a fade shouldn't report finished");
    }

    [TestMethod]
    public void ReachingTheFullDurationFinishesExactlyAtTheTarget()
    {
        var (gain, done) = AudioManager.StepGainLerp(startGain: 0.2, targetGain: 0.8, elapsedMs: 1000, totalMs: 1000);

        Assert.AreEqual(0.8, gain, 0.0001);
        Assert.IsTrue(done);
    }

    [TestMethod]
    public void OvershootingTheDurationClampsRatherThanExtrapolating()
    {
        // Update() runs once per frame, so elapsed can overshoot total by up to one frame's worth
        // -- the gain must clamp to the target, not keep sliding past it.
        var (gain, done) = AudioManager.StepGainLerp(startGain: 0.0, targetGain: 1.0, elapsedMs: 1200, totalMs: 1000);

        Assert.AreEqual(1.0, gain, 0.0001);
        Assert.IsTrue(done);
    }

    [TestMethod]
    public void FadingOutInterpolatesDownward()
    {
        var (gain, done) = AudioManager.StepGainLerp(startGain: 1.0, targetGain: 0.0, elapsedMs: 250, totalMs: 1000);

        Assert.AreEqual(0.75, gain, 0.0001);
        Assert.IsFalse(done);
    }
}
