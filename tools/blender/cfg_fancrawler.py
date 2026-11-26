# Desk fan with a grinning face, crawling on two long human arms. Front +X, left +Y in Tripo's unit box.
# Hands have three-joint fingers so they press flat on the floor and push off; the face has a jaw.
NAME = "FanCrawler"
HEIGHT = 1.35
WEIGHT_POWER = 6.0

PALM = {1: (0.29, 0.33, -0.38), -1: (0.29, -0.33, -0.38)}
TIPS = [(0.31, 0.46), (0.40, 0.41), (0.41, 0.31), (0.38, 0.23), (0.20, 0.18)]  # (x, |y|) on the left hand

BONES = [
    ("Root", (0, 0, -0.5), (0, 0, -0.42), None, False),
    ("Base", (0.02, 0, -0.3), (0.02, 0, 0.05), "Root", True),
    ("Head", (0.02, 0, 0.05), (0.02, 0, 0.45), "Base", True),
    ("Jaw", (0.1, 0, 0.11), (0.18, 0, 0.06), "Head", True),
]
for side, tag in ((1, "L"), (-1, "R")):
    BONES += [
        ("UpperArm" + tag, (0.02, side * 0.22, 0.2), (0.1, side * 0.43, 0.02), "Head", True),
        ("ForeArm" + tag, (0.1, side * 0.43, 0.02), (0.24, side * 0.39, -0.32), "UpperArm" + tag, True),
        ("Hand" + tag, (0.24, side * 0.39, -0.32), PALM[side], "ForeArm" + tag, True),
    ]
    px, py, pz = PALM[side]
    for n, (tx, ty) in enumerate(TIPS):
        ty *= side
        mid = (px + (tx - px) * 0.55, py + (ty - py) * 0.55, -0.42)
        BONES += [
            (f"Finger{n}A{tag}", (px, py, pz), mid, "Hand" + tag, True),
            (f"Finger{n}B{tag}", mid, (tx, ty, -0.44), f"Finger{n}A{tag}", True),
        ]

WEIGHT_BIAS = {"Head": 1.1, "Base": 1.0, "Jaw": 100.0}  # the jaw only moves its hard-weighted mouth region
# The mouth: front of the face mask, lower middle.
HARD = [
    # the fan cage and face are one stiff piece on the head (arms must not bend the grille)
    ("Head", lambda x, y, z: x > -0.03 and (y * y + (z - 0.2) ** 2) ** 0.5 < 0.25 and z > -0.02),
    ("Jaw", lambda x, y, z: x > 0.11 and abs(y) < 0.07 and 0.035 < z < 0.105),
]


def _fingers(tag, curl, spread=0.0):
    out = []
    for n in range(len(TIPS)):
        out.append((f"Finger{n}A{tag}", [("pitch", curl * 0.6), ("yaw", spread * (n - 2))], (0, 0, 0)))
        out.append((f"Finger{n}B{tag}", [("pitch", curl)], (0, 0, 0)))
    return out


def idle(t, m):
    s = m.sin(2 * m.pi * t)
    return [
        ("Head", [("yaw", 10 * m.sin(2 * m.pi * t)), ("roll", 3 * s)], (0, 0, 0)),
        ("Base", [], (0, 0, 0.01 * s)),
        ("UpperArmL", [("pitch", 3 * s)], (0, 0, 0)),
        ("UpperArmR", [("pitch", -3 * s)], (0, 0, 0)),
        ("Jaw", [("pitch", 4 + 4 * max(0, m.sin(2 * m.pi * t * 2)))], (0, 0, 0)),
    ] + _fingers("L", 6 * s) + _fingers("R", -6 * s)


def _crawl(t, m, reach, lift, sway):
    # Each hand: lift and curl while reaching forward, then plant flat (fingers spread) and pull the body.
    s = m.sin(2 * m.pi * t)
    c = m.cos(2 * m.pi * t)
    up_l, up_r = max(0, c), max(0, -c)
    return [
        ("Base", [("roll", sway * s), ("pitch", 4)], (0, 0, 0.03 * abs(s))),
        ("Head", [("roll", -sway * s), ("yaw", 6 * s)], (0, 0, 0)),
        ("Jaw", [("pitch", 6)], (0, 0, 0)),
        ("UpperArmL", [("pitch", -reach * s), ("roll", -lift * up_l)], (0, 0, 0)),
        ("ForeArmL", [("pitch", -lift * up_l)], (0, 0, 0)),
        ("HandL", [("pitch", 0.6 * reach * s - 25 * up_l)], (0, 0, 0)),
        ("UpperArmR", [("pitch", reach * s), ("roll", lift * up_r)], (0, 0, 0)),
        ("ForeArmR", [("pitch", -lift * up_r)], (0, 0, 0)),
        ("HandR", [("pitch", -0.6 * reach * s - 25 * up_r)], (0, 0, 0)),
    ] + _fingers("L", 55 * up_l - 8 * (1 - up_l), 4 * (1 - up_l)) + _fingers("R", 55 * up_r - 8 * (1 - up_r), 4 * (1 - up_r))


def crawl(t, m):
    return _crawl(t, m, reach=18, lift=14, sway=4)


def chase(t, m):
    return _crawl(t, m, reach=30, lift=24, sway=7)


def attack(t, m):
    # Jump-scare: the face shoots up into yours, mouth wide open, arms flung out, then drops back.
    k = m.sin(m.pi * min(1.0, t * 1.3))
    return [
        ("Base", [("pitch", -20 * k)], (0, -0.35 * k, 0.35 * k)),
        ("Head", [("pitch", -15 * k), ("roll", 8 * m.sin(18 * t) * k)], (0, 0, 0)),
        ("Jaw", [("pitch", 28 * k)], (0, 0, 0)),
        ("UpperArmL", [("pitch", -38 * k), ("roll", -18 * k)], (0, 0, 0)),
        ("UpperArmR", [("pitch", -38 * k), ("roll", 18 * k)], (0, 0, 0)),
        ("ForeArmL", [("pitch", -20 * k)], (0, 0, 0)),
        ("ForeArmR", [("pitch", -20 * k)], (0, 0, 0)),
    ] + _fingers("L", -20 * k, 8 * k) + _fingers("R", -20 * k, 8 * k)


ANIMS = {"Idle": (2.0, idle), "Crawl": (1.0, crawl), "Chase": (0.55, chase), "Attack": (0.9, attack)}
