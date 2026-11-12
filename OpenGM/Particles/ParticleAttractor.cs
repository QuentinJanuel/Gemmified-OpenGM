namespace OpenGM.Particles
{
    /// <summary>
    /// Pulls (or, with a negative force, pushes) nearby particles toward a point. Created per
    /// particle system by part_attractor_create.
    /// </summary>
    public class ParticleAttractor
    {
        public double X;
        public double Y;

        public double Force;

        /// <summary>Range of the effect. Particles further away than this are untouched.</summary>
        public double Dist;

        /// <summary>
        /// How the force falls off with distance: 0 constant (ps_force_constant), 1 linear,
        /// 2 quadratic.
        /// </summary>
        public int Kind;

        /// <summary>
        /// False (the default) displaces the particle directly, so it slides toward the attractor
        /// without ever building up momentum. True adds to its velocity instead, which is what makes
        /// particles orbit and overshoot.
        /// </summary>
        public bool Additive;
    }
}
