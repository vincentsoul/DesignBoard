using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToyGarden
{
    /// <summary>An isolated visual clone; no gameplay actors or colliders run in the portrait stage.</summary>
    public sealed class GardenCharacterPreview : IDisposable
    {
        const int PreviewLayer = 30;
        readonly GameObject stage;
        readonly Camera camera;
        readonly RenderTexture texture;
        readonly Material pedestalMaterial;
        GameObject model;
        Camera worldCamera;
        int previousMask;
        float yaw=12;
        public RenderTexture Texture => texture;

        public GardenCharacterPreview()
        {
            stage=new GameObject("Equipment portrait stage") { hideFlags=HideFlags.HideAndDontSave, layer=PreviewLayer };
            stage.transform.position=new Vector3(10000,10000,10000);
            var cameraObject=new GameObject("Equipment portrait camera") { hideFlags=HideFlags.HideAndDontSave, layer=PreviewLayer };
            cameraObject.transform.SetParent(stage.transform,false);
            camera=cameraObject.AddComponent<Camera>(); camera.cullingMask=1<<PreviewLayer;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.115f,.105f,.16f,1);
            if(QualitySettings.activeColorSpace==ColorSpace.Linear) camera.backgroundColor=camera.backgroundColor.linear;
            camera.nearClipPlane=.1f; camera.farClipPlane=12; camera.fieldOfView=29;
            cameraObject.transform.localPosition=new Vector3(0,1.45f,4.7f);
            cameraObject.transform.LookAt(stage.transform.position+Vector3.up*.95f);
            texture=new RenderTexture(640,760,24,RenderTextureFormat.ARGB32) { name="Equipment character portrait", hideFlags=HideFlags.HideAndDontSave, antiAliasing=2 };
            texture.Create(); camera.targetTexture=texture; camera.depth=50;
            LightForPortrait("Portrait key light",new Vector3(-2,3,3),new Color(.85f,.85f,1),2.5f);
            LightForPortrait("Portrait gold rim",new Vector3(2,2,-1),new Color(1,.7f,.4f),2);
            GameObject pedestal=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name="Equipment portrait pedestal"; pedestal.hideFlags=HideFlags.HideAndDontSave; pedestal.layer=PreviewLayer;
            pedestal.transform.SetParent(stage.transform,false); pedestal.transform.localPosition=Vector3.down*.12f; pedestal.transform.localScale=new Vector3(1.65f,.1f,1.65f);
            UnityEngine.Object.Destroy(pedestal.GetComponent<Collider>());
            pedestalMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags=HideFlags.HideAndDontSave };
            pedestalMaterial.color=new Color(.25f,.22f,.35f); pedestal.GetComponent<Renderer>().sharedMaterial=pedestalMaterial;
            worldCamera=Camera.main;
            if(worldCamera!=null) { previousMask=worldCamera.cullingMask; worldCamera.cullingMask&=~(1<<PreviewLayer); }
        }

        void LightForPortrait(string name,Vector3 position,Color color,float intensity)
        {
            var go=new GameObject(name) { hideFlags=HideFlags.HideAndDontSave, layer=PreviewLayer };
            go.transform.SetParent(stage.transform,false); go.transform.localPosition=position;
            Light light=go.AddComponent<Light>(); light.type=LightType.Point; light.range=7; light.color=color; light.intensity=intensity; light.cullingMask=1<<PreviewLayer; light.shadows=LightShadows.None;
        }

        public void Show(GardenActor actor,Color equipmentTint)
        {
            if(model!=null) UnityEngine.Object.Destroy(model);
            if(actor==null || actor.visualRoot==null) return;
            model=UnityEngine.Object.Instantiate(actor.visualRoot.gameObject,stage.transform,false);
            model.name="Equipment visual clone"; model.transform.localPosition=Vector3.zero;
            model.transform.localRotation=Quaternion.Euler(0,yaw,0); model.transform.localScale=Vector3.one;
            foreach(Transform child in model.GetComponentsInChildren<Transform>(true))
            { child.gameObject.layer=PreviewLayer; child.gameObject.hideFlags=HideFlags.HideAndDontSave; }
            foreach(Collider collider in model.GetComponentsInChildren<Collider>(true)) { collider.enabled=false; UnityEngine.Object.Destroy(collider); }
            foreach(MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) { behaviour.enabled=false; UnityEngine.Object.Destroy(behaviour); }
            foreach(AudioSource source in model.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.Destroy(source);
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                if(renderer.name.Contains("tunic") || renderer.name.Contains("blade") || renderer.name.Contains("mallet"))
                {
                    var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                    Color original=renderer.sharedMaterial!=null?renderer.sharedMaterial.color:Color.white;
                    block.SetColor("_BaseColor",Color.Lerp(original,equipmentTint,.24f)); renderer.SetPropertyBlock(block);
                }
            }
        }

        public void Tick(float drag)
        {
            yaw+=drag;
            if(model!=null) model.transform.localRotation=Quaternion.Euler(0,yaw,0);
        }

        public void Dispose()
        {
            if(worldCamera!=null && worldCamera.cullingMask==(previousMask&~(1<<PreviewLayer))) worldCamera.cullingMask=previousMask;
            if(camera!=null) camera.targetTexture=null;
            if(texture!=null) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            if(pedestalMaterial!=null) UnityEngine.Object.Destroy(pedestalMaterial);
            if(stage!=null) UnityEngine.Object.Destroy(stage);
        }
    }
}
