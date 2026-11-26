using System.Linq;
using CoOpHorror.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoOpHorror.Editor
{
    public static partial class ElecMolePrototypeSetup
    {
        private static void DressOpenFurniture(Transform group, Vector3 bottom, Vector3 size)
        {
            var model=Group(group,"Model_OpenFurniture",bottom,0f);
            var top=Mat("OpenFurnitureTop",new Color(0.39f,0.35f,0.29f),tex:Texture("Wood"));
            var steel=Mat("OpenFurnitureFrame",new Color(0.22f,0.25f,0.24f),tex:Texture("Metal"));
            var height=Mathf.Max(0.8f,size.y);
            var depth=Mathf.Max(0.8f,size.z);
            Box(model,"Top",new Vector3(0,height-0.035f,0),new Vector3(size.x,0.07f,depth),top);
            foreach(var x in new[]{-size.x/2f+0.10f,size.x/2f-0.10f})
            foreach(var z in new[]{-depth/2f+0.10f,depth/2f-0.10f})
            {
                Box(model,"Leg",new Vector3(x,(height-0.07f)/2f,z),new Vector3(0.07f,height-0.07f,0.07f),steel);
                Box(model,"Foot",new Vector3(x,0.015f,z),new Vector3(0.11f,0.03f,0.11f),steel);
            }
            // Braces belong immediately under the top; no low crossbar blocks the crawl route.
            foreach(var z in new[]{-depth/2f+0.10f,depth/2f-0.10f})
                Box(model,"UpperBrace",new Vector3(0,height-0.13f,z),new Vector3(size.x-0.13f,0.07f,0.06f),steel);
        }

        // Keep the interactable collider and indicator references; only replace their presentation.
        private static void DressDoorButton(Transform button)
        {
            // Hull faces sit at +/- 0.15 m; a 0.10 m housing rests against them.
            var mounting=button.localPosition;
            mounting.x=Mathf.Sign(mounting.x)*0.205f;
            button.localPosition=mounting;
            var scale = button.localScale;
            var hit = button.GetComponent<BoxCollider>();
            if (hit != null) { hit.size = Vector3.Scale(hit.size, scale); hit.center = Vector3.Scale(hit.center, scale); }
            button.localScale = Vector3.one;
            var renderer = button.GetComponent<MeshRenderer>();
            var filter = button.GetComponent<MeshFilter>();
            if (renderer != null) Object.DestroyImmediate(renderer);
            if (filter != null) Object.DestroyImmediate(filter);
            var previous = button.Find("ButtonHousing");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var root = Group(button, "ButtonHousing", Vector3.zero, 0f);
            var side = Mathf.Sign(button.localPosition.x);
            var steel = Mat("DoorButtonSteel", new Color(0.19f,0.21f,0.20f), tex:Texture("Metal"));
            var dark = Mat("DoorButtonInset", new Color(0.045f,0.052f,0.048f));
            Shape(root,"Backplate",PrimitiveType.Cube,Vector3.zero,Vector3.zero,new Vector3(0.10f,0.42f,0.30f),steel);
            Shape(root,"Inset",PrimitiveType.Cube,new Vector3(side*0.055f,0.01f,0),Vector3.zero,new Vector3(0.018f,0.27f,0.23f),dark);
            var lamp = button.Find("Lamp");
            if (lamp != null)
            {
                // A real circular cap stands proud of the inset; no flat neon rectangle.
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lamp.GetComponent<MeshFilter>().sharedMesh=cap.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(cap);
                lamp.localPosition=new Vector3(side*0.075f,0.025f,0);
                lamp.localRotation=Quaternion.Euler(0,0,90);
                lamp.localScale=new Vector3(0.16f,0.018f,0.16f);
            }
            foreach(var y in new[]{-0.17f,0.17f})
            foreach(var z in new[]{-0.10f,0.10f})
                Shape(root,"Fastener",PrimitiveType.Cylinder,new Vector3(side*0.057f,y,z),new Vector3(0,0,90),new Vector3(0.024f,0.006f,0.024f),dark);
        }

        private static int FitFurnitureCollision(Transform root)
        {
            var models=root.Cast<Transform>().Where(t=>t.name.StartsWith("Model_")).ToArray();
            if(models.Length==0)return 0;
            // Static, concave meshes preserve the space between legs. A single convex hull would fill it.
            foreach(var box in root.GetComponents<BoxCollider>()) Object.DestroyImmediate(box);
            foreach(var child in root.Cast<Transform>().ToArray())
            {
                if(child.name.StartsWith("Model_"))continue;
                if(root.GetComponent<CctvPanel>()!=null && child.name!="Desk")continue;
                foreach(var collider in child.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            }
            var count=0;
            foreach(var model in models)
            foreach(var mesh in model.GetComponentsInChildren<MeshFilter>())
            {
                if(mesh.sharedMesh==null)continue;
                if(mesh.GetComponent<BoxCollider>()!=null){count++;continue;}
                var collider=mesh.GetComponent<MeshCollider>();
                if(collider==null)collider=mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh=mesh.sharedMesh; collider.convex=false; count++;
            }
            return count;
        }

        [MenuItem("CoOp/Setup/Polish world controls and furniture")]
        public static int RefreshWorldPolish()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Stop Play Mode first.");
            var count=0;
            foreach(var door in Object.FindObjectsByType<PoweredDoor>(FindObjectsSortMode.None))
                foreach(var button in door.Buttons) if(button!=null)DressDoorButton(button.transform);
            var map=GameObject.Find(Prefix+"Map");
            if(map!=null)
                foreach(var t in map.GetComponentsInChildren<Transform>().Where(t=>t.name=="Desk"||t.name=="Table"||t.name=="Bench").ToArray())
                    {
                        var old=t.Find("Model_table")??t.Find("Model_bench");
                        if(old!=null)
                        {
                            var bounds=Bounds(old.gameObject);
                            var local=new Bounds(t.InverseTransformPoint(bounds.center),Vector3.zero);
                            for(var corner=0;corner<8;corner++)local.Encapsulate(t.InverseTransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1))));
                            Object.DestroyImmediate(old.gameObject);
                            DressOpenFurniture(t,new Vector3(local.center.x,local.min.y,local.center.z),local.size);
                        }
                        count+=FitFurnitureCollision(t);
                    }
            foreach(var panel in Object.FindObjectsByType<CctvPanel>(FindObjectsSortMode.None))count+=FitFurnitureCollision(panel.transform);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            return count;
        }
    }
}
