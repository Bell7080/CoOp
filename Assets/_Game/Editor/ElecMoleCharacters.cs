using System.Collections.Generic;
using System.Linq;
using CoOpHorror.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CoOpHorror.Editor
{
    // Rigged characters made with Tripo (paid credits, commercial use) and rigged/animated in Blender by
    // GameAssets/Tripo/scripts/rig_animate.py: the mole alien player and two monsters. Each FBX carries its
    // clips as takes; this sets the importer up (generic rig, looping clips, no imported materials), builds an
    // Animator Controller with one state per clip and spawns the model with a point-filtered PS1 material.
    public static partial class ElecMolePrototypeSetup
    {
        private const string CharacterDir = "Assets/_Game/Art/Characters";

        private static string CharacterFbx(string name) => $"{CharacterDir}/{name}/{name}.fbx";

        private static void PrepareCharacter(string name, params string[] oneShots)
        {
            var path = CharacterFbx(name);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;
            var changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
            // Always rebuild the clip list from the file's takes (a re-export can change clip lengths).
            var clips = importer.defaultClipAnimations.ToArray();
            var old = importer.clipAnimations;
            if (old.Length != clips.Length) changed = true;
            else for (var i = 0; i < old.Length; i++)
                if (!Mathf.Approximately(old[i].lastFrame, clips[i].lastFrame) || !Mathf.Approximately(old[i].firstFrame, clips[i].firstFrame)) changed = true;
            foreach (var clip in clips)
            {
                var shortName = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
                var loop = !oneShots.Contains(shortName);
                if (clip.name != shortName || clip.loopTime != loop || !clip.lockRootRotation)
                {
                    clip.name = shortName;
                    clip.loopTime = loop;
                    clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                    changed = true;
                }
            }
            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            var texPath = $"{CharacterDir}/{name}/{name}_Albedo.png";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter tex &&
                (tex.filterMode != FilterMode.Point || tex.maxTextureSize != 256 || tex.mipmapEnabled))
            {
                tex.filterMode = FilterMode.Point;
                tex.maxTextureSize = 256;
                tex.mipmapEnabled = false;
                tex.textureCompression = TextureImporterCompression.Uncompressed;
                tex.SaveAndReimport();
            }
        }

        private static AnimationClip[] CharacterClips(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(CharacterFbx(name)).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();

        private static RuntimeAnimatorController CharacterController(string name)
        {
            var path = $"{CharacterDir}/{name}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            AnimatorState idle = null;
            foreach (var clip in CharacterClips(name))
            {
                var state = machine.AddState(clip.name);
                state.motion = clip;
                if (clip.name == "Idle") idle = state;
            }
            if (idle != null) machine.defaultState = idle;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Material CharacterMaterial(string name, Color tint)
        {
            var material = Mat("Char_" + name, tint, tex: AssetDatabase.LoadAssetAtPath<Texture2D>($"{CharacterDir}/{name}/{name}_Albedo.png"));
            material.SetFloat("_Smoothness", 0.12f);
            return material;
        }

        // Instantiates the rigged model (feet at the parent's origin, facing +Z) with its animator.
        private static GameObject SpawnCharacter(Transform parent, string name, Color tint, Transform motionRoot, out RigAnimator rig)
        {
            rig = null;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFbx(name));
            if (source == null)
            {
                Debug.LogWarning($"[ElecMole] Missing character model {name}");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            var material = CharacterMaterial(name, tint);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = CharacterController(name);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            rig = instance.AddComponent<RigAnimator>();
            rig.Animator = animator;
            rig.MotionRoot = motionRoot;
            return instance;
        }

        private static Transform[] Bones(GameObject model, params string[] names)
        {
            var all = model.GetComponentsInChildren<Transform>(true);
            return names.Select(n => all.FirstOrDefault(t => t.name == n)).Where(t => t != null).ToArray();
        }

        private static Material BlushMaterial()
        {
            const string texPath = CharacterDir + "/MoleAlien/Blush.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Blush", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var d = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 10f).magnitude; // a bit wider than tall
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * 0.85f));
                }
                tex.Apply();
                AssetDatabase.CreateAsset(tex, texPath);
            }
            var path = MaterialDir + "/MoleBlush.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetTexture("_BaseMap", tex);
            material.SetColor("_BaseColor", new Color(1f, 0.45f, 0.55f, 0.75f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void PrepareCharacters()
        {
            PrepareCharacter("MoleAlien");
            PrepareCharacter("TvWalker");
            PrepareCharacter("FanCrawler");
            PrepareCharacter("ClockSpider");
        }
    }
}
