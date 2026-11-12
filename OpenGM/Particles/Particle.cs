namespace OpenGM.Particles
{
    public class Particle
    {
        public int PartType;
        public int Age;
        public int Lifetime;
        public double X;
        public double Y;
        public double XOld;
        public double YOld;
        public double Speed;
        public double Dir;
        public double Angle;
        public int Color;

        /// <summary>
        /// Set by part_particles_create_color, which overrides the type's colour ramp for these
        /// particular particles. HandleShape skips its per-step colour animation when this is on --
        /// otherwise the requested colour would be replaced on the very next step.
        /// </summary>
        public bool FixedColor;

        public double Alpha;
        public double Size;
        public int SpriteStart;

        /// <summary>
        /// Fixed per-particle random offset. Only ever used to desynchronise the wiggle waves in
        /// <see cref="ParticleManager.Wiggle"/>, so particles of one type don't all move as a block.
        /// </summary>
        public int Ran;
    }
}
