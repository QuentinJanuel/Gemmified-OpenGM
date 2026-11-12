using OpenGM.Rendering;

namespace OpenGM.Particles
{
    /// <summary>
    /// A particle system draws like anything else in the room: it sits in <see cref="DrawManager"/>'s
    /// list at its own depth (set by part_system_depth) and draws all of its particles when its turn
    /// comes round, which is what puts them in front of or behind instances. Systems are global
    /// rather than per-room, so one stays registered from part_system_create until
    /// part_system_destroy or a game restart.
    /// </summary>
    public class ParticleSystem : DrawWithDepth
    {
        public List<Particle> Particles = new();
        public List<ParticleEmitter> Emitters = new();
        public List<ParticleAttractor> Attractors = new();
        public List<ParticleDeflector> Deflectors = new();
        public List<ParticleDestroyer> Destroyers = new();
        public List<ParticleChanger> Changers = new();
        public bool oldtonew = true;
        public float XDraw;
        public float YDraw;
        public bool AutomaticUpdate = true;
        public bool AutomaticDraw = true;
        public int ElementID;
        public bool Volatile;

        public override void Draw()
        {
            if (!AutomaticDraw)
            {
                return;
            }

            ParticleManager.DrawSystem(this);
        }

        public override void Destroy() => DrawManager.Unregister(this);
    }
}
