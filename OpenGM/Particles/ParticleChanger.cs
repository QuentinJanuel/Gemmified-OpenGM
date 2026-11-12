namespace OpenGM.Particles
{
    /// <summary>
    /// Retypes particles of one type into another as they pass through its region -- smoke turning
    /// to steam over a vent, a spark cooling into an ember. Created per particle system by
    /// part_changer_create.
    /// </summary>
    public class ParticleChanger
    {
        public double XMin;
        public double YMin;
        public double XMax;
        public double YMax;

        /// <summary>ps_shape_rectangle / _ellipse / _diamond / _line, as 0..3.</summary>
        public int Shape;

        /// <summary>The type to look for. -1 until part_changer_types is called.</summary>
        public int PartTypeFrom = -1;

        /// <summary>The type to change it into. -1 until part_changer_types is called.</summary>
        public int PartTypeTo = -1;

        /// <summary>
        /// 0 = ps_change_motion (re-roll the motion attributes only), 1 = ps_change_shape (re-roll
        /// the appearance only), 2 = ps_change_all (replace the particle outright).
        /// </summary>
        public int Kind = 2;
    }
}
