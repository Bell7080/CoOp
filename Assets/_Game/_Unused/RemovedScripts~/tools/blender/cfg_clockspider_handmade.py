# Config for the hand-modelled eight-legged clock spider (make_clockspider.py), replaced on 2026-11-27 by the
# Tripo model (tools/blender/cfg_clockspider.py). Built in Tripo's unit box (front +X, left +Y, up +Z,
# feet z=-0.5) so rig_animate.py can rig it like the Tripo models.
import math as _m

NAME = "ClockSpider"
HEIGHT = 1.2
WEIGHT_POWER = 8.0

ZC = 0.08            # dial centre height
RC = 0.32            # case radius
X_FRONT, X_BACK = 0.1, -0.1

LEG_ANGLES = [38.0, 12.0, -16.0, -48.0]   # degrees from straight out sideways, toward the front (+X)
LEG_HIP_X = [0.05, 0.02, -0.02, -0.06]


def leg(side, i):
    phi = _m.radians(LEG_ANGLES[i])
    d = (_m.sin(phi), side * _m.cos(phi))
    hip = (LEG_HIP_X[i], side * 0.24, ZC - 0.1)
    knee = (hip[0] + d[0] * 0.24, hip[1] + d[1] * 0.24, ZC + 0.12)
    tip = (hip[0] + d[0] * 0.56, hip[1] + d[1] * 0.56, -0.5)
    return hip, knee, tip, d


LEGS = [(side, i) for side in (1, -1) for i in range(4)]


def tag(side, i):
    return ("L" if side > 0 else "R") + str(i)


BONES = [
    ("Root", (0, 0, -0.5), (0, 0, -0.42), None, False),
    ("Body", (0, 0, ZC - 0.18), (0, 0, ZC + 0.3), "Root", True),
]
for _s, _i in LEGS:
    _h, _k, _t, _ = leg(_s, _i)
    BONES += [
        ("Upper" + tag(_s, _i), _h, _k, "Body", True),
        ("Lower" + tag(_s, _i), _k, _t, "Upper" + tag(_s, _i), True),
    ]

WEIGHT_BIAS = {"Body": 1.0}
HARD = [("Body", lambda x, y, z: (X_BACK - 0.06 < x < X_FRONT + 0.02 and (y * y + (z - ZC) ** 2) ** 0.5 < RC + 0.005)
         or z > ZC + RC - 0.02)]


def _leg_pose(side, i, lift, swing, knee=0.0):
    _, _, _, d = leg(side, i)
    wx, wy = d[1], -d[0]
    ax, ay = wy, -wx
    up = [("pitch", lift * ax), ("roll", lift * ay), ("yaw", -side * swing)]
    low = [("pitch", knee * ax), ("roll", knee * ay)]
    return [("Upper" + tag(side, i), up, (0, 0, 0)), ("Lower" + tag(side, i), low, (0, 0, 0))]


def _gait(t, m, swing, lift, bob, jitter, lean):
    out = []
    for side, i in LEGS:
        group = (i + (0 if side > 0 else 1)) % 2
        ph = 2 * m.pi * (t + 0.5 * group)
        up = max(0.0, m.cos(ph))
        out += _leg_pose(side, i, lift * up, swing * m.sin(ph), knee=-0.5 * lift * up)
    s2 = m.sin(4 * m.pi * t)
    out.append(("Body", [("roll", jitter * m.sin(2 * m.pi * t)), ("pitch", -lean), ("yaw", jitter * 0.6 * s2)],
                (0, 0, bob * abs(s2))))
    return out


def idle(t, m):
    twitch = 1.5 * m.exp(-(t * 8 - m.floor(t * 8)) * 6) * (1 if int(t * 8) % 2 else -1)
    out = [("Body", [("yaw", twitch), ("roll", 1.2 * m.sin(2 * m.pi * t))], (0, 0, 0.006 * m.sin(2 * m.pi * t)))]
    for n, (side, i) in enumerate(LEGS):
        k = max(0.0, m.sin(2 * m.pi * (t - n / 8.0))) ** 8
        out += _leg_pose(side, i, 14 * k, 0, knee=-6 * k)
    return out


def walk(t, m):
    return _gait(t, m, swing=16, lift=18, bob=0.02, jitter=3, lean=0)


def chase(t, m):
    return _gait(t, m, swing=24, lift=26, bob=0.035, jitter=6, lean=-8)


def attack(t, m):
    rise = m.sin(m.pi * min(1.0, t / 0.55))
    slam = max(0.0, m.sin(m.pi * (t - 0.45) / 0.55)) if t > 0.45 else 0.0
    out = [("Body", [("pitch", -24 * rise + 12 * slam), ("roll", 4 * m.sin(30 * t) * rise)],
            (0, -0.12 * slam, 0.12 * rise - 0.02 * slam))]
    for side, i in LEGS:
        if i == 0:
            claw = 10 * m.sin(20 * t) * rise
            out += _leg_pose(side, i, 55 * rise + 14 * slam, 25 * rise + claw, knee=-35 * rise)
        elif i == 3:
            out += _leg_pose(side, i, 12 * rise, -6 * rise)
        else:
            out += _leg_pose(side, i, 4 * rise + 6 * slam, 0)
    return out


ANIMS = {"Idle": (2.0, idle), "Walk": (0.5, walk), "Chase": (0.34, chase), "Attack": (0.9, attack)}
