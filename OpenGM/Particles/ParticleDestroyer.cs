namespace OpenGM.Particles
{
    /// <summary>
    /// Removes any particle inside its region. Created per particle system by
    /// part_destroyer_create.
    /// </summary>
    public class ParticleDestroyer
    {
        public double XMin;
        public double YMin;
        public double XMax;
        public double YMax;

        /// <summary>ps_shape_rectangle / _ellipse / _diamond / _line, as 0..3.</summary>
        public int Shape;
    }
}
