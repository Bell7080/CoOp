# Red alarm clock with a grinning face on its dial, scuttling on four fleshy spider legs. Tripo image-to-model
# from the concept image (smart low-poly), rigged and animated by rig_animate.py. Front +X, left +Y in Tripo's
# unit box; this model is wider than tall, so its feet are at z=-0.435 rather than -0.5.
# The clock (case, dial, bells, handle) is rigid on Body; each leg is a hip->knee and knee->tip bone pair.
NAME = "ClockSpider"
HEIGHT = 1.4          # unit-box scale: the model ends up ~1.2 m tall and ~1.4 m across the legs
FLOOR = -0.435
WEIGHT_POWER = 8.0

# (hip, knee, tip, rest_drop) measured from the Tripo mesh. Tripo left the back legs short of the floor,
# so they are rotated down by rest_drop degrees in every pose to plant their tips.
LEGS = {
    "L0": ((0.17, 0.27, -0.06), (0.21, 0.39, 0.03), (0.31, 0.49, -0.435), 0.0),
    "R0": ((0.17, -0.27, -0.06), (0.2, -0.41, 0.025), (0.305, -0.485, -0.435), 0.0),
    "L1": ((-0.05, 0.16, -0.07), (-0.16, 0.28, -0.02), (-0.23, 0.425, -0.36), 13.0),
    "R1": ((-0.05, -0.16, -0.07), (-0.19, -0.24, 0.03), (-0.33, -0.34, -0.28), 30.0),
}

BONES = [
    ("Root", (0, 0, FLOOR), (0, 0, FLOOR + 0.08), None, False),
    ("Body", (0.05, 0, -0.15), (0.05, 0, 0.3), "Root", True),
]
for _t, (_h, _k, _tip, _) in LEGS.items():
    BONES += [
        ("Upper" + _t, _h, _k, "Body", True),
        ("Lower" + _t, _k, _tip, "Upper" + _t, True),
    ]

WEIGHT_BIAS = {"Body": 1.0}
# The whole clock is one rigid piece: the round case and dial, plus the bells and handle above it.
HARD = [("Body", lambda x, y, z: x > -0.1 and ((y * y + (z - 0.05) ** 2) ** 0.5 < 0.265 or z > 0.2))]


# ---------------- animation ----------------
# Rotations are about world axes after rig_animate's normalisation: X = creature's left, -Y = forward, Z = up.
# A leg is lifted by turning it about (its outward direction x up) and swung forward by a yaw.

def _leg_pose(t, lift, swing, knee=0.0):
    hip, _, tip, drop = LEGS[t]
    dx, dy = tip[0] - hip[0], tip[1] - hip[1]
    n = (dx * dx + dy * dy) ** 0.5
    wx, wy = dy / n, -dx / n                 # outward direction in world space
    ax, ay = wy, -wx                         # lift axis = outward x up
    side = 1 if t[0] == "L" else -1
    lift -= drop
    up = [("pitch", lift * ax), ("roll", lift * ay), ("yaw", -side * swing)]
    low = [("pitch", knee * ax), ("roll", knee * ay)]
    return [("Upper" + t, up, (0, 0, 0)), ("Lower" + t, low, (0, 0, 0))]


def _gait(t, m, swing, lift, bob, jitter, lean):
    # trot: diagonal pairs (front-left + back-right, front-right + back-left) step together
    out = []
    for leg, group in (("L0", 0), ("R1", 0), ("R0", 1), ("L1", 1)):
        ph = 2 * m.pi * (t + 0.5 * group)
        up = max(0.0, m.cos(ph))             # lifted while it swings forward
        out += _leg_pose(leg, lift * up, swing * m.sin(ph), knee=-0.5 * lift * up)
    s2 = m.sin(4 * m.pi * t)
    out.append(("Body", [("roll", jitter * m.sin(2 * m.pi * t)), ("pitch", -lean), ("yaw", jitter * 0.6 * s2)],
                (0, 0, bob * abs(s2))))
    return out


def idle(t, m):
    # Sits ticking: the case twitches on each tick, one leg after another taps the floor.
    twitch = 1.5 * m.exp(-(t * 8 - m.floor(t * 8)) * 6) * (1 if int(t * 8) % 2 else -1)
    out = [("Body", [("yaw", twitch), ("roll", 1.2 * m.sin(2 * m.pi * t))], (0, 0, 0.006 * m.sin(2 * m.pi * t)))]
    for n, leg in enumerate(("L0", "R1", "R0", "L1")):
        k = max(0.0, m.sin(2 * m.pi * (t - n / 4.0))) ** 8
        out += _leg_pose(leg, 14 * k, 0, knee=-6 * k)
    return out


def walk(t, m):
    return _gait(t, m, swing=28, lift=20, bob=0.02, jitter=3, lean=0)


def chase(t, m):
    # frantic skitter, face pushed forward
    return _gait(t, m, swing=36, lift=28, bob=0.035, jitter=6, lean=-8)


def attack(t, m):
    # Rears up showing the grin (front legs raised and clawing), then slams forward and down.
    rise = m.sin(m.pi * min(1.0, t / 0.55))
    slam = max(0.0, m.sin(m.pi * (t - 0.45) / 0.55)) if t > 0.45 else 0.0
    out = [("Body", [("pitch", -24 * rise + 12 * slam), ("roll", 4 * m.sin(30 * t) * rise)],
            (0, -0.12 * slam, 0.12 * rise - 0.02 * slam))]
    for leg in ("L0", "R0"):
        claw = 10 * m.sin(20 * t) * rise
        out += _leg_pose(leg, 55 * rise + 14 * slam, 25 * rise + claw, knee=-35 * rise)
    for leg in ("L1", "R1"):
        out += _leg_pose(leg, 12 * rise, -6 * rise)
    return out


ANIMS = {"Idle": (2.0, idle), "Walk": (0.5, walk), "Chase": (0.34, chase), "Attack": (0.9, attack)}
