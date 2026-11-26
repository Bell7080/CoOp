# CRT television on two hairy human legs (white socks, loafers). Front +X, left +Y in Tripo's unit box.
# The cabinet is rigid (hard-weighted to Body); only the legs bend. It bounces around on them and, when it
# sees someone, hops frantically and charges head first.
NAME = "TvWalker"
HEIGHT = 1.9
WEIGHT_POWER = 6.0
BONES = [
    ("Root", (0, 0, -0.5), (0, 0, -0.42), None, False),
    ("Body", (-0.08, 0, -0.18), (-0.08, 0, 0.3), "Root", True),
    ("ThighL", (-0.08, 0.07, -0.2), (-0.18, 0.07, -0.33), "Body", True),
    ("ShinL", (-0.18, 0.07, -0.33), (-0.3, 0.06, -0.43), "ThighL", True),
    ("FootL", (-0.3, 0.06, -0.43), (-0.14, 0.05, -0.48), "ShinL", True),
    ("ThighR", (-0.02, -0.15, -0.2), (0.07, -0.15, -0.33), "Body", True),
    ("ShinR", (0.07, -0.15, -0.33), (0.15, -0.16, -0.43), "ThighR", True),
    ("FootR", (0.15, -0.16, -0.43), (0.34, -0.17, -0.46), "ShinR", True),
]
WEIGHT_BIAS = {"Body": 1.0}
# Everything above the hips is the cabinet, antennae included: one solid piece.
HARD = [("Body", lambda x, y, z: z > -0.19)]


def idle(t, m):
    s = m.sin(2 * m.pi * t)
    return [
        ("Body", [("roll", 1.5 * s), ("yaw", 3 * m.sin(m.pi * 2 * t + 0.6))], (0, 0, 0.012 * s)),
        ("ThighL", [("pitch", 2 * s)], (0, 0, 0)),
        ("ThighR", [("pitch", -2 * s)], (0, 0, 0)),
    ]


def _hop(t, m, stride, hop, sway, lean):
    # Two hops per cycle: each push-off lifts the cabinet ("thump, thump"); knees soak up the landing.
    s = m.sin(2 * m.pi * t)
    c = m.cos(2 * m.pi * t)
    air = abs(m.sin(2 * m.pi * t))            # 0 at each landing, 1 mid-hop
    land = max(0.0, 1 - air * 3)             # squat right at the landing
    return [
        ("Body", [("roll", sway * s), ("pitch", lean - 4 * land)], (0, 0, hop * air - 0.04 * land)),
        ("ThighL", [("pitch", -stride * (s + 1) * 0.5 - 18 * land)], (0, 0, 0)),
        ("ShinL", [("pitch", 22 * max(0, c) + 30 * land)], (0, 0, 0)),
        ("FootL", [("pitch", -12 * max(0, -c) - 12 * land)], (0, 0, 0)),
        ("ThighR", [("pitch", stride * (s + 1) * 0.5 - stride - 18 * land)], (0, 0, 0)),
        ("ShinR", [("pitch", 22 * max(0, -c) + 30 * land)], (0, 0, 0)),
        ("FootR", [("pitch", -12 * max(0, c) - 12 * land)], (0, 0, 0)),
    ]


def walk(t, m):
    return _hop(t, m, stride=30, hop=0.09, sway=4, lean=3)


def chase(t, m):
    # frantic bouncing charge
    return _hop(t, m, stride=46, hop=0.2, sway=9, lean=16)


def attack(t, m):
    # Ram: crouch, then slam the cabinet forward.
    wind = max(0.0, 1 - t * 3)
    k = m.sin(m.pi * min(1.0, max(0.0, (t - 0.15) / 0.6)))
    return [
        ("Body", [("pitch", 40 * k - 10 * wind), ("roll", 6 * m.sin(6 * m.pi * t))], (0, -0.35 * k, -0.1 * k - 0.08 * wind)),
        ("ThighL", [("pitch", -30 * k + 20 * wind)], (0, 0, 0)),
        ("ThighR", [("pitch", -45 * k + 20 * wind)], (0, 0, 0)),
        ("ShinL", [("pitch", 30 * wind)], (0, 0, 0)),
        ("ShinR", [("pitch", 25 * k + 30 * wind)], (0, 0, 0)),
    ]


ANIMS = {"Idle": (2.4, idle), "Walk": (1.0, walk), "Chase": (0.45, chase), "Attack": (0.75, attack)}
