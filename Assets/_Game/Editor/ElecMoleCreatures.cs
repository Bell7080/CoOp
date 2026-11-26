using System.Collections.Generic;
using CoOpHorror.Gameplay;
using CoOpHorror.Player;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CoOpHorror.Editor
{
    // Placeholder low-poly creatures after the concept art (analogue machines with human parts and grinning
    // faces): a CRT TV walking on hairy legs in white socks, an alarm clock on spider legs, and a desk fan
    // crawling on long human arms. Procedural limb animation (CreatureAnimator), TempMonster brains with
    // their own stats. Also the squishy mole alien player model. Root pivots are at the feet.
    public static partial class ElecMolePrototypeSetup
    {
        private static void BuildCreatures()
        {
            BuildTvWalker(new Vector3(27f, 0f, -19f), new[]
            {
                new Vector3(27f, 0f, -19f), new Vector3(9f, 0f, -19f), new Vector3(-9f, 0f, -19f),
                new Vector3(-9f, 0f, 0f), new Vector3(9f, 0f, 0f), new Vector3(27f, 0f, 0f),
            });
            BuildClockSpider(new Vector3(27f, 0f, 0f), new[]
            {
                new Vector3(27f, 0f, 0f), new Vector3(25f, 0f, 17f), new Vector3(9f, 0f, 19f),
                new Vector3(9f, 0f, 0f), new Vector3(9f, 0f, -19f), new Vector3(27f, 0f, -19f),
            });
            BuildFanCrawler(new Vector3(-27f, 0f, 0f), new[]
            {
                new Vector3(-27f, 0f, 0f), new Vector3(-27f, 0f, 19f), new Vector3(-9f, 0f, 19f), new Vector3(-9f, 0f, 0f),
            });
        }

        private static (GameObject root, Transform visual) CreatureRoot(string name, Vector3 position, float radius, float height)
        {
            var root = NetRoot(name, position, 0f);
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(0f, height / 2f, 0f);
            var agent = root.AddComponent<NavMeshAgent>();
            agent.baseOffset = 0f;
            agent.radius = Mathf.Min(radius, 0.5f);
            agent.height = height;
            agent.acceleration = 12f;
            agent.angularSpeed = 540f;
            agent.stoppingDistance = 0.4f;
            var sync = root.AddComponent<NetworkTransform>();
            sync.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            sync.SyncRotAngleX = sync.SyncRotAngleZ = false;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            return (root, visual);
        }

        private static TempMonster Brain(GameObject root, string name, Vector3[] route, float patrol, float investigate, float chase, float sight, float eye, int damage, Renderer[] eyes)
        {
            var waypointRoot = new GameObject(Prefix + name + "Route").transform;
            var waypoints = new Transform[route.Length];
            for (var i = 0; i < route.Length; i++)
            {
                waypoints[i] = new GameObject("Waypoint_" + i).transform;
                waypoints[i].SetParent(waypointRoot, false);
                // the layout may have put a structure on a planned point: use the nearest walkable spot
                var spot = NearestWalkable(route[i]);
                waypoints[i].position = UnityEngine.AI.NavMesh.SamplePosition(spot, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : spot;
            }
            var monster = root.AddComponent<TempMonster>();
            monster.Waypoints = waypoints;
            monster.Eyes = eyes;
            monster.PatrolSpeed = patrol;
            monster.InvestigateSpeed = investigate;
            monster.ChaseSpeed = chase;
            monster.SightRange = sight;
            monster.EyeHeight = eye;
            monster.AttackDamage = damage;
            root.GetComponent<NavMeshAgent>().speed = patrol;
            return monster;
        }

        // A grinning human face on a flat surface facing local +Z. Returns the eye renderers (glow when chasing).
        private static Renderer[] Face(Transform parent, Vector3 center, float scale)
        {
            var black = Mat("FaceBlack", new Color(0.02f, 0.02f, 0.02f), unlit: true);
            var eyeMat = Mat("MonsterEye", new Color(0.55f, 0.08f, 0.05f), unlit: true);
            var eyes = new List<Renderer>();
            foreach (var side in new[] { -1f, 1f })
                eyes.Add(Shape(parent, "Eye", PrimitiveType.Cube, center + new Vector3(side * 0.13f, 0.09f, 0f) * scale, Vector3.zero, new Vector3(0.07f, 0.05f, 0.012f) * scale, eyeMat).GetComponent<Renderer>());
            // Wide smile with teeth.
            for (var i = -3; i <= 3; i++)
            {
                var x = i * 0.055f;
                var y = -0.08f + x * x * 3.2f;
                Shape(parent, "Smile", PrimitiveType.Cube, center + new Vector3(x, y, 0f) * scale, new Vector3(0f, 0f, i * 8f), new Vector3(0.06f, 0.025f, 0.012f) * scale, black);
                if (i > -3 && i < 3) Shape(parent, "Tooth", PrimitiveType.Cube, center + new Vector3(x, y + 0.025f, -0.002f) * scale, Vector3.zero, new Vector3(0.035f, 0.025f, 0.012f) * scale, Mat("Tooth", new Color(0.9f, 0.88f, 0.75f), unlit: true));
            }
            return eyes.ToArray();
        }

        private static CreatureAnimator.Limb Limb(Transform bone, Vector3 axis, float phase, float swing) =>
            new() { Bone = bone, Axis = axis, Phase = phase, Swing = swing };

        // CRT television on hairy human legs with white socks and black shoes.
        private static void BuildTvWalker(Vector3 position, Vector3[] route)
        {
            var (root, visual) = CreatureRoot("Creature_TvWalker", position, 0.45f, 1.9f);
            if (SpawnCharacter(visual, "TvWalker", Color.white, root.transform, out var rig) != null)
            {
                rig.MoveState = "Walk";
                rig.FastState = "Chase";
                rig.AttackState = "Attack";
                rig.FastSpeed = 99f;
                rig.ClipSpeedPerMetre = 0.9f;        // ~1.1 m per walk cycle
                rig.FastClipSpeedPerMetre = 0.5f;    // long bounding strides while charging
                rig.StepsPerCycle = 2;               // thump, thump
                rig.StepVolume = 0.45f;
                var tv = Brain(root, "TvWalker", route, 1.6f, 2.6f, 3.4f, 14f, 1.4f, 22, null);
                // spots you, bounces frantically and rams you head first from far away
                tv.LungeRange = 9f; tv.LungeSpeed = 7.5f; tv.LungeSeconds = 1.3f; tv.LungeCooldown = 4f; tv.Knockback = 13f; tv.KnockUp = 5f;
                return;
            }
            var plastic = Mat("TvPlastic", new Color(0.32f, 0.25f, 0.18f), tex: Texture("Wood"), tiling: 1f);
            var skin = Mat("HairyLeg", new Color(0.62f, 0.45f, 0.36f), tex: Texture("Carpet"), tiling: 2f);
            var sock = Mat("Sock", new Color(0.9f, 0.9f, 0.88f));
            var shoe = Mat("Shoe", new Color(0.05f, 0.05f, 0.05f));
            var limbs = new List<CreatureAnimator.Limb>();
            foreach (var side in new[] { -1f, 1f })
            {
                var hip = new GameObject(side < 0 ? "HipL" : "HipR").transform;
                hip.SetParent(root.transform, false);
                hip.localPosition = new Vector3(side * 0.2f, 0.95f, 0f);
                Shape(hip, "Thigh", PrimitiveType.Cube, new Vector3(0f, -0.25f, 0f), Vector3.zero, new Vector3(0.15f, 0.5f, 0.15f), skin);
                Shape(hip, "Shin", PrimitiveType.Cube, new Vector3(0f, -0.62f, 0f), Vector3.zero, new Vector3(0.12f, 0.3f, 0.12f), skin);
                Shape(hip, "Sock", PrimitiveType.Cube, new Vector3(0f, -0.83f, 0f), Vector3.zero, new Vector3(0.14f, 0.16f, 0.14f), sock);
                Shape(hip, "Shoe", PrimitiveType.Cube, new Vector3(0f, -0.92f, 0.07f), Vector3.zero, new Vector3(0.17f, 0.08f, 0.3f), shoe);
                limbs.Add(Limb(hip, Vector3.right, side < 0 ? 0f : Mathf.PI, 32f));
            }
            Shape(visual, "Cabinet", PrimitiveType.Cube, new Vector3(0f, 1.35f, 0f), Vector3.zero, new Vector3(0.95f, 0.78f, 0.75f), plastic);
            Shape(visual, "Back", PrimitiveType.Cube, new Vector3(0f, 1.35f, -0.48f), Vector3.zero, new Vector3(0.6f, 0.5f, 0.3f), plastic);
            Shape(visual, "Screen", PrimitiveType.Cube, new Vector3(-0.08f, 1.37f, 0.376f), Vector3.zero, new Vector3(0.66f, 0.54f, 0.01f), Mat("TvGlow", new Color(0.55f, 0.65f, 0.62f), unlit: true));
            Shape(visual, "Knob", PrimitiveType.Cylinder, new Vector3(0.38f, 1.45f, 0.38f), new Vector3(90f, 0f, 0f), new Vector3(0.08f, 0.02f, 0.08f), shoe);
            Shape(visual, "AntennaL", PrimitiveType.Cylinder, new Vector3(-0.15f, 1.95f, -0.1f), new Vector3(0f, 0f, 25f), new Vector3(0.02f, 0.3f, 0.02f), shoe);
            Shape(visual, "AntennaR", PrimitiveType.Cylinder, new Vector3(0.15f, 1.95f, -0.1f), new Vector3(0f, 0f, -25f), new Vector3(0.02f, 0.3f, 0.02f), shoe);
            var eyes = Face(visual, new Vector3(-0.08f, 1.38f, 0.383f), 1.1f);
            Brain(root, "TvWalker", route, 1.6f, 2.6f, 3.4f, 14f, 1.4f, 22, eyes);
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Limbs = limbs.ToArray();
            anim.Body = visual;
            anim.BobAmount = 0.06f;
            anim.StrideRate = 4.5f;
        }

        // Alarm clock on eight spider legs, bells on top.
        private static void BuildClockSpider(Vector3 position, Vector3[] route)
        {
            var (root, visual) = CreatureRoot("Creature_ClockSpider", position, 0.55f, 1.3f);
            var red = Mat("ClockRed", new Color(0.6f, 0.1f, 0.08f), tex: Texture("Rust"), tiling: 1f);
            var metal = Mat("ApplianceMetal", new Color(0.6f, 0.62f, 0.66f));
            var leg = Mat("SpiderLeg", new Color(0.12f, 0.1f, 0.09f));
            Shape(visual, "Case", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.8f, 0.14f, 0.8f), red);
            Shape(visual, "Dial", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0.145f), new Vector3(90f, 0f, 0f), new Vector3(0.68f, 0.01f, 0.68f), Mat("ClockFace", new Color(0.88f, 0.86f, 0.75f), unlit: true));
            Shape(visual, "HandH", PrimitiveType.Cube, new Vector3(0.06f, 0.92f, 0.155f), new Vector3(0f, 0f, -40f), new Vector3(0.03f, 0.18f, 0.01f), Mat("FaceBlack", new Color(0.02f, 0.02f, 0.02f), unlit: true));
            foreach (var side in new[] { -1f, 1f })
            {
                Shape(visual, "Bell", PrimitiveType.Sphere, new Vector3(side * 0.24f, 1.28f, 0f), Vector3.zero, new Vector3(0.26f, 0.2f, 0.26f), metal);
                Shape(visual, "Foot", PrimitiveType.Cube, new Vector3(side * 0.22f, 0.46f, 0f), new Vector3(0f, 0f, side * 20f), new Vector3(0.05f, 0.14f, 0.05f), metal);
            }
            var eyes = Face(visual, new Vector3(0f, 0.8f, 0.157f), 1f);
            var limbs = new List<CreatureAnimator.Limb>();
            for (var i = 0; i < 8; i++)
            {
                var side = i < 4 ? -1f : 1f;
                var z = -0.27f + (i % 4) * 0.18f;
                var hip = new GameObject("Leg" + i).transform;
                hip.SetParent(root.transform, false);
                hip.localPosition = new Vector3(side * 0.3f, 0.75f, z);
                hip.localRotation = Quaternion.Euler(0f, side * (60f - (i % 4) * 40f), 0f);
                Shape(hip, "Upper", PrimitiveType.Cube, new Vector3(side * 0.28f, 0.18f, 0f), new Vector3(0f, 0f, side * -45f), new Vector3(0.05f, 0.6f, 0.05f), leg);
                Shape(hip, "Lower", PrimitiveType.Cube, new Vector3(side * 0.66f, -0.3f, 0f), new Vector3(0f, 0f, side * 18f), new Vector3(0.04f, 0.95f, 0.04f), leg);
                limbs.Add(Limb(hip, Vector3.up, (i % 2 == 0 ? 0f : Mathf.PI) + side, 22f));
            }
            Brain(root, "ClockSpider", route, 2.2f, 3.2f, 4.0f, 10f, 0.9f, 15, eyes);
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Limbs = limbs.ToArray();
            anim.Body = visual;
            anim.BobAmount = 0.04f;
            anim.StrideRate = 7f;
        }

        // Desk fan dragging itself on two long human arms.
        private static void BuildFanCrawler(Vector3 position, Vector3[] route)
        {
            var (root, visual) = CreatureRoot("Creature_FanCrawler", position, 0.5f, 1.4f);
            if (SpawnCharacter(visual, "FanCrawler", Color.white, root.transform, out var rig) != null)
            {
                rig.MoveState = "Crawl";
                rig.FastState = "Chase";
                rig.AttackState = "Attack";
                rig.FastSpeed = 99f;
                rig.ClipSpeedPerMetre = 1f;          // a hand reaches ~1 m per crawl cycle: planted hands don't slide
                rig.FastClipSpeedPerMetre = 0.48f;
                rig.StepsPerCycle = 2;
                rig.StepVolume = 0.2f;
                var fan = Brain(root, "FanCrawler", route, 1.3f, 2.4f, 3.0f, 16f, 1.0f, 25, null);
                // scuttles up fast and shoves its face into yours
                fan.LungeRange = 5.5f; fan.LungeSpeed = 9f; fan.LungeSeconds = 0.7f; fan.LungeCooldown = 3f; fan.Knockback = 10f; fan.KnockUp = 6f;
                return;
            }
            var plastic = Mat("FanPlastic", new Color(0.75f, 0.78f, 0.72f));
            var skin = Mat("ArmSkin", new Color(0.72f, 0.55f, 0.45f));
            Shape(visual, "Base", PrimitiveType.Cylinder, new Vector3(0f, 0.05f, -0.45f), Vector3.zero, new Vector3(0.6f, 0.05f, 0.6f), plastic);
            Shape(visual, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 0.5f, -0.3f), new Vector3(25f, 0f, 0f), new Vector3(0.08f, 0.45f, 0.08f), plastic);
            Shape(visual, "Cage", PrimitiveType.Cylinder, new Vector3(0f, 0.95f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.9f, 0.08f, 0.9f), Mat("FanCage", new Color(0.4f, 0.42f, 0.42f)));
            var blades = new GameObject("Blades").transform;
            blades.SetParent(visual, false);
            blades.localPosition = new Vector3(0f, 0.95f, 0.06f);
            for (var i = 0; i < 3; i++)
                Shape(blades, "Blade", PrimitiveType.Cube, Quaternion.Euler(0f, 0f, i * 120f) * new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0f, i * 120f), new Vector3(0.14f, 0.36f, 0.01f), plastic);
            Shape(visual, "Hub", PrimitiveType.Cylinder, new Vector3(0f, 0.95f, 0.1f), new Vector3(90f, 0f, 0f), new Vector3(0.3f, 0.02f, 0.3f), Mat("ClockFace", new Color(0.88f, 0.86f, 0.75f), unlit: true));
            var eyes = Face(visual, new Vector3(0f, 0.95f, 0.125f), 0.55f);
            var limbs = new List<CreatureAnimator.Limb>();
            foreach (var side in new[] { -1f, 1f })
            {
                var shoulder = new GameObject(side < 0 ? "ShoulderL" : "ShoulderR").transform;
                shoulder.SetParent(root.transform, false);
                shoulder.localPosition = new Vector3(side * 0.42f, 0.85f, 0.05f);
                Shape(shoulder, "UpperArm", PrimitiveType.Cube, new Vector3(side * 0.08f, -0.05f, 0.35f), new Vector3(10f, 0f, 0f), new Vector3(0.11f, 0.11f, 0.75f), skin);
                Shape(shoulder, "Forearm", PrimitiveType.Cube, new Vector3(side * 0.12f, -0.45f, 0.75f), new Vector3(-60f, 0f, 0f), new Vector3(0.09f, 0.09f, 0.85f), skin);
                Shape(shoulder, "Hand", PrimitiveType.Cube, new Vector3(side * 0.12f, -0.8f, 0.95f), Vector3.zero, new Vector3(0.18f, 0.05f, 0.22f), skin);
                limbs.Add(Limb(shoulder, Vector3.right, side < 0 ? 0f : Mathf.PI, 28f));
            }
            Brain(root, "FanCrawler", route, 1.3f, 2.4f, 3.0f, 16f, 1.0f, 25, eyes);
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Limbs = limbs.ToArray();
            anim.Body = visual;
            anim.BobAmount = 0.08f;
            anim.StrideRate = 3.5f;
            anim.Spinners = new[] { blades };
            anim.SpinSpeed = 720f;
        }

        // ---------------- player: squishy mole alien ----------------

        private static void BuildMoleModel(GameObject root)
        {
            foreach (var name in new[] { "Body", "Head", "MoleBody" })
            {
                var old = root.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var controller = root.GetComponent<CharacterController>();
            controller.height = FirstPersonController.StandHeight;
            controller.center = new Vector3(0f, FirstPersonController.StandCenterY, 0f);
            controller.radius = 0.42f;
            var pivot = root.transform.Find("AimPivot");
            pivot.localPosition = new Vector3(0f, FirstPersonController.StandEyeY, 0f);
            foreach (var name in new[] { "LeftFist", "RightFist" })
            {
                var old = pivot.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            var skin = Mat("MoleSkin", Color.white);
            var belly = Mat("MoleBelly", new Color(0.95f, 0.88f, 0.75f));
            var pink = Mat("MolePink", new Color(0.95f, 0.55f, 0.6f));
            var dark = Mat("FaceBlack", new Color(0.02f, 0.02f, 0.02f), unlit: true);
            var glow = Mat("MoleGlow", new Color(0.4f, 1f, 0.95f), unlit: true);
            var claw = Mat("MoleClaw", new Color(0.92f, 0.9f, 0.8f));

            var body = new GameObject("MoleBody").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0f, -1f, 0f); // squash pivot at the feet
            var skinParts = new List<Renderer>();
            var details = new List<Renderer>();
            var model = SpawnCharacter(body, "MoleAlien", Color.white, root.transform, out var rig);
            if (model != null)
            {
                // Body: the player's colour. Nose: the same colour, muted. Eyes: as painted.
                var nose = new List<Renderer>();
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.name == "MoleBlush")
                    {
                        // skinned blush quads from paint_mole.py: soft pink, follows the cheeks exactly
                        r.sharedMaterial = BlushMaterial();
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        details.Add(r);
                        continue;
                    }
                    (r.name == "MoleEyes" ? details : r.name == "MoleNose" ? nose : skinParts).Add(r);
                }
                root.GetComponent<PlayerAvatarVisual>().Nose = nose.ToArray();
                rig.MoveState = "Walk";
                rig.FastState = "Run";
                rig.CrouchState = "Crouch";
                rig.FastSpeed = 5f;
                rig.ClipSpeedPerMetre = 0.4f;
                rig.Floppy = new[]
                {
                    // firm jelly: springs stiff and well damped (a little lag, no wobbling about)
                    // viscous: over-damped springs (a slow, thick lag, never a bounce)
                    new RigAnimator.FloppyChain { Bones = Bones(model, "Nose1", "Nose2", "Nose3"), Stiffness = 220f, Damping = 42f, Gravity = 0f },
                    new RigAnimator.FloppyChain { Bones = Bones(model, "ArmL1", "ArmL2"), Stiffness = 200f, Damping = 40f, Gravity = 0.3f },
                    new RigAnimator.FloppyChain { Bones = Bones(model, "ArmR1", "ArmR2"), Stiffness = 200f, Damping = 40f, Gravity = 0.3f },
                };
                // the nose gets a deep chain of bones so it can really stretch (ElecMoleNoseRig)
                DeepRigNose(root, model.transform);
            }
            Renderer Part(string n, PrimitiveType t, Vector3 p, Vector3 s, Material m, bool isSkin)
            {
                var r = Shape(body, n, t, p, Vector3.zero, s, m).GetComponent<Renderer>();
                (isSkin ? skinParts : details).Add(r);
                return r;
            }
            if (model == null) // primitive fallback when the rigged model is missing
            {
                Part("Torso", PrimitiveType.Sphere, new Vector3(0f, 0.68f, 0f), new Vector3(0.95f, 1.3f, 0.9f), skin, true);
                Part("Belly", PrimitiveType.Sphere, new Vector3(0f, 0.6f, 0.2f), new Vector3(0.7f, 0.85f, 0.6f), belly, false);
                Part("Snout", PrimitiveType.Sphere, new Vector3(0f, 1.0f, 0.4f), new Vector3(0.34f, 0.25f, 0.32f), pink, false);
                Part("Nose", PrimitiveType.Sphere, new Vector3(0f, 1.03f, 0.57f), Vector3.one * 0.1f, dark, false);
                foreach (var side in new[] { -1f, 1f })
                {
                    Part("Eye", PrimitiveType.Sphere, new Vector3(side * 0.15f, 1.17f, 0.36f), Vector3.one * 0.08f, dark, false);
                    Part("Ear", PrimitiveType.Sphere, new Vector3(side * 0.34f, 1.22f, 0f), new Vector3(0.16f, 0.12f, 0.08f), skin, true);
                    Part("Foot", PrimitiveType.Sphere, new Vector3(side * 0.22f, 0.07f, 0.15f), new Vector3(0.3f, 0.14f, 0.38f), skin, true);
                }
                Part("Antenna", PrimitiveType.Cylinder, new Vector3(0f, 1.45f, -0.05f), new Vector3(0.03f, 0.13f, 0.03f), dark, false);
                Part("AntennaTip", PrimitiveType.Sphere, new Vector3(0f, 1.6f, -0.05f), Vector3.one * 0.09f, glow, false);
            }

            Transform Paw(string n, float side)
            {
                var paw = Shape(pivot, n, PrimitiveType.Sphere, new Vector3(side * 0.3f, -0.28f, 0.45f), Vector3.zero, new Vector3(0.17f, 0.1f, 0.2f), skin).transform;
                for (var i = -1; i <= 1; i++)
                    Shape(paw, "Claw", PrimitiveType.Cube, new Vector3(i * 0.3f, -0.1f, 0.55f), Vector3.zero, new Vector3(0.15f, 0.2f, 0.4f), claw);
                return paw;
            }
            // First-person hands: the mole's own arm (cut out by tools/blender/make_arm.py), mirrored for the
            // left, coming up from the bottom corners of the view. Falls back to the old paws.
            Transform Arm(string n, float side)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterDir + "/MoleAlien/MoleArm.fbx");
                if (source == null) return Paw(n, side);
                var holder = new GameObject(n).transform;
                holder.SetParent(pivot, false);
                holder.localPosition = new Vector3(side * 0.3f, -0.33f, 0.16f);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.transform.SetParent(holder, false);
                var renderer = model.GetComponentInChildren<Renderer>();
                var filter = model.GetComponentInChildren<MeshFilter>();
                renderer.sharedMaterial = CharacterMaterial("MoleAlien", Color.white);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var scale = new Vector3(side < 0f ? -0.95f : 0.95f, 0.95f, 0.95f);
                model.transform.localScale = scale;
                // Turn the arm so it reaches forward and a little inward from the shoulder.
                var centre = model.transform.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.bounds.center));
                var reach = Vector3.Scale(centre, scale);
                model.transform.localRotation = Quaternion.FromToRotation(reach, new Vector3(-side * 0.35f, 0.05f, 1f).normalized);
                return holder;
            }
            var visual = root.GetComponent<PlayerAvatarVisual>();
            visual.BodyRoot = body;
            visual.Skin = skinParts.ToArray();
            visual.Details = details.ToArray();
            visual.AimPivot = pivot;
            visual.LeftFist = Arm("LeftFist", -1f);
            visual.RightFist = Arm("RightFist", 1f);
            visual.Body = null;
            visual.Head = null;
            root.GetComponent<FirstPersonController>().AimPivot = pivot;
        }
    }
}
