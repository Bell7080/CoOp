# ElecMole player: squishy mole alien. Coordinates in Tripo's unit box (front +X, left +Y, up +Z, feet z=-0.5).
NAME = "MoleAlien"
HEIGHT = 1.25
WEIGHT_POWER = 3.5
BONES = [
    ("Root", (0, 0, -0.5), (0, 0, -0.42), None, False),
    ("Hips", (-0.02, 0, -0.38), (-0.02, 0, -0.12), "Root", True),
    ("Spine", (-0.02, 0, -0.12), (0, 0, 0.14), "Hips", True),
    ("Head", (0, 0, 0.14), (0, 0, 0.48), "Spine", True),
    ("Nose1", (0.22, 0, 0.12), (0.31, 0, 0.0), "Head", True),
    ("Nose2", (0.31, 0, 0.0), (0.38, 0, -0.09), "Nose1", True),
    ("Nose3", (0.38, 0, -0.09), (0.46, 0, -0.19), "Nose2", True),
    ("ArmL1", (0, 0.27, -0.08), (0, 0.37, -0.15), "Spine", True),
    ("ArmL2", (0, 0.37, -0.15), (0, 0.49, -0.2), "ArmL1", True),
    ("ArmR1", (0, -0.27, -0.08), (0, -0.37, -0.15), "Spine", True),
    ("ArmR2", (0, -0.37, -0.15), (0, -0.49, -0.2), "ArmR1", True),
    ("LegL", (-0.05, 0.17, -0.33), (-0.04, 0.18, -0.46), "Hips", True),
    ("FootL", (-0.04, 0.18, -0.46), (0.08, 0.18, -0.49), "LegL", True),
    ("LegR", (-0.05, -0.17, -0.33), (-0.04, -0.18, -0.46), "Hips", True),
    ("FootR", (-0.04, -0.18, -0.46), (0.08, -0.18, -0.49), "LegR", True),
]
WEIGHT_BIAS = {"Hips": 0.85, "Spine": 1.0, "Head": 1.15, "LegL": 1.5, "LegR": 1.5, "FootL": 1.3, "FootR": 1.3}


def _floppy(m, t, amp, lag):
    return amp * m.sin(2 * m.pi * (t - lag))


def idle(t, m):
    s = m.sin(2 * m.pi * t)
    return [
        ("Hips", [], (0, 0, 0.012 * s)),
        ("Spine", [("pitch", 0.8 * s)], (0, 0, 0)),
        ("Head", [("roll", 3 * m.sin(2 * m.pi * t + 1))], (0, 0, 0)),
        ("Nose1", [("roll", _floppy(m, t, 8, 0.1)), ("pitch", _floppy(m, t, 4, 0.0))], (0, 0, 0)),
        ("Nose2", [("roll", _floppy(m, t, 10, 0.2))], (0, 0, 0)),
        ("Nose3", [("roll", _floppy(m, t, 12, 0.3))], (0, 0, 0)),
        ("ArmL1", [("roll", -6 + _floppy(m, t, 5, 0.1))], (0, 0, 0)),
        ("ArmL2", [("roll", _floppy(m, t, 8, 0.25))], (0, 0, 0)),
        ("ArmR1", [("roll", 6 - _floppy(m, t, 5, 0.1))], (0, 0, 0)),
        ("ArmR2", [("roll", -_floppy(m, t, 8, 0.25))], (0, 0, 0)),
    ]


def _gait(t, m, stride, bounce, waddle, flop, lean):
    s = m.sin(2 * m.pi * t)
    c = m.cos(2 * m.pi * t)
    return [
        ("Hips", [("roll", waddle * s)], (0, 0, bounce * abs(c))),
        ("Spine", [("pitch", lean), ("roll", -0.2 * waddle * s)], (0, 0, 0)),
        ("Head", [("roll", -0.3 * waddle * s), ("pitch", 3 * c)], (0, 0, 0)),
        ("LegL", [("pitch", -stride * s)], (0, 0, 0)),
        ("FootL", [("pitch", 0.5 * stride * max(0, s))], (0, 0, 0)),
        ("LegR", [("pitch", stride * s)], (0, 0, 0)),
        ("FootR", [("pitch", 0.5 * stride * max(0, -s))], (0, 0, 0)),
        ("ArmL1", [("pitch", flop * m.sin(2 * m.pi * (t - 0.1))), ("roll", -8)], (0, 0, 0)),
        ("ArmL2", [("pitch", 1.3 * flop * m.sin(2 * m.pi * (t - 0.25)))], (0, 0, 0)),
        ("ArmR1", [("pitch", -flop * m.sin(2 * m.pi * (t - 0.1))), ("roll", 8)], (0, 0, 0)),
        ("ArmR2", [("pitch", -1.3 * flop * m.sin(2 * m.pi * (t - 0.25)))], (0, 0, 0)),
        ("Nose1", [("roll", -0.8 * waddle * m.sin(2 * m.pi * (t - 0.1))), ("pitch", -6 * c)], (0, 0, 0)),
        ("Nose2", [("roll", -1.2 * waddle * m.sin(2 * m.pi * (t - 0.2))), ("pitch", -8 * m.cos(2 * m.pi * (t - 0.15)))], (0, 0, 0)),
        ("Nose3", [("roll", -1.6 * waddle * m.sin(2 * m.pi * (t - 0.3))), ("pitch", -10 * m.cos(2 * m.pi * (t - 0.25)))], (0, 0, 0)),
    ]


def walk(t, m):
    return _gait(t, m, stride=26, bounce=0.03, waddle=5, flop=24, lean=3)


def run(t, m):
    return _gait(t, m, stride=34, bounce=0.05, waddle=6, flop=34, lean=6)


def crouch(t, m):
    s = m.sin(2 * m.pi * t)
    return [
        ("Hips", [], (0, 0, -0.12 + 0.01 * s)),
        ("Spine", [("pitch", 10 + 1 * s)], (0, 0, 0)),
        ("Head", [("pitch", -8)], (0, 0, 0)),
        ("LegL", [("roll", -8)], (0, 0.0, 0.12)),
        ("LegR", [("roll", 8)], (0, 0.0, 0.12)),
        ("ArmL1", [("pitch", -25), ("roll", -15)], (0, 0, 0)),
        ("ArmR1", [("pitch", -25), ("roll", 15)], (0, 0, 0)),
        ("Nose1", [("pitch", -10), ("roll", _floppy(m, t, 5, 0))], (0, 0, 0)),
        ("Nose2", [("pitch", -10), ("roll", _floppy(m, t, 7, 0.15))], (0, 0, 0)),
        ("Nose3", [("pitch", -10)], (0, 0, 0)),
    ]


ANIMS = {"Idle": (2.0, idle), "Walk": (0.8, walk), "Run": (0.5, run), "Crouch": (1.6, crouch)}
