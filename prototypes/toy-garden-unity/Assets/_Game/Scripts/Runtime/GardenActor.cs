using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ToyGarden
{
    public enum ActorKind { Blade, Hammer, Ranger, Grunt, Spitter, Boss }

    [RequireComponent(typeof(CharacterController))]
    public sealed class GardenActor : MonoBehaviour
    {
        public ActorKind kind;
        public int encounterId;
        public Transform visualRoot, weaponRoot;
        public Renderer bodyRenderer;

        public bool IsEnemy => kind >= ActorKind.Grunt;
        public bool IsAlive => Health > 0f;
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public float SkillCooldownRemaining => Mathf.Max(0f, skillCooldown);
        public float DodgeCooldownRemaining => Mathf.Max(0f, dodgeCooldown);
        public bool IsDodging => IsAlive && dodgeTimer > 0f;
        public Vector3 HomePosition { get; private set; }
        public bool IsPlayerControlled
        {
            get => playerControlled;
            set
            {
                if (playerControlled == value) return;
                playerControlled = value;
                moveInput = Vector3.zero;
                ClearPath();
                // Attacks, life and cooldowns belong to the actor, not its controller.
            }
        }

        GardenGame game;
        CharacterController controller;
        bool initialized, playerControlled;
        Vector3 moveInput, dodgeDirection, knockback;
        Vector3 visualRestPosition, weaponRestPosition;
        Quaternion visualRestRotation, weaponRestRotation, homeRotation;
        Color bodyColor = Color.white;
        MaterialPropertyBlock bodyBlock;
        float speed, attackDamage, attackRange, attackInterval, skillInterval;
        float verticalSpeed, skillCooldown, dodgeCooldown, attackCooldown;
        float dodgeTimer, dodgeRecovery, stunTimer, flashTimer;
        float attackElapsed, attackLength, impactTime, currentRange;
        bool attacking, impactDone, usingSkill;
        Vector3 attackDirection;
        int attackCount;
        LineRenderer telegraph;
        NavMeshPath navigationPath;
        Vector3[] corners;
        int cornerIndex;
        float repathTimer, stuckTimer, animationPhase;
        Vector3 pathTarget, previousPosition;

        public void Initialize(GardenGame owner)
        {
            game = owner;
            controller = GetComponent<CharacterController>();
            controller.height = kind == ActorKind.Boss ? 2.3f : 1.65f;
            controller.radius = kind == ActorKind.Boss ? .65f : .34f;
            controller.center = new Vector3(0f, controller.height * .5f, 0f);
            controller.stepOffset = .32f;
            controller.slopeLimit = 48f;
            controller.skinWidth = .04f;
            controller.minMoveDistance = 0f;
            HomePosition = transform.position;
            homeRotation = transform.rotation;
            previousPosition = HomePosition;
            animationPhase = Random.Range(0f, Mathf.PI * 2f);
            navigationPath = new NavMeshPath();
            bodyBlock = new MaterialPropertyBlock();
            if (visualRoot != null && visualRoot != transform)
            {
                visualRestPosition = visualRoot.localPosition;
                visualRestRotation = visualRoot.localRotation;
            }
            if (weaponRoot != null)
            {
                weaponRestPosition = weaponRoot.localPosition;
                weaponRestRotation = weaponRoot.localRotation;
            }
            if (bodyRenderer != null && bodyRenderer.sharedMaterial != null)
            {
                var material = bodyRenderer.sharedMaterial;
                if (material.HasProperty("_BaseColor")) bodyColor = material.GetColor("_BaseColor");
                else if (material.HasProperty("_Color")) bodyColor = material.GetColor("_Color");
            }
            ConfigureStats();
            Health = MaxHealth;
            initialized = true;
            // A squad must not block itself at bridges, doorways or a character switch.
            if (game != null && !IsEnemy)
                foreach (var hero in game.Heroes)
                    if (hero != null && hero != this)
                    {
                        var other = hero.GetComponent<CharacterController>();
                        if (other != null) Physics.IgnoreCollision(controller, other, true);
                    }
        }

        void ConfigureStats()
        {
            switch (kind)
            {
                case ActorKind.Blade:
                    MaxHealth = 130f; speed = 5.6f; attackDamage = 24f;
                    attackRange = 2.1f; attackInterval = .43f; skillInterval = 5f; break;
                case ActorKind.Hammer:
                    MaxHealth = 175f; speed = 4.8f; attackDamage = 40f;
                    attackRange = 2.3f; attackInterval = .75f; skillInterval = 7f; break;
                case ActorKind.Ranger:
                    MaxHealth = 105f; speed = 5.9f; attackDamage = 20f;
                    attackRange = 10f; attackInterval = .48f; skillInterval = 6f; break;
                case ActorKind.Grunt:
                    MaxHealth = 85f; speed = 3.1f; attackDamage = 14f;
                    attackRange = 1.95f; attackInterval = 1.8f; skillInterval = 5f; break;
                case ActorKind.Spitter:
                    MaxHealth = 65f; speed = 2.7f; attackDamage = 13f;
                    attackRange = 8f; attackInterval = 2.3f; skillInterval = 5f; break;
                default:
                    MaxHealth = 560f; speed = 2.5f; attackDamage = 25f;
                    attackRange = 2.9f; attackInterval = 2.1f; skillInterval = 5f; break;
            }
        }

        public void SetMoveInput(Vector3 direction)
        {
            direction.y = 0f;
            moveInput = Vector3.ClampMagnitude(direction, 1f);
        }

        void Update()
        {
            if (!initialized || game == null) return;
            float dt = Time.deltaTime;
            if (!game.IsPlaying)
            {
                moveInput = Vector3.zero;
                return;
            }
            flashTimer = Mathf.Max(0f, flashTimer - dt);
            if (!IsAlive)
            {
                UpdateVisual(Vector3.zero, dt);
                return;
            }
            skillCooldown = Mathf.Max(0f, skillCooldown - dt);
            dodgeCooldown = Mathf.Max(0f, dodgeCooldown - dt);
            attackCooldown = Mathf.Max(0f, attackCooldown - dt);
            dodgeTimer = Mathf.Max(0f, dodgeTimer - dt);
            dodgeRecovery = Mathf.Max(0f, dodgeRecovery - dt);
            stunTimer = Mathf.Max(0f, stunTimer - dt);
            repathTimer -= dt;

            Vector3 desired = playerControlled ? moveInput * speed : ThinkAI();
            if (attacking)
            {
                attackElapsed += dt;
                desired *= IsEnemy ? 0f : .16f;
                if (!impactDone && attackElapsed >= impactTime)
                {
                    impactDone = true;
                    ResolveAttack();
                    HideTelegraph();
                }
                if (attackElapsed >= attackLength) StopAttack();
            }
            if (stunTimer > 0f) desired = Vector3.zero;
            if (dodgeTimer > 0f) desired = dodgeDirection * (kind == ActorKind.Hammer ? 11f : 13f);
            else if (dodgeRecovery > 0f) desired *= .35f;
            desired += knockback;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, dt * 22f);
            MoveCharacter(desired, dt);
            if (!attacking && stunTimer <= 0f && desired.sqrMagnitude > .04f)
            {
                Vector3 facing = dodgeTimer > 0f ? dodgeDirection : desired;
                facing.y = 0f;
                if (facing.sqrMagnitude > .001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(facing), 1f - Mathf.Exp(-18f * dt));
            }
            UpdateVisual(desired, dt);
            UpdateRegroup(desired, dt);
        }

        void MoveCharacter(Vector3 velocity, float dt)
        {
            if (!controller.enabled) return;
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed - 24f * dt, -30f);
            velocity.y = verticalSpeed;
            // This is the sole regular position writer: input, AI, rolls and knockback converge here.
            CollisionFlags flags = controller.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            if (transform.position.y < HomePosition.y - 12f)
            {
                Vector3 rescue = !IsEnemy && game.ActiveHero != null && game.ActiveHero != this
                    ? game.ActiveHero.transform.position - game.ActiveHero.transform.forward * 2f
                    : HomePosition;
                if (NavMesh.SamplePosition(rescue, out var hit, 3f, NavMesh.AllAreas)) rescue = hit.position;
                Teleport(rescue);
            }
        }

        Vector3 ThinkAI()
        {
            if (attacking || dodgeRecovery > 0f || stunTimer > 0f) return Vector3.zero;
            if (IsEnemy)
            {
                if (!game.IsEncounterActive(encounterId)) return Vector3.zero;
                if (HorizontalDistance(transform.position, HomePosition) > 15f)
                    return Navigate(HomePosition, speed);
                GardenActor target = FindNearest(game.Heroes, 16f, false);
                if (target == null) return Navigate(HomePosition, speed);
                float distance = HorizontalDistance(transform.position, target.transform.position);
                if (distance <= attackRange * (kind == ActorKind.Spitter ? .92f : .85f))
                {
                    Face(target.transform.position);
                    TryAttack(kind == ActorKind.Boss && attackCount > 0 && attackCount % 3 == 0);
                    return Vector3.zero;
                }
                return Navigate(target.transform.position, speed);
            }

            GardenActor leader = game.ActiveHero;
            if (leader == null || leader == this || !leader.IsAlive) return Vector3.zero;
            float leaderDistance = HorizontalDistance(transform.position, leader.transform.position);
            GardenActor enemy = leaderDistance < 9f ? FindCompanionTarget(leader) : null;
            if (enemy != null)
            {
                float distance = HorizontalDistance(transform.position, enemy.transform.position);
                if (enemy.attacking && !enemy.impactDone && enemy.kind != ActorKind.Spitter
                    && enemy.impactTime - enemy.attackElapsed < .3f && distance < enemy.currentRange + .4f)
                {
                    if (TryDodge(transform.position - enemy.transform.position)) return Vector3.zero;
                }
                if (distance <= attackRange * .88f)
                {
                    Face(enemy.transform.position);
                    TryAttack(skillCooldown <= 0f);
                    return Vector3.zero;
                }
                return Navigate(enemy.transform.position, speed);
            }
            int formationSlot = 0;
            foreach (GardenActor hero in game.Heroes)
            {
                if (hero == this) break;
                if (hero != null && hero != leader && hero.IsAlive) formationSlot++;
            }
            Vector3 offset = leader.transform.right * (formationSlot == 0 ? -1.5f : 1.5f)
                - leader.transform.forward * 1.9f;
            Vector3 followPosition = leader.transform.position + offset;
            if (HorizontalDistance(transform.position, followPosition) < .8f) return Vector3.zero;
            return Navigate(followPosition, speed * (leaderDistance > 5f ? 1.3f : 1f));
        }

        GardenActor FindCompanionTarget(GardenActor leader)
        {
            GardenActor result = null;
            float nearest = float.PositiveInfinity;
            foreach (GardenActor enemy in game.Enemies)
            {
                if (enemy == null || !enemy.IsAlive || !game.IsEncounterActive(enemy.encounterId)) continue;
                if (HorizontalDistance(leader.transform.position, enemy.transform.position) > 8f) continue;
                float distance = HorizontalDistance(transform.position, enemy.transform.position);
                if (distance < nearest) { nearest = distance; result = enemy; }
            }
            return result;
        }

        GardenActor FindNearest(List<GardenActor> candidates, float range, bool forwardOnly)
        {
            GardenActor result = null;
            float nearest = range;
            foreach (GardenActor candidate in candidates)
            {
                if (candidate == null || !candidate.IsAlive) continue;
                if (candidate.IsEnemy && !game.IsEncounterActive(candidate.encounterId)) continue;
                Vector3 difference = candidate.transform.position - transform.position;
                if (Mathf.Abs(difference.y) > 2.5f) continue;
                difference.y = 0f;
                float distance = difference.magnitude;
                if (distance > nearest) continue;
                if (forwardOnly && distance > .1f && Vector3.Dot(transform.forward, difference / distance) < .15f) continue;
                nearest = distance;
                result = candidate;
            }
            return result;
        }

        Vector3 Navigate(Vector3 destination, float movementSpeed)
        {
            if (repathTimer <= 0f || (destination - pathTarget).sqrMagnitude > 2.25f)
            {
                repathTimer = .25f + Mathf.Repeat(animationPhase, .1f);
                pathTarget = destination;
                corners = null;
                cornerIndex = 0;
                if (NavMesh.SamplePosition(transform.position, out var start, 1.5f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(destination, out var goal, 2f, NavMesh.AllAreas)
                    && Mathf.Abs(start.position.y - transform.position.y) < 1f
                    && Mathf.Abs(goal.position.y - destination.y) < 1f
                    && NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, navigationPath)
                    && navigationPath.status == NavMeshPathStatus.PathComplete)
                {
                    corners = navigationPath.corners;
                    cornerIndex = corners.Length > 1 ? 1 : 0;
                }
            }
            if (corners == null || corners.Length == 0) return Vector3.zero;
            while (cornerIndex < corners.Length && HorizontalDistance(transform.position, corners[cornerIndex]) < .3f)
                cornerIndex++;
            if (cornerIndex >= corners.Length) return Vector3.zero;
            Vector3 direction = corners[cornerIndex] - transform.position;
            direction.y = 0f;
            return direction.normalized * movementSpeed;
        }

        void ClearPath()
        {
            corners = null;
            cornerIndex = 0;
            repathTimer = 0f;
            stuckTimer = 0f;
        }

        void UpdateRegroup(Vector3 velocity, float dt)
        {
            float traveled = HorizontalDistance(transform.position, previousPosition);
            previousPosition = transform.position;
            if (IsEnemy || playerControlled || game.ActiveHero == null) return;
            float leaderDistance = HorizontalDistance(transform.position, game.ActiveHero.transform.position);
            // Ordinary catch-up uses the faster follow speed. Rescue only a stationary, distant
            // companion outside combat, never across a disconnected island or a locked gate.
            stuckTimer = leaderDistance > 9f && traveled < .01f && !attacking ? stuckTimer + dt : 0f;
            if (game.IsCombatActive || stuckTimer < 5f || leaderDistance < 9f) return;
            Vector3 target = game.ActiveHero.transform.position - game.ActiveHero.transform.forward * 2f;
            foreach (GardenActor enemy in game.Enemies)
                if (enemy != null && enemy.IsAlive && HorizontalDistance(enemy.transform.position, target) < 6f) return;
            if (!NavMesh.SamplePosition(target, out var hit, 1.5f, NavMesh.AllAreas)
                || Mathf.Abs(hit.position.y - target.y) > .8f
                || !NavMesh.SamplePosition(transform.position, out var start, 1f, NavMesh.AllAreas)
                || Mathf.Abs(start.position.y - transform.position.y) > .8f) return;
            var rescuePath = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, rescuePath)
                || rescuePath.status != NavMeshPathStatus.PathComplete) return;
            Vector3[] route = rescuePath.corners;
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 delta = route[i] - route[i - 1];
                var obstacles = Physics.SphereCastAll(route[i - 1] + Vector3.up * .8f, .25f,
                    delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit obstacle in obstacles)
                    if (obstacle.collider.GetComponentInParent<GardenActor>() == null) return;
            }
            Teleport(hit.position);
        }

        public bool TryAttack(bool skill = false)
        {
            if (!initialized || game == null || !game.IsPlaying || !IsAlive || attacking
                || dodgeRecovery > 0f || stunTimer > 0f || attackCooldown > 0f) return false;
            if (IsEnemy && !game.IsEncounterActive(encounterId)) return false;
            if (skill && skillCooldown > 0f) return false;
            usingSkill = skill;
            currentRange = skill && kind == ActorKind.Blade ? 3.3f
                : skill && kind == ActorKind.Hammer ? 4f
                : skill && kind == ActorKind.Boss ? 4.8f : attackRange;
            GardenActor target = FindNearest(IsEnemy ? game.Heroes : game.Enemies,
                attackRange + .5f, playerControlled);
            if (target != null) Face(target.transform.position);
            attackDirection = transform.forward;
            attackElapsed = 0f;
            impactDone = false;
            attacking = true;
            attackCount++;
            if (IsEnemy)
            {
                impactTime = kind == ActorKind.Boss ? (skill ? 1.05f : .75f) : .65f;
                attackLength = impactTime + .32f;
                ShowTelegraph(kind == ActorKind.Spitter ? 1.25f : currentRange);
            }
            else
            {
                impactTime = kind == ActorKind.Hammer ? (skill ? .4f : .28f) : .13f;
                attackLength = kind == ActorKind.Hammer ? .65f : .34f;
            }
            attackCooldown = Mathf.Max(attackInterval, attackLength);
            if (skill) skillCooldown = skillInterval;
            return true;
        }

        void ResolveAttack()
        {
            if (kind == ActorKind.Ranger || kind == ActorKind.Spitter)
            {
                float projectileSpeed = IsEnemy ? 8f : 17f;
                Vector3 origin = transform.position + Vector3.up * .95f + attackDirection * .65f;
                if (usingSkill && kind == ActorKind.Ranger)
                {
                    var volleyHits = new HashSet<GardenActor>();
                    for (int i = -1; i <= 1; i++)
                        GardenProjectile.Spawn(this, game, origin,
                            Quaternion.Euler(0f, i * 13f, 0f) * attackDirection,
                            attackDamage * 1.5f, projectileSpeed, attackRange + 2f,
                            new Color(.22f, .95f, .85f), volleyHits);
                }
                else
                    GardenProjectile.Spawn(this, game, origin, attackDirection,
                        attackDamage, projectileSpeed, attackRange + 2f,
                        IsEnemy ? new Color(1f, .37f, .2f) : new Color(1f, .88f, .22f));
                return;
            }
            List<GardenActor> targets = IsEnemy ? game.Heroes : game.Enemies;
            float damage = attackDamage * (usingSkill ? (kind == ActorKind.Hammer ? 2.15f : 1.9f) : 1f);
            bool radial = usingSkill;
            // The attack resolves once, so no target can receive repeated damage from one swing.
            for (int i = 0; i < targets.Count; i++)
            {
                GardenActor target = targets[i];
                if (target == null || !target.IsAlive) continue;
                if (target.IsEnemy && !game.IsEncounterActive(target.encounterId)) continue;
                Vector3 difference = target.transform.position - transform.position;
                if (Mathf.Abs(difference.y) > 2f) continue;
                difference.y = 0f;
                if (difference.magnitude > currentRange) continue;
                if (!radial && difference.sqrMagnitude > .01f
                    && Vector3.Dot(attackDirection, difference.normalized) < .18f) continue;
                if (!HasClearStrike(target)) continue;
                target.TakeDamage(damage, transform.position);
            }
            ShowImpact(currentRange, IsEnemy ? new Color(1f, .25f, .12f) : new Color(1f, .9f, .4f));
        }

        bool HasClearStrike(GardenActor target)
        {
            Vector3 start = transform.position + Vector3.up * .9f;
            Vector3 finish = target.transform.position + Vector3.up * .9f;
            Vector3 delta = finish - start;
            var hits = Physics.RaycastAll(start, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                // Actor capsules are handled by the damage query; only scenery occludes a strike.
                if (hit.collider.GetComponentInParent<GardenActor>() == null) return false;
            }
            return true;
        }

        public bool TryDodge(Vector3 direction)
        {
            if (!initialized || game == null || !game.IsPlaying || !IsAlive || IsEnemy
                || dodgeCooldown > 0f || dodgeRecovery > 0f || stunTimer > 0f) return false;
            direction.y = 0f;
            dodgeDirection = direction.sqrMagnitude > .01f ? direction.normalized : transform.forward;
            dodgeTimer = .22f;
            dodgeRecovery = .4f;
            dodgeCooldown = .95f;
            knockback = Vector3.zero;
            StopAttack();
            return true;
        }

        public void TakeDamage(float amount, Vector3 sourcePosition)
        {
            if (!initialized || !IsAlive || IsDodging || amount <= 0f || game == null || !game.IsPlaying) return;
            Health = Mathf.Max(0f, Health - amount);
            flashTimer = .13f;
            if (Health <= 0f)
            {
                StopAttack();
                moveInput = Vector3.zero;
                knockback = Vector3.zero;
                controller.enabled = false;
                UpdateVisual(Vector3.zero, 1f);
                game.NotifyDeath(this);
                return;
            }
            if (kind != ActorKind.Boss)
            {
                stunTimer = IsEnemy ? .12f : .18f;
                Vector3 away = transform.position - sourcePosition;
                away.y = 0f;
                knockback = away.normalized * (IsEnemy ? 2.5f : 3.5f);
                // An interrupted telegraph never deals a hidden delayed hit.
                StopAttack();
            }
        }

        public void Heal(float amount)
        {
            if (!initialized || !IsAlive || amount <= 0f) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        public void ResetActor()
        {
            if (!initialized) return;
            StopAttack();
            GardenProjectile.ClearFor(this);
            Health = MaxHealth;
            skillCooldown = dodgeCooldown = attackCooldown = 0f;
            dodgeTimer = dodgeRecovery = stunTimer = flashTimer = 0f;
            attackCount = 0;
            moveInput = knockback = Vector3.zero;
            Teleport(HomePosition);
            transform.rotation = homeRotation;
            if (visualRoot != null && visualRoot != transform)
            {
                visualRoot.localPosition = visualRestPosition;
                visualRoot.localRotation = visualRestRotation;
            }
            if (weaponRoot != null)
            {
                weaponRoot.localPosition = weaponRestPosition;
                weaponRoot.localRotation = weaponRestRotation;
            }
            SetBodyColor(bodyColor);
        }

        public void Teleport(Vector3 position)
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            bool enableAfter = IsAlive;
            if (controller != null) controller.enabled = false;
            transform.position = position;
            verticalSpeed = -2f;
            previousPosition = position;
            ClearPath();
            if (controller != null) controller.enabled = enableAfter;
        }

        void Face(Vector3 position)
        {
            Vector3 direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(direction);
        }

        void StopAttack()
        {
            attacking = false;
            attackElapsed = 0f;
            HideTelegraph();
        }

        void UpdateVisual(Vector3 velocity, float dt)
        {
            if (visualRoot != null && visualRoot != transform)
            {
                if (!IsAlive)
                {
                    visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation,
                        visualRestRotation * Quaternion.Euler(0f, 0f, 80f), 1f - Mathf.Exp(-10f * dt));
                    visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition,
                        visualRestPosition + Vector3.down * .18f, 1f - Mathf.Exp(-10f * dt));
                }
                else
                {
                    float moving = Mathf.Clamp01(velocity.magnitude / speed);
                    float bob = Mathf.Abs(Mathf.Sin(Time.time * (moving > .1f ? 12f : 3f) + animationPhase))
                        * Mathf.Lerp(.025f, .09f, moving);
                    visualRoot.localPosition = visualRestPosition + Vector3.up * bob;
                    float roll = dodgeTimer > 0f ? (1f - dodgeTimer / .22f) * 360f : 0f;
                    float windup = attacking && IsEnemy ? -12f * Mathf.Sin(Mathf.Clamp01(attackElapsed / impactTime) * Mathf.PI * .5f) : 0f;
                    visualRoot.localRotation = visualRestRotation * Quaternion.Euler(roll + windup, 0f,
                        Mathf.Sin(Time.time * 12f) * moving * 4f);
                }
            }
            if (weaponRoot != null)
            {
                float swing = attacking ? Mathf.Sin(Mathf.Clamp01(attackElapsed / attackLength) * Mathf.PI) : 0f;
                weaponRoot.localRotation = weaponRestRotation * Quaternion.Euler(-95f * swing, 0f, -20f * swing);
                weaponRoot.localPosition = weaponRestPosition;
            }
            Color color = !IsAlive ? bodyColor * .38f : flashTimer > 0f ? Color.white
                : IsDodging ? Color.Lerp(bodyColor, Color.white, .55f) : bodyColor;
            color.a = 1f;
            SetBodyColor(color);
            if (telegraph != null && telegraph.enabled)
            {
                float pulse = .55f + .45f * Mathf.Sin(Time.time * 24f);
                telegraph.startColor = telegraph.endColor = new Color(1f, .2f + .45f * pulse, .12f);
            }
        }

        void SetBodyColor(Color color)
        {
            if (bodyRenderer == null || bodyBlock == null) return;
            bodyRenderer.GetPropertyBlock(bodyBlock);
            bodyBlock.SetColor("_BaseColor", color);
            bodyBlock.SetColor("_Color", color);
            bodyRenderer.SetPropertyBlock(bodyBlock);
        }

        void ShowTelegraph(float radius)
        {
            if (telegraph == null)
            {
                var marker = new GameObject("Attack warning");
                marker.transform.SetParent(transform, false);
                telegraph = marker.AddComponent<LineRenderer>();
                ConfigureRing(telegraph);
                telegraph.useWorldSpace = false;
            }
            telegraph.enabled = true;
            SetRing(telegraph, Vector3.up * .07f, radius);
        }

        void HideTelegraph()
        {
            if (telegraph != null) telegraph.enabled = false;
        }

        void ShowImpact(float radius, Color color)
        {
            var effect = new GameObject("Attack sweep");
            var ring = effect.AddComponent<LineRenderer>();
            ConfigureRing(ring);
            ring.useWorldSpace = true;
            ring.startColor = ring.endColor = color;
            ring.startWidth = ring.endWidth = usingSkill ? .16f : .075f;
            SetRing(ring, transform.position + Vector3.up * .12f, radius);
            Destroy(effect, .14f);
        }

        void ConfigureRing(LineRenderer line)
        {
            // A serialized material reference keeps the warning-ring shader in builds.
            line.sharedMaterial = game.effectMaterial;
            line.loop = true;
            line.positionCount = 40;
            line.startWidth = line.endWidth = .09f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        static void SetRing(LineRenderer line, Vector3 center, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / line.positionCount;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnDisable() { HideTelegraph(); }
    }
}
