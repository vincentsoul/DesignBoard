using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ToyGarden.Tests
{
    public class GardenPlayModeTests
    {
        GardenGame game;
        Keyboard testKeyboard;
        InputSettings originalInputSettings, isolatedInputSettings;
        bool originalRunInBackground;

        [UnitySetUp]
        public IEnumerator LoadGarden()
        {
            yield return SceneManager.LoadSceneAsync("ToyGarden", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<GardenGame>();
            Assert.IsNotNull(game);
            game.AcceptPlayerInput = false;
            game.RestartGame();
            yield return null;
        }

        [TearDown]
        public void RemoveSyntheticKeyboard()
        {
            if (game != null) game.AcceptPlayerInput = false;
            if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
            testKeyboard = null;
            if (isolatedInputSettings != null)
            {
                InputSystem.settings = originalInputSettings;
                Application.runInBackground = originalRunInBackground;
                Object.DestroyImmediate(isolatedInputSettings);
                isolatedInputSettings = originalInputSettings = null;
            }
        }

        [UnityTest]
        public IEnumerator MapConnectsSpawnChestUpperArenaAndExit()
        {
            Assert.AreEqual(3, game.Heroes.Count);
            Assert.AreEqual(5, game.Enemies.Count);
            Assert.IsNotNull(Camera.main);
            Assert.IsNotNull(game.projectileMaterial, "Projectile material must be referenced by the scene for player builds.");
            Assert.IsNotNull(game.effectMaterial, "Warning-ring material must be referenced by the scene for player builds.");
            Assert.IsTrue(NavMesh.SamplePosition(game.ActiveHero.transform.position, out var start, 2, NavMesh.AllAreas));
            foreach (var destination in new[] { game.encounterOneCenter, game.chestPosition, new Vector3(22, 3, -5), game.encounterTwoCenter, game.exitPosition + Vector3.right * 2 })
            {
                Assert.IsTrue(NavMesh.SamplePosition(destination, out var goal, 3, NavMesh.AllAreas), $"Missing navmesh near {destination}");
                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, $"Disconnected route to {destination}");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PartyActuallyWalksFromEntryUpRamp()
        {
            game.enabled = false; // Drive movement directly, keeping encounters dormant for this navigation check.
            var hero = game.ActiveHero;
            Assert.IsTrue(NavMesh.SamplePosition(hero.transform.position, out var start, 2, NavMesh.AllAreas));
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(22, 3, -5), out var goal, 2, NavMesh.AllAreas));
            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, path);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
            var previousPositions = game.Heroes.ToDictionary(member => member, member => member.transform.position);
            float previousSampleTime = Time.time;
            int corner = 1;
            float end = Time.time + 25;
            while (corner < path.corners.Length && Time.time < end)
            {
                var delta = path.corners[corner] - hero.transform.position;
                delta.y = 0;
                if (delta.magnitude < .5f) corner++;
                else hero.SetMoveInput(delta.normalized);
                yield return null;
                AssertContinuousPartyMovement(previousPositions, ref previousSampleTime);
            }
            hero.SetMoveInput(Vector3.zero);
            Assert.Less(GardenGame.FlatDistance(hero.transform.position, goal.position), 1.2f, "Player failed to traverse the baked route.");
            float regroupUntil = Time.time + 5;
            while (Time.time < regroupUntil)
            {
                yield return null;
                AssertContinuousPartyMovement(previousPositions, ref previousSampleTime);
            }
            foreach (var member in game.Heroes)
            {
                Assert.Greater(member.transform.position.y, 2.7f, $"{member.kind} did not reach the upper terrace.");
                Assert.Less(GardenGame.FlatDistance(member.transform.position, hero.transform.position), 5, $"{member.kind} was left behind.");
            }
        }

        [UnityTest]
        public IEnumerator SwingHitsOnceAndDodgeBlocksOnlyDuringItsWindow()
        {
            game.enabled = false;
            game.ActivateEncounter(1);
            var hero = game.ActiveHero;
            var enemy = game.Enemies.First(e => e.kind == ActorKind.Grunt && e.encounterId == 1);
            foreach (var e in game.Enemies) e.enabled = false;
            foreach (var h in game.Heroes.Where(h => h != hero)) h.enabled = false;
            hero.Teleport(enemy.transform.position - Vector3.forward * 1.5f);
            hero.transform.forward = Vector3.forward;
            float health = enemy.Health;
            Assert.IsTrue(hero.TryAttack());
            yield return new WaitForSeconds(.2f);
            Assert.AreEqual(health - 24f, enemy.Health, .001f,
                "One Blade swing must deal exactly 24 damage, including its first impact frame.");
            float afterHit = enemy.Health;
            yield return new WaitForSeconds(.4f);
            Assert.AreEqual(afterHit, enemy.Health, "A single swing must not deal a hit each frame.");
            Assert.IsTrue(hero.TryDodge(Vector3.back));
            health = hero.Health;
            hero.TakeDamage(10, enemy.transform.position);
            Assert.AreEqual(health, hero.Health);
            yield return new WaitForSeconds(.45f);
            hero.TakeDamage(10, enemy.transform.position);
            Assert.AreEqual(health - 10, hero.Health);
        }

        [UnityTest]
        public IEnumerator SwitchingPreservesStateAndDeathTransfersControl()
        {
            game.enabled = false;
            var original = game.ActiveHero;
            Assert.IsTrue(original.TryAttack(true));
            float cooldown = original.SkillCooldownRemaining;
            original.TakeDamage(15, original.transform.position - Vector3.forward);
            float health = original.Health;
            Assert.IsTrue(game.SwitchHero());
            Assert.IsFalse(original.IsPlayerControlled);
            Assert.AreEqual(1, game.Heroes.Count(h => h.IsPlayerControlled));
            Assert.AreEqual(cooldown, original.SkillCooldownRemaining);
            Assert.AreEqual(health, original.Health);
            game.ActiveHero.TakeDamage(10000, Vector3.zero);
            Assert.IsTrue(game.ActiveHero.IsAlive);
            Assert.AreEqual(1, game.Heroes.Count(h => h.IsPlayerControlled));
            foreach (var member in game.Heroes.ToArray()) if (member.IsAlive) member.TakeDamage(10000, Vector3.zero);
            Assert.IsTrue(game.GameOver);
            game.RestartGame();
            Assert.IsFalse(game.GameOver);
            Assert.IsTrue(game.Heroes.All(h => h.IsAlive && h.Health == h.MaxHealth));
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardRoutesMovementSwitchPauseEncounterAndRestart()
        {
            // Use real queued Input System events and the ordinary Game.Update loop.
            // Do not call InputSystem.Update here: it can consume WasPressedThisFrame
            // before the game gets its dynamic input update.
            // A batchmode Editor has no focused Game View. Use a temporary settings
            // copy so synthetic input reaches gameplay without modifying the project's
            // InputSettings asset or leaving different background behavior after this test.
            originalInputSettings = InputSystem.settings;
            originalRunInBackground = Application.runInBackground;
            isolatedInputSettings = Object.Instantiate(originalInputSettings);
            isolatedInputSettings.hideFlags = HideFlags.HideAndDontSave;
            isolatedInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            isolatedInputSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = isolatedInputSettings;
            Application.runInBackground = true;
            yield return null;
            game.RestartGame(); // Clear any initial application-focus pause before routing test input.
            testKeyboard = InputSystem.AddDevice<Keyboard>("Garden PlayMode Keyboard");
            InputSystem.EnableDevice(testKeyboard);
            Assert.IsTrue(testKeyboard.enabled, "The isolated test keyboard must be enabled in batchmode.");
            game.AcceptPlayerInput = true;
            Assert.IsTrue(game.IsPlaying);
            var original = game.ActiveHero;
            Vector3 start = original.transform.position;
            Vector3 cameraForward = Camera.main.transform.forward;
            cameraForward.y = 0;
            QueueKeys(Key.W);
            yield return WaitForState(() => testKeyboard.wKey.isPressed, 1f,
                "The queued keyboard event did not reach the player's Input System update.");
            yield return WaitForState(() => GardenGame.FlatDistance(original.transform.position, start) > 1f,
                3f, "Holding W through Input System did not move the player.");
            QueueKeys();
            yield return null;
            Assert.Greater(Vector3.Dot(original.transform.position - start, cameraForward.normalized), .8f,
                "W must move in the camera's forward direction.");

            yield return TapKey(Key.Tab);
            Assert.AreNotSame(original, game.ActiveHero, "Tab must switch the active hero.");
            Assert.IsFalse(original.IsPlayerControlled);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.AreEqual(1, game.Heroes.Count(member => member.IsPlayerControlled),
                "There must still be one input owner after subsequent Update calls.");

            var selected = game.ActiveHero;
            yield return TapKey(Key.Q);
            Assert.Greater(selected.SkillCooldownRemaining, 0f, "Q must start the selected hero's skill.");
            yield return TapKey(Key.Escape);
            Assert.IsTrue(game.Paused, "Escape must pause through the input action map.");
            var pausedPositions = game.Heroes.Concat(game.Enemies)
                .ToDictionary(actor => actor, actor => actor.transform.position);
            float pausedCooldown = selected.SkillCooldownRemaining;
            float pausedHealth = selected.Health;
            float pausedElapsed = game.Elapsed;
            QueueKeys(Key.W);
            yield return new WaitForSecondsRealtime(.35f);
            foreach (var sample in pausedPositions)
                Assert.Less(Vector3.Distance(sample.Key.transform.position, sample.Value), .001f,
                    $"{sample.Key.kind} moved while the game was paused.");
            Assert.AreEqual(pausedCooldown, selected.SkillCooldownRemaining, .001f, "Pause must freeze cooldowns.");
            selected.TakeDamage(10f, selected.transform.position + Vector3.forward);
            Assert.AreEqual(pausedHealth, selected.Health, "Pause must reject damage.");
            Assert.AreEqual(pausedElapsed, game.Elapsed, .001f, "Pause must stop elapsed game time.");
            QueueKeys();
            yield return null;

            yield return TapKey(Key.Escape);
            Assert.IsFalse(game.Paused, "A second Escape press must resume.");
            Vector3 resumePosition = selected.transform.position;
            QueueKeys(Key.W);
            yield return WaitForState(() => GardenGame.FlatDistance(selected.transform.position, resumePosition) > .6f,
                3f, "W movement did not resume after Escape.");
            QueueKeys();
            yield return null;

            game.RestartGame();
            yield return null;
            Assert.IsFalse(game.IsEncounterActive(1));
            game.ActiveHero.Teleport(game.encounterOneCenter);
            yield return WaitForState(() => game.IsEncounterActive(1), 2f,
                "Entering the plaza must activate the encounter through Game.Update.");
            foreach (var member in game.Heroes.ToArray())
                if (member.IsAlive) member.TakeDamage(10000f, Vector3.zero);
            Assert.IsTrue(game.GameOver);
            yield return TapKey(Key.R);
            Assert.IsFalse(game.GameOver, "R must restart from the game-over screen.");
            Assert.IsFalse(game.Paused);
            Assert.AreEqual(0, game.ClearedCount);
            Assert.IsTrue(game.Heroes.All(member => member.IsAlive && member.Health == member.MaxHealth));
        }

        void QueueKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(keys));
        }

        IEnumerator TapKey(Key key)
        {
            QueueKeys(key);
            yield return new WaitForSecondsRealtime(.08f);
            QueueKeys();
            yield return null;
        }

        static IEnumerator WaitForState(System.Func<bool> condition, float timeout, string failure)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), failure);
        }

        static void AssertContinuousPartyMovement(Dictionary<GardenActor, Vector3> previous, ref float previousTime)
        {
            float elapsed = Mathf.Max(0f, Time.time - previousTime);
            // The fastest catch-up walk is 7.67 m/s horizontally. Allow ramp ascent
            // and small CharacterController corrections, scaled by the actual frame time.
            float maximumTravel = 12f * elapsed + .35f;
            foreach (var member in previous.Keys.ToArray())
            {
                Assert.LessOrEqual(Vector3.Distance(member.transform.position, previous[member]), maximumTravel,
                    $"{member.kind} jumped position; rescue teleport must not make a walking test pass.");
                previous[member] = member.transform.position;
            }
            previousTime = Time.time;
        }

        [UnityTest]
        public IEnumerator EncountersChestVictoryAndReplayCompleteTheLoop()
        {
            game.enabled = false;
            Assert.IsFalse(game.TryInteract());
            game.ActivateEncounter(1);
            foreach (var enemy in game.Enemies.Where(e => e.encounterId == 1)) enemy.TakeDamage(10000, Vector3.zero);
            Assert.AreEqual(1, game.ClearedCount);
            game.ActivateEncounter(2);
            foreach (var enemy in game.Enemies.Where(e => e.encounterId == 2)) enemy.TakeDamage(10000, Vector3.zero);
            Assert.AreEqual(2, game.ClearedCount);
            Assert.IsFalse(game.exitGate.gameObject.activeSelf);
            game.ActiveHero.Teleport(game.chestPosition);
            game.ActiveHero.TakeDamage(20, Vector3.zero);
            Assert.IsTrue(game.TryInteract());
            Assert.IsTrue(game.HasChest);
            Assert.AreEqual(game.ActiveHero.MaxHealth, game.ActiveHero.Health);
            Assert.IsFalse(game.TryInteract(), "The chest reward must only be given once.");
            game.ActiveHero.Teleport(game.exitPosition);
            Assert.IsTrue(game.TryInteract());
            Assert.IsTrue(game.Won);
            game.RestartGame();
            Assert.IsFalse(game.Won);
            Assert.IsFalse(game.HasChest);
            Assert.AreEqual(0, game.ClearedCount);
            Assert.IsTrue(game.exitGate.gameObject.activeSelf);
            Assert.IsTrue(game.Enemies.All(e => e.IsAlive));
            yield return null;
        }
    }
}
