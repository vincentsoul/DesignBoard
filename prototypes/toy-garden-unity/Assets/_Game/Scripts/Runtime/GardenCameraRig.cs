using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace ToyGarden
{
    public sealed class GardenCameraRig : MonoBehaviour
    {
        Transform target;
        CinemachineFollow follow;
        GardenGame game;
        Vector3 velocity;
        float distance = 18;

        void Start()
        {
            game = GetComponent<GardenGame>();
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                camera = cameraObject.GetComponent<Camera>();
            }
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 180;
            if (!camera.TryGetComponent<CinemachineBrain>(out _)) camera.gameObject.AddComponent<CinemachineBrain>();
            target = new GameObject("Party Camera Target").transform;
            target.SetParent(transform);
            target.position = game.ActiveHero != null ? game.ActiveHero.transform.position + Vector3.up : Vector3.zero;
            var rig = new GameObject("Party Cinemachine Camera");
            rig.transform.SetParent(transform);
            rig.transform.rotation = Quaternion.Euler(52, 0, 0);
            var cm = rig.AddComponent<CinemachineCamera>();
            cm.Follow = target;
            cm.Lens.FieldOfView = 48;
            cm.Lens.NearClipPlane = .1f;
            cm.Lens.FarClipPlane = 180;
            follow = rig.AddComponent<CinemachineFollow>();
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = Vector3.zero;
            follow.FollowOffset = Offset(distance);
        }

        void LateUpdate()
        {
            if (game == null || target == null || game.ActiveHero == null) return;
            var desired = game.ActiveHero.transform.position + Vector3.up * .9f;
            target.position = Vector3.SmoothDamp(target.position, desired, ref velocity, .17f, Mathf.Infinity, Time.unscaledDeltaTime);
            float wanted = game.IsCombatActive ? 25 : 18;
            distance = Mathf.Lerp(distance, wanted, 1 - Mathf.Exp(-3 * Time.unscaledDeltaTime));
            follow.FollowOffset = Offset(distance);
        }

        static Vector3 Offset(float d) => Quaternion.Euler(52, 0, 0) * (Vector3.back * d);
    }
}
