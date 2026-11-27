namespace CoOpHorror.Gameplay
{
    // Each creature's senses and tuning (Docs: creature behaviour plan, 2026-11-27). The TV walker answers to
    // light, the fan crawler to sound, the clock spider to closeness and time. Stats that the level builder
    // also writes are applied here on Awake too, so scenes built before the plan pick them up without a rebuild.
    public sealed class CreatureSenses
    {
        // How far each kind of noise carries for this creature (x the noise's radius; 0 = deaf to it).
        public float Drain = 1f, Footstep = 0f, Door = 1f, Radio = 0f, Alarm = 1f, Bang = 1f;
        public float ViewHalfAngle = TempMonster.ViewHalfAngle;
        public bool SeesTucked;          // finds players squeezed under furniture
        public bool NeedsLight;          // only sees far into lit places
        public float DarkSight = 5f;     // ...and this far in the dark
        public int Watts = 1000;         // its health: power a nose drains out of it, by size (1000 ~ 5000 W)
        public float StunSeconds = 2.5f; // stunned window (TV: wall hit, fan: overheated, spider: torn off)
        // Pursuit: a persistent hunter keeps its target beyond its sight range (up to TrackRange) and through
        // footsteps it hears, and gives up only after the target has been hidden (cover, under furniture,
        // crouched out of view) for HiddenGiveUp seconds.
        public bool Persistent;
        public float TrackRange;
        public float HiddenGiveUp = TempMonster.LoseHiddenAfter;

        public float Hearing(NoiseKind kind) => kind switch
        {
            NoiseKind.Drain => Drain,
            NoiseKind.Footstep => Footstep,
            NoiseKind.Door => Door,
            NoiseKind.Radio => Radio,
            NoiseKind.Alarm => Alarm,
            _ => Bang,
        };

        public static CreatureSenses For(CreatureKind kind) => kind switch
        {
            // Light: the drain flare from 1.5x as far, flashlight beams, lamps. Deaf to footsteps and radios.
            CreatureKind.TvWalker => new CreatureSenses
            {
                Drain = 1.5f, Footstep = 0f, Door = 0.5f, Radio = 0f, Alarm = 1f, Bang = 1f,
                NeedsLight = true, DarkSight = 5f, Watts = 5000, StunSeconds = TempMonster.TvWallStun, // the biggest
            },
            // Sound: shortish sight, hears running from 10 m, walking from 4 m, doors, radios, drains from 1.5x as
            // far. Does not let go of a target until it really hides.
            CreatureKind.FanCrawler => new CreatureSenses
            {
                Drain = 1.5f, Footstep = 1f, Door = 1f, Radio = 1f, Alarm = 1f, Bang = 1f,
                Watts = 3000, StunSeconds = TempMonster.FanOverheat,
                Persistent = true, TrackRange = 30f, HiddenGiveUp = 2f,
            },
            // Closeness: a wide view in any light, under furniture too, but half-deaf. Ignores its own alarm.
            CreatureKind.ClockSpider => new CreatureSenses
            {
                Drain = 0.5f, Footstep = 0f, Door = 0f, Radio = 0f, Alarm = 0f, Bang = 0.5f,
                ViewHalfAngle = 90f, SeesTucked = true, Watts = 1000, StunSeconds = TempMonster.SpiderThrownStun, // the smallest
            },
            _ => new CreatureSenses { Footstep = 0f, Radio = 0f },
        };

        // Stats from the plan (the builder writes the same numbers).
        public void Tune(TempMonster m)
        {
            switch (m.Kind)
            {
                case CreatureKind.TvWalker:
                    m.ChaseSpeed = 3.0f;             // slow between charges
                    m.LungeRange = 9f; m.LungeSpeed = 8f; m.LungeSeconds = 1.3f; m.LungeCooldown = 4f;
                    m.Knockback = 13f; m.KnockUp = 5f;
                    break;
                case CreatureKind.FanCrawler:
                    m.SightRange = 12f;              // spots you at 12 m, then keeps you (Persistent) until you hide
                    m.AttackRange = 1.8f;
                    m.LungeRange = 5.5f; m.LungeSpeed = 9f; m.LungeSeconds = 0.7f; m.LungeCooldown = 3f;
                    m.Knockback = 10f; m.KnockUp = 6f;
                    break;
                case CreatureKind.ClockSpider:
                    m.LungeRange = 3f; m.LungeSpeed = 8f; m.LungeSeconds = 0.45f; m.LungeCooldown = 2.5f;
                    break;
            }
        }
    }
}
