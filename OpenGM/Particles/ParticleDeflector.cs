namespace OpenGM.Particles
{
    /// <summary>
    /// Bounces particles that move into its region back out again. Created per particle system by
    /// part_deflector_create.
    /// </summary>
    public class ParticleDeflector
    {
        public double XMin;
        public double YMin;
        public double XMax;
        public double YMax;

        /// <summary>
        /// 0 = ps_deflect_horizontal (reverses the horizontal component, i.e. bounces off a vertical
        /// wall), 1 = ps_deflect_vertical (reverses the vertical component -- a floor or ceiling).
        /// </summary>
        public int Kind;

        /// <summary>Speed lost on each bounce.</summary>
        public double Friction;
    }
}
