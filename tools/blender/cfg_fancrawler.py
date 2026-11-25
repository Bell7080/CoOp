# Desk fan with a grinning face, crawling on two long human arms. Front +X, left +Y in Tripo's unit box.
NAME = "FanCrawler"
HEIGHT = 1.35
WEIGHT_POWER = 6.0
BONES = [
    ("Root", (0, 0, -0.5), (0, 0, -0.42), None, False),
    ("Base", (0.02, 0, -0.3), (0.02, 0, 0.05), "Root", True),
    ("Head", (0.02, 0, 0.05), (0.02, 0, 0.45), "Base", True),
    ("UpperArmL", (0.02, 0.22, 0.2), (0.1, 0.43, 0.02), "Head", True),
    ("ForeArmL", (0.1, 0.43, 0.02), (0.25, 0.45, -0.34), "UpperArmL", True),
    ("HandL", (0.25, 0.45, -0.34), (0.36, 0.38, -0.45), "ForeArmL", True),
    ("UpperArmR", (0.02, -0.22, 0.2), (0.1, -0.43, 0.02), "Head", True),
    ("ForeArmR", (0.1, -0.43, 0.02), (0.25, -0.45, -0.34), "UpperArmR", True),
    ("HandR", (0.25, -0.45, -0.34), (0.36, -0.38, -0.45), "ForeArmR", True),
]
WEIGHT_BIAS = {"Head": 1.1, "Base": 1.0}


def idle(t, m):
    s = m.sin(2 * m.pi * t)
    return [
        ("Head", [("yaw", 10 * m.sin(2 * m.pi * t)), ("roll", 3 * s)], (0, 0, 0)),
        ("Base", [], (0, 0, 0.01 * s)),
        ("UpperArmL", [("pitch", 3 * s)], (0, 0, 0)),
        ("UpperArmR", [("pitch", -3 * s)], (0, 0, 0)),
        ("HandL", [("pitch", 6 * m.sin(2 * m.pi * t + 1))], (0, 0, 0)),
        ("HandR", [("pitch", -6 * m.sin(2 * m.pi * t + 1))], (0, 0, 0)),
    ]


def _crawl(t, m, reach, lift, sway):
    s = m.sin(2 * m.pi * t)
    c = m.cos(2 * m.pi * t)
    up_l, up_r = max(0, c), max(0, -c)
    return [
        ("Base", [("roll", sway * s), ("pitch", 4)], (0, 0, 0.03 * abs(s))),
        ("Head", [("roll", -sway * s), ("yaw", 6 * s)], (0, 0, 0)),
        ("UpperArmL", [("pitch", -reach * s), ("roll", -lift * up_l)], (0, 0, 0)),
        ("ForeArmL", [("pitch", -lift * up_l)], (0, 0, 0)),
        ("HandL", [("pitch", 0.6 * reach * s)], (0, 0, 0)),
        ("UpperArmR", [("pitch", reach * s), ("roll", lift * up_r)], (0, 0, 0)),
        ("ForeArmR", [("pitch", -lift * up_r)], (0, 0, 0)),
        ("HandR", [("pitch", -0.6 * reach * s)], (0, 0, 0)),
    ]


def crawl(t, m):
    return _crawl(t, m, reach=18, lift=14, sway=4)


def chase(t, m):
    return _crawl(t, m, reach=28, lift=22, sway=7)


def attack(t, m):
    k = m.sin(m.pi * t)
    raise_ = max(0.0, m.sin(m.pi * min(1.0, t * 1.6)))
    return [
        ("Head", [("pitch", 20 * k)], (0, -0.1 * k, 0)),
        ("UpperArmL", [("pitch", -50 * raise_), ("roll", -20 * raise_)], (0, 0, 0)),
        ("UpperArmR", [("pitch", -50 * raise_), ("roll", 20 * raise_)], (0, 0, 0)),
        ("ForeArmL", [("pitch", -30 * raise_)], (0, 0, 0)),
        ("ForeArmR", [("pitch", -30 * raise_)], (0, 0, 0)),
    ]


ANIMS = {"Idle": (2.0, idle), "Crawl": (1.0, crawl), "Chase": (0.6, chase), "Attack": (0.8, attack)}
