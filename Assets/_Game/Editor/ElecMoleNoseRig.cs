using System.Collections.Generic;
using CoOpHorror.Player;
using UnityEditor;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // The mole's segmented nose comes from Blender (tools/blender/rig_mole_firm.py): the nose mesh is skinned
    // to a dense chain of NoseSeg01..NN bones (children of the original Nose1/2/3 bones that the clips sway).
    // Here they are handed to MoleRig, which lays them along a curve to stretch the real nose at runtime, and
    // the owner's first-person arms are cut out of the body mesh.
    public static partial class ElecMolePrototypeSetup
    {
        private static void DeepRigNose(GameObject root, Transform model)
        {
            var segments = new List<Transform>();
            Transform Find(string n)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
                return null;
            }
            for (var k = 1; ; k++)
            {
                var seg = Find("NoseSeg" + k.ToString("00"));
                if (seg == null) break;
                segments.Add(seg);
            }
            var rig = root.GetComponent<MoleRig>() ?? root.AddComponent<MoleRig>();
            if (segments.Count < 2)
            {
                Debug.LogWarning("[ElecMole] MoleAlien.fbx has no NoseSeg bones: run tools/blender/rig_mole_firm.py");
                rig.NoseSegments = new Transform[0];
            }
            else
            {
                rig.NoseSegments = segments.ToArray();
                var last = segments[segments.Count - 1];
                rig.NoseTip = last.childCount > 0 ? last.GetChild(0) : last;
            }
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == "MoleNose") r.updateWhenOffscreen = true; // a stretched nose leaves the body's bounds
            rig.ArmR1 = Find("ArmR1");
            rig.ArmR2 = Find("ArmR2");
            rig.ArmL1 = Find("ArmL1");
            rig.ArmL2 = Find("ArmL2");
            BuildFirstPersonArms(root, model);
        }

        private const string ArmsMeshPath = "Assets/_Game/Art/Characters/MoleAlien/MoleArmsFirstPerson.asset";

        // First person: the camera sits inside the head and the whole body cannot be drawn for its owner (the
        // scanned mesh shows its inside there). So the owner sees the real arms cut out of the real body mesh:
        // only the triangles skinned to the arm bones, on the same skeleton, so they move exactly as the body's
        // arms do (clips, springs, MoleRig reaching).
        private static void BuildFirstPersonArms(GameObject root, Transform model)
        {
            SkinnedMeshRenderer body = null;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == "MoleAlien") body = r;
            if (body == null || body.sharedMesh == null) return;
            var source = body.sharedMesh;
            var armBones = new HashSet<int>();
            for (var i = 0; i < body.bones.Length; i++)
                if (body.bones[i] != null && body.bones[i].name.StartsWith("Arm")) armBones.Add(i);
            var weights = source.boneWeights;
            bool OnArm(int v)
            {
                var w = weights[v];
                var arm = (armBones.Contains(w.boneIndex0) ? w.weight0 : 0f) + (armBones.Contains(w.boneIndex1) ? w.weight1 : 0f)
                    + (armBones.Contains(w.boneIndex2) ? w.weight2 : 0f) + (armBones.Contains(w.boneIndex3) ? w.weight3 : 0f);
                return arm > 0.5f;
            }
            var mesh = Object.Instantiate(source);
            mesh.name = "MoleArmsFirstPerson";
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = source.GetTriangles(sub);
                var kept = new List<int>();
                for (var t = 0; t < tris.Length; t += 3)
                    if (OnArm(tris[t]) && OnArm(tris[t + 1]) && OnArm(tris[t + 2])) { kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]); }
                mesh.SetTriangles(kept, sub);
            }
            if (AssetDatabase.LoadAssetAtPath<Mesh>(ArmsMeshPath) != null) AssetDatabase.DeleteAsset(ArmsMeshPath);
            AssetDatabase.CreateAsset(mesh, ArmsMeshPath);
            var go = new GameObject("MoleArmsFirstPerson");
            go.transform.SetParent(body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            go.transform.localScale = body.transform.localScale;
            var arms = go.AddComponent<SkinnedMeshRenderer>();
            arms.sharedMesh = mesh;
            arms.bones = body.bones;
            arms.rootBone = body.rootBone;
            arms.sharedMaterials = body.sharedMaterials;
            arms.updateWhenOffscreen = true;
            arms.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arms.enabled = false; // PlayerAvatarVisual turns it on for the owner only
            root.GetComponent<PlayerAvatarVisual>().FirstPersonArms = arms;
        }
    }
}
