namespace OpenGM.Particles
{
    public class ParticleEmitter
    {
        public bool Created;

        /// <summary>
        /// part_emitter_enable. A disabled emitter keeps its region, type and count but stops
        /// streaming, so enabling it again resumes rather than restarting.
        /// </summary>
        public bool Enabled = true;
        public int Number;
        public double XMin;
        public double YMin;
        public double XMax;
        public double YMax;
        public int Distribution;
        public int Shape;

        /// <summary>
        /// What to stream, set by part_emitter_stream. -1 until then, so an emitter that was given a
        /// particle count but never a type emits nothing rather than whatever type happens to be 0.
        /// </summary>
        public int PartType = -1;
    }
}
