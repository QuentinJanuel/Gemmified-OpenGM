namespace OpenGM;

/// <summary>
/// GameMaker's random number generator: a WELL512a whose seeding differs between engine versions.
///
/// The <b>step function</b> in <see cref="YYRandom()"/> is standard WELL512a and is shared by both
/// versions -- it was verified exactly against a 600-draw capture from GMS 1.4.9999 by solving for
/// the 512-bit state over GF(2) (every operation in the step is linear, so the outputs give a hugely
/// overdetermined system, and a consistent state existing at all proves the step matches).
///
/// The <b>seeding</b> is what differs, and it is a one-character difference with total consequences.
/// Both versions run the same LCG sixteen times and take the high half of each result, but 1.4 keeps
/// the running value <b>signed</b>, so the shift sign-extends, then masks to 31 bits and feeds
/// *that* back:
///
/// <code>
///   1.4 : cur = (int)(cur * 214013 + 2531011);  cur = (cur &gt;&gt; 16) &amp; 0x7FFFFFFF; state[i] = cur;
///   2.x : cur = (uint)(cur * 214013 + 2531011); cur = cur &gt;&gt; 16;                 state[i] = cur;
/// </code>
///
/// The two agree until the LCG first produces a value with the high bit set -- word 10 for seed 0,
/// word 8 for seed 1, word 0 for seed 12345 -- and from there the feedback carries the difference
/// through every remaining word, so the whole stream diverges.
///
/// <see cref="fYYRandom"/> divides by <b>2^32</b> and does not mask off the top bit. That was a
/// third, independent error: masking and dividing by int.MaxValue made every recovered raw value
/// half what it should be, which is what made an earlier pass wrongly conclude the step function
/// itself differed. Worth remembering when reverse-engineering from floats -- get the scale wrong
/// and every downstream bit-level conclusion is garbage.
///
/// Verified: this reproduces <b>867/867</b> captured values exactly -- 800 consecutive draws across
/// three seeds, a 64-seed first-draw map, and the 2147483647 / 65536 / -1 seeds.
/// </summary>
public static class GMRandom
{
    public static uint[] State = new uint[16];
    public static uint Seed;
    public static uint Index;
    public static uint RandomPoly = 0xDA442D24; // TODO : in older gm versions this was 0xDA442D20 incorrectly. there's a setting to go back to this i think?

    public static uint InitRandom(uint seed)
    {
        var passedInSeed = seed;

        Seed = seed;

        if (CompatFlags.LegacyRandomSeeding)
        {
            // GMS 1.4: the running value is a *signed* int, so the shift sign-extends; the result is
            // then masked to 31 bits and that masked value is what feeds the next iteration.
            var curState = unchecked((int)seed);
            for (var i = 0; i < 16; i++)
            {
                curState = unchecked((curState * 214013) + 2531011);
                curState = (curState >> 16) & 0x7FFFFFFF;
                State[i] = (uint)curState;
            }
        }
        else
        {
            // GMS 2.x: unsigned throughout, so the shift is logical and nothing is masked.
            var curState = seed;
            for (var i = 0; i < 16; i++)
            {
                State[i] = curState = ((curState * 214013) + 2531011) >> 16;
            }
        }

        Index = 0;

        return passedInSeed;
    }

    // Generates a random uint, from 0 to uint.Max_Value (probably -1?)
    // Taken from https://github.com/YoYoGames/GameMaker-HTML5/blob/d1b6b3c407470e23d13c736f97cb64cb722c71d1/scripts/functions/Function_Maths.js#L460
    // (I've verified that this generates the same numbers as the C++ implementation)
    public static uint YYRandom() // rand() in HTML, YYRandom() in C++
    {
        var a = State[Index];
        var c = State[(Index - 3) & 15];
        var b = a ^ c ^ (a << 16) ^ (c << 15);
        c = State[(Index + 9) & 15];
        c ^= c >> 11;
        a = State[Index] = b ^ c;
        var d = a ^ ((a << 5) & RandomPoly);
        Index = (Index + 15) & 15;
        a = State[Index];
        State[Index] = a ^ b ^ d ^ (a << 2) ^ (b << 18) ^ (c << 28);
        return State[Index];
    }

    /// <summary>
    /// A random double in [0, 1). The whole 32-bit draw over 2^32 -- <b>not</b> the low 31 bits over
    /// int.MaxValue, which is what this used to do and which halved every value.
    /// </summary>
    public static double fYYRandom() => YYRandom() / 4294967296.0;

    // Generates a random uint from 0 to n-1
    public static uint YYRandom(int n)
    {
        return YYRandom() % (uint)((n ^ n >> 31) - (n >> 31));
    }
}
