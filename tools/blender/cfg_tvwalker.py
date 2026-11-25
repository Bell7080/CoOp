# CRT television on two hairy human legs (white socks, loafers). Front +X, left +Y in Tripo's unit box.
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


def idle(t, m):
    s = m.sin(2 * m.pi * t)
    return [
        ("Body", [("roll", 2 * s), ("yaw", 4 * m.sin(m.pi * 2 * t + 0.6))], (0, 0, 0.01 * s)),
        ("ThighL", [("pitch", 2 * s)], (0, 0, 0)),
        ("ThighR", [("pitch", -2 * s)], (0, 0, 0)),
    ]


def _walk(t, m, stride, bob, sway, lean):
    s = m.sin(2 * m.pi * t)
    c = m.cos(2 * m.pi * t)
    # the rest pose is already mid-stride (left leg back, right leg forward); swing around it
    return [
        ("Body", [("roll", sway * s), ("pitch", lean)], (0, 0, bob * abs(c))),
        ("ThighL", [("pitch", -stride * (s + 1) * 0.5)], (0, 0, 0)),
        ("ShinL", [("pitch", 20 * max(0, c))], (0, 0, 0)),
        ("FootL", [("pitch", -10 * max(0, -c))], (0, 0, 0)),
        ("ThighR", [("pitch", stride * (s + 1) * 0.5 - stride)], (0, 0, 0)),
        ("ShinR", [("pitch", 20 * max(0, -c))], (0, 0, 0)),
        ("FootR", [("pitch", -10 * max(0, c))], (0, 0, 0)),
    ]


def walk(t, m):
    return _walk(t, m, stride=34, bob=0.04, sway=5, lean=3)


def chase(t, m):
    return _walk(t, m, stride=48, bob=0.08, sway=8, lean=12)


def attack(t, m):
    k = m.sin(m.pi * t)  # lunge and back
    return [
        ("Body", [("pitch", 35 * k), ("roll", 6 * m.sin(6 * m.pi * t))], (0, -0.25 * k, -0.15 * k)),
        ("ThighL", [("pitch", -25 * k)], (0, 0, 0)),
        ("ThighR", [("pitch", -40 * k)], (0, 0, 0)),
        ("ShinR", [("pitch", 25 * k)], (0, 0, 0)),
    ]


ANIMS = {"Idle": (2.4, idle), "Walk": (1.0, walk), "Chase": (0.6, chase), "Attack": (0.7, attack)}
