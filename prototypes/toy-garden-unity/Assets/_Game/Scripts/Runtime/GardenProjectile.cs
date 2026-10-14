using System.Collections.Generic;
using UnityEngine;

namespace ToyGarden
{
    public sealed class GardenProjectile : MonoBehaviour
    {
        static readonly List<GardenProjectile> Live = new List<GardenProjectile>();
        GardenActor owner;
        GardenGame game;
        Vector3 velocity;
        float damage, remainingDistance;
        bool enemyProjectile;
        HashSet<GardenActor> attackHits;

        public static void Spawn(GardenActor source, GardenGame context, Vector3 position,
            Vector3 direction, float amount, float speed, float range, Color color,
            HashSet<GardenActor> sharedAttackHits = null)
        {
            var bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bolt.name = source.IsEnemy ? "Spitter bolt" : "Ranger arrow";
            bolt.transform.position = position;
            bolt.transform.localScale = source.IsEnemy ? Vector3.one * .33f : new Vector3(.15f, .15f, .48f);
            bolt.transform.rotation = Quaternion.LookRotation(direction);
            var collider = bolt.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            var renderer = bolt.GetComponent<Renderer>();
            // The scene references this asset so its shader is retained in player builds.
            renderer.sharedMaterial = context.projectileMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var projectile = bolt.AddComponent<GardenProjectile>();
            projectile.owner = source;
            projectile.game = context;
            projectile.velocity = direction.normalized * speed;
            projectile.damage = amount;
            projectile.remainingDistance = range;
            projectile.enemyProjectile = source.IsEnemy;
            projectile.attackHits = sharedAttackHits ?? new HashSet<GardenActor>();
            Live.Add(projectile);
        }

        void Update()
        {
            if (game == null) { Destroy(gameObject); return; }
            if (!game.IsPlaying) return;
            Vector3 from = transform.position;
            Vector3 step = velocity * Time.deltaTime;
            float length = Mathf.Min(step.magnitude, remainingDistance);
            Vector3 direction = velocity.normalized;
            Vector3 to = from + direction * length;
            float nearest = length + 1f;
            GardenActor hitActor = null;
            var candidates = enemyProjectile ? game.Heroes : game.Enemies;
            foreach (GardenActor target in candidates)
            {
                if (target == null || !target.IsAlive || attackHits.Contains(target)) continue;
                if (target.IsEnemy && !game.IsEncounterActive(target.encounterId)) continue;
                Vector3 center = target.transform.position + Vector3.up * .85f;
                Vector3 closest = from + direction * Mathf.Clamp(Vector3.Dot(center - from, direction), 0f, length);
                float radius = target.kind == ActorKind.Boss ? .95f : .6f;
                if ((closest - center).sqrMagnitude > radius * radius) continue;
                float hitDistance = (closest - from).magnitude;
                if (hitDistance < nearest) { nearest = hitDistance; hitActor = target; }
            }
            // Sweep against scenery as well: arrows cannot hit through walls or raised platforms.
            var worldHits = Physics.SphereCastAll(from, .1f, direction, length, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in worldHits)
            {
                if (hit.collider.GetComponentInParent<GardenActor>() != null) continue;
                if (hit.distance < nearest) { nearest = hit.distance; hitActor = null; }
            }
            if (nearest <= length)
            {
                if (hitActor != null)
                {
                    attackHits.Add(hitActor);
                    hitActor.TakeDamage(damage, owner != null ? owner.transform.position : from);
                }
                Destroy(gameObject);
                return;
            }
            transform.position = to;
            remainingDistance -= length;
            if (remainingDistance <= .001f) Destroy(gameObject);
        }

        public static void ClearFor(GardenActor source)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                GardenProjectile projectile = Live[i];
                if (projectile == null) { Live.RemoveAt(i); continue; }
                if (projectile.owner == source)
                {
                    projectile.enabled = false;
                    Destroy(projectile.gameObject);
                }
            }
        }

        void OnDestroy() { Live.Remove(this); }
    }
}
