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
        GardenChapter chapter;
        Keyboard testKeyboard;
        InputSettings originalInputSettings, isolatedInputSettings;
        bool originalRunInBackground, originalPersistentSaves;

        [UnitySetUp]
        public IEnumerator LoadGarden()
        {
            originalPersistentSaves = GardenGame.PersistentSavesEnabled;
            GardenGame.PersistentSavesEnabled = false;
            yield return SceneManager.LoadSceneAsync("ToyGarden", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<GardenGame>();
            Assert.IsNotNull(game);
            game.AcceptPlayerInput = false;
            game.RestartGame();
            chapter = game.Chapter;
            Assert.IsNotNull(chapter, "The chapter component must be wired into the game.");
            yield return null;
        }

        [TearDown]
        public void RemoveSyntheticKeyboard()
        {
            GardenGame.PersistentSavesEnabled = originalPersistentSaves;
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
        public IEnumerator RestoredChapterConnectsEveryRegionMechanismAndExit()
        {
            FreezeForLogic();
            Assert.AreEqual(3, game.Heroes.Count);
            Assert.AreEqual(8, chapter.regions.Length);
            Assert.AreEqual(6, game.TotalEncounters);
            Assert.AreEqual(7, chapter.Puzzles.Count);
            Assert.AreEqual(8, chapter.Gates.Count);
            Assert.AreEqual(23, game.Enemies.Count);
            foreach (var encounter in chapter.encounters)
                Assert.IsTrue(game.Enemies.Any(enemy => enemy.encounterId == encounter.id),
                    $"Encounter {encounter.id} has no enemies.");
            Assert.IsNotNull(Camera.main);
            Assert.IsNotNull(game.projectileMaterial);
            Assert.IsNotNull(game.effectMaterial);
            RestoreChapter(); // Every gate opens through its real puzzle and encounter requirements.
            yield return new WaitForSeconds(.3f); // Allow carving obstacles to leave the navigation world.
            Assert.IsTrue(chapter.Gates.All(gate => gate.IsOpen));
            Assert.IsTrue(NavMesh.SamplePosition(game.Heroes[0].HomePosition, out var start, 2f, NavMesh.AllAreas));
            var destinations = chapter.regions.Select(region => region.center)
                .Concat(chapter.Gates.SelectMany(gate => new[] { gate.before, gate.after }))
                .Concat(chapter.Mechanisms.Where(item => item.kind == MechanismKind.Chest || item.kind == MechanismKind.Exit)
                    .Select(item => item.transform.position));
            foreach (Vector3 destination in destinations)
            {
                Assert.IsTrue(NavMesh.SamplePosition(destination, out var goal, 3f, NavMesh.AllAreas),
                    $"Missing navigation near chapter destination {destination}");
                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, $"Disconnected chapter route to {destination}");
            }
        }

        [UnityTest]
        public IEnumerator PartyActuallyWalksAcrossTheWaterworksRamp()
        {
            game.enabled = false;
            SolvePuzzle(0);
            SolvePuzzle(1);
            ClearEncounter(1);
            foreach (var enemy in game.Enemies) enemy.enabled = false;
            var hero = game.ActiveHero;
            hero.Teleport(new Vector3(14f, .05f, 60f));
            game.Heroes[1].Teleport(new Vector3(11f, .05f, 58f));
            game.Heroes[2].Teleport(new Vector3(11f, .05f, 62f));
            yield return new WaitForSeconds(.3f);
            Assert.IsTrue(NavMesh.SamplePosition(hero.transform.position, out var start, 2f, NavMesh.AllAreas));
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(50f, 2f, 60f), out var goal, 2f, NavMesh.AllAreas));
            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, path);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
            var previous = game.Heroes.ToDictionary(member => member, member => member.transform.position);
            float previousTime = Time.time;
            int corner = 1;
            float deadline = Time.time + 20f;
            while (corner < path.corners.Length && Time.time < deadline)
            {
                Vector3 delta = path.corners[corner] - hero.transform.position;
                delta.y = 0f;
                if (delta.magnitude < .5f) corner++;
                else hero.SetMoveInput(delta.normalized);
                yield return null;
                AssertContinuousPartyMovement(previous, ref previousTime);
            }
            hero.SetMoveInput(Vector3.zero);
            Assert.Less(GardenGame.FlatDistance(hero.transform.position, goal.position), 1.2f);
            deadline = Time.time + 5f;
            while (Time.time < deadline)
            {
                yield return null;
                AssertContinuousPartyMovement(previous, ref previousTime);
            }
            foreach (var member in game.Heroes)
            {
                Assert.Greater(member.transform.position.y, 1.7f, $"{member.kind} did not reach Waterworks.");
                Assert.Less(GardenGame.FlatDistance(member.transform.position, hero.transform.position), 5f);
            }
        }

        [UnityTest]
        public IEnumerator WindmillShortcutUpperLandingAllowsEveryHeroToReturn()
        {
            FreezeForLogic();
            Assert.IsFalse(chapter.IsPuzzleSolved(4));
            Assert.IsFalse(chapter.IsGateOpen(7), "This regression must reproduce the approach before the shortcut is unlocked.");
            var landing = new Vector3(0f, 5.05f, 108f);
            var turnaround = new Vector3(0f, 3.8f, 98f);
            for (int index = 0; index < game.Heroes.Count; index++)
            {
                var hero = game.Heroes[index];
                if (hero != game.ActiveHero) Assert.IsTrue(game.SwitchTo(index));
                hero.enabled = true;
                hero.Teleport(landing); // Set up the approach only; the return leg must begin where walking ended.
                yield return new WaitForSeconds(.15f);
                yield return WalkCharacterTo(hero, turnaround, $"{hero.kind}: descend from Windmill Heights");
                Assert.Less(hero.transform.position.y, 4.6f, "The hero must descend onto the sloped shortcut.");
                yield return WalkCharacterTo(hero, landing, $"{hero.kind}: return over the shortcut's upper landing");
                Assert.AreEqual(landing.y, hero.transform.position.y, .2f,
                    $"{hero.kind} must regain the Windmill Heights platform, not stop below its rim.");
                Assert.IsFalse(chapter.IsGateOpen(7), "Walking back must not depend on opening the distant shortcut gate.");
                hero.enabled = false;
            }
        }

        [UnityTest]
        public IEnumerator EverySlopedBridgeSupportsRoundTripCharacterWalking()
        {
            FreezeForLogic();
            RestoreChapter(); // Keep the real puzzle and encounter requirements when opening the route.
            Assert.IsTrue(chapter.Gates.All(gate => gate.IsOpen));
            yield return new WaitForSeconds(.3f);
            var hero = game.ActiveHero;
            hero.enabled = true;
            // Both endpoints lie on the adjoining platform, beyond the bridge/platform seams.
            // The ruins endpoint stops before the decorative rune stones at z = 80.
            var routes = new[]
            {
                (name: "Orchard canal", from: new Vector3(20f, .05f, 60f), to: new Vector3(46f, 2.05f, 60f)),
                (name: "Waterworks aqueduct", from: new Vector3(64f, 2.05f, 78f), to: new Vector3(64f, 5.05f, 106f)),
                (name: "Ancient ascent", from: new Vector3(-64f, 5.05f, 106f), to: new Vector3(-64f, 8.05f, 81f)),
                (name: "Orchard return shortcut", from: new Vector3(0f, 5.05f, 106f), to: new Vector3(0f, .05f, 78f))
            };
            foreach (var route in routes)
            {
                hero.Teleport(route.from);
                yield return new WaitForSeconds(.15f);
                yield return WalkCharacterTo(hero, route.to, route.name + ": outbound");
                Assert.AreEqual(route.to.y, hero.transform.position.y, .2f, route.name + ": far platform height");
                yield return WalkCharacterTo(hero, route.from, route.name + ": return");
                Assert.AreEqual(route.from.y, hero.transform.position.y, .2f, route.name + ": starting platform height");
            }
            // Cover the shortcut's upper seam away from its centre line while staying inside the railings.
            foreach (float side in new[] { -2.4f, 2.4f })
            {
                var landing = new Vector3(side, 5.05f, 106f);
                var turnaround = new Vector3(side, 3.8f, 98f);
                hero.Teleport(landing);
                yield return new WaitForSeconds(.15f);
                yield return WalkCharacterTo(hero, turnaround, $"Shortcut side {side}: descend");
                yield return WalkCharacterTo(hero, landing, $"Shortcut side {side}: regain landing");
                Assert.AreEqual(landing.y, hero.transform.position.y, .2f);
            }
        }

        IEnumerator WalkCharacterTo(GardenActor hero, Vector3 destination, string route)
        {
            Assert.IsTrue(hero.enabled && hero.IsPlayerControlled && game.IsPlaying,
                route + ": the real player movement loop must be active.");
            float startHeight = hero.transform.position.y;
            float deadline = Time.time + Mathf.Max(3f,
                GardenGame.FlatDistance(hero.transform.position, destination) / 3f + 2f);
            var previous = new Dictionary<GardenActor, Vector3> { [hero] = hero.transform.position };
            float previousTime = Time.time;
            while (GardenGame.FlatDistance(hero.transform.position, destination) > .25f && Time.time < deadline)
            {
                Vector3 direction = destination - hero.transform.position;
                direction.y = 0f;
                hero.SetMoveInput(direction.normalized);
                yield return null;
                AssertContinuousPartyMovement(previous, ref previousTime);
                Assert.Greater(hero.transform.position.y, Mathf.Min(startHeight, destination.y) - 1.5f,
                    route + ": the CharacterController fell below the traversable surface.");
            }
            hero.SetMoveInput(Vector3.zero);
            Assert.Less(GardenGame.FlatDistance(hero.transform.position, destination), .35f,
                $"{route}: CharacterController stopped at {hero.transform.position} before reaching {destination}.");
        }

        [UnityTest]
        public IEnumerator SwingHitsOnceAndDodgeBlocksOnlyDuringItsWindow()
        {
            game.enabled = false;
            SolvePuzzle(0);
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

            yield return TapKey(Key.M);
            Assert.IsTrue(game.MapOpen, "M must open the chapter map.");
            Assert.IsFalse(game.IsPlaying, "The map must suspend gameplay.");
            var mapPositions = game.Heroes.Concat(game.Enemies)
                .ToDictionary(actor => actor, actor => actor.transform.position);
            float mapElapsed = game.Elapsed;
            QueueKeys(Key.W);
            yield return new WaitForSecondsRealtime(.25f);
            foreach (var sample in mapPositions)
                Assert.Less(Vector3.Distance(sample.Key.transform.position, sample.Value), .001f,
                    $"{sample.Key.kind} moved while the map was open.");
            Assert.AreEqual(mapElapsed, game.Elapsed, .001f, "Map viewing must stop elapsed game time.");
            QueueKeys();
            yield return null;
            yield return TapKey(Key.Escape);
            Assert.IsFalse(game.MapOpen, "Escape must close the map.");
            Assert.IsFalse(game.Paused, "Closing the map must not also pause the game.");
            Assert.IsTrue(game.IsPlaying, "Closing the map must resume gameplay.");

            game.RestartGame();
            yield return null;
            Assert.IsFalse(game.IsEncounterActive(1));
            SolvePuzzle(0);
            game.ActiveHero.Teleport(chapter.encounters.First(item => item.id == 1).center);
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
        public IEnumerator AllPuzzlesBattlesSuppliesExitAndRestartCompleteTheChapter()
        {
            FreezeForLogic();
            var exit = chapter.Mechanisms.Single(item => item.kind == MechanismKind.Exit);
            game.ActiveHero.Teleport(exit.transform.position + Vector3.back * 1.5f);
            Assert.IsFalse(game.TryInteract(), "The summit exit must refuse an incomplete chapter.");
            Assert.IsFalse(game.Won);
            for (int id = 2; id <= game.TotalEncounters; id++)
            {
                game.ActivateEncounter(id);
                Assert.IsFalse(game.IsEncounterActive(id), $"Encounter {id} activated before its route opened.");
            }
            RestoreChapter();
            Assert.AreEqual(7, chapter.SolvedCount);
            Assert.AreEqual(6, game.ClearedCount);
            Assert.IsTrue(chapter.CanExit);
            Assert.IsTrue(chapter.Gates.All(gate => gate.IsOpen));
            foreach (var supply in chapter.Mechanisms.Where(item => item.kind == MechanismKind.Chest))
            {
                game.ActiveHero.Teleport(supply.transform.position + Vector3.back * 1.5f);
                Assert.IsTrue(game.TryInteract(), $"Cannot collect {supply.name}");
                Assert.IsTrue(supply.Used);
            }
            Assert.Greater(chapter.SupplyCount, 0);
            game.ActiveHero.Teleport(exit.transform.position + Vector3.back * 1.5f);
            Assert.IsTrue(game.TryInteract());
            Assert.IsTrue(game.Won);
            game.RestartGame();
            Assert.IsFalse(game.Won);
            Assert.IsFalse(chapter.CanExit);
            Assert.AreEqual(0, game.ClearedCount);
            Assert.AreEqual(0, chapter.SolvedCount);
            Assert.AreEqual(0, chapter.SupplyCount);
            Assert.IsTrue(chapter.Gates.All(gate => !gate.IsOpen && gate.barrier.gameObject.activeSelf));
            Assert.IsTrue(game.Enemies.All(enemy => enemy.IsAlive));
            Assert.IsTrue(game.Heroes.All(hero => hero.IsAlive && hero.Health == hero.MaxHealth));
            foreach (var puzzle in chapter.Puzzles)
            {
                Assert.IsFalse(puzzle.Solved);
                Assert.AreEqual(puzzle.crateStart, puzzle.CrateCell);
                Assert.IsTrue(puzzle.Values.All(value => value == 0));
                Assert.AreEqual(0, puzzle.SequenceProgress);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GatesRequireBothConditionsAndPhysicallyBlockThePlayer()
        {
            game.enabled = false;
            foreach (var actor in game.Enemies.Concat(game.Heroes.Where(hero => hero != game.ActiveHero))) actor.enabled = false;
            var entry = chapter.Gates.Single(gate => gate.id == 0);
            var hero = game.ActiveHero;
            hero.Teleport(entry.before);
            Vector3 direction = entry.after - entry.before;
            direction.y = 0f;
            direction.Normalize();
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(entry.before + Vector3.up * .8f, direction,
                GardenGame.FlatDistance(entry.before, entry.after), ~0, QueryTriggerInteraction.Ignore);
            Assert.IsTrue(hits.Any(hit => hit.transform.IsChildOf(entry.barrier)), "Closed gate needs a real collision barrier.");
            hero.SetMoveInput(direction);
            yield return new WaitForSeconds(1.5f);
            hero.SetMoveInput(Vector3.zero);
            Assert.Less(Vector3.Dot(hero.transform.position - entry.barrier.position, direction), .2f,
                "The closed entry gate allowed the CharacterController through.");
            SolvePuzzle(0);
            Assert.IsTrue(entry.IsOpen);
            Assert.IsFalse(entry.barrier.gameObject.activeSelf);
            hero.Teleport(entry.before);
            float deadline = Time.time + 5f;
            while (GardenGame.FlatDistance(hero.transform.position, entry.after) > .6f && Time.time < deadline)
            {
                hero.SetMoveInput(direction);
                yield return null;
            }
            hero.SetMoveInput(Vector3.zero);
            Assert.Less(GardenGame.FlatDistance(hero.transform.position, entry.after), .8f,
                "Opening the gate must allow the player to walk through it.");
            SolvePuzzle(1);
            Assert.IsFalse(chapter.IsGateOpen(1), "Solving the block alone must not bypass the orchard battle.");
            ClearEncounter(1);
            Assert.IsTrue(chapter.IsGateOpen(1));
            ClearEncounter(2);
            Assert.IsFalse(chapter.IsGateOpen(2), "Clearing a battle alone must not bypass its dial puzzle.");
            SolvePuzzle(2);
            Assert.IsTrue(chapter.IsGateOpen(2));
        }

        [UnityTest]
        public IEnumerator PushBlocksRejectWallsBoundsAndSupportResetBeforeSolving()
        {
            FreezeForLogic();
            foreach (int id in new[] { 1, 3 })
            {
                var puzzle = chapter.Puzzles.Single(item => item.id == id);
                var crate = Mechanism(id, MechanismKind.Crate);
                var reset = Mechanism(id, MechanismKind.ResetCrate);
                Vector2Int blockedDirection = id == 1 ? Vector2Int.right : Vector2Int.up;
                var start = puzzle.CrateCell;
                Assert.IsFalse(ActivateFrom(crate, puzzle.CellPosition(start - blockedDirection)),
                    $"Puzzle {id} pushed a block through a stone.");
                Assert.AreEqual(start, puzzle.CrateCell);
                if (id == 3)
                {
                    Assert.IsTrue(ActivateFrom(crate, puzzle.CellPosition(start + Vector2Int.right)));
                    Assert.AreEqual(new Vector2Int(0, 1), puzzle.CrateCell);
                    Assert.IsFalse(ActivateFrom(crate, puzzle.CellPosition(new Vector2Int(1, 1))),
                        "A block must not leave the board.");
                    Assert.AreEqual(new Vector2Int(0, 1), puzzle.CrateCell);
                }
                else
                {
                    Assert.IsTrue(ActivateFrom(crate, puzzle.CellPosition(start + Vector2Int.down)));
                    Assert.AreNotEqual(start, puzzle.CrateCell);
                }
                Assert.IsTrue(ActivateFrom(reset, reset.transform.position + Vector3.back * 1.5f));
                Assert.AreEqual(puzzle.crateStart, puzzle.CrateCell);
                Assert.IsFalse(puzzle.Solved);
                SolvePuzzle(id);
                Assert.IsTrue(puzzle.Solved);
                Assert.AreEqual(puzzle.crateGoal, puzzle.CrateCell);
                Assert.IsFalse(ActivateFrom(reset, reset.transform.position + Vector3.back * 1.5f),
                    "A completed puzzle must keep its solved state.");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DialsAndChimesRequireTheirActualSolutions()
        {
            FreezeForLogic();
            foreach (int id in new[] { 2, 6 })
            {
                var puzzle = chapter.Puzzles.Single(item => item.id == id);
                var dial = Mechanism(id, MechanismKind.Dial, 0);
                for (int i = 0; i < 4; i++) Assert.IsTrue(ActivateNear(dial));
                Assert.AreEqual(0, puzzle.Values[0], "The dial must cycle back after four steps.");
                Assert.IsFalse(puzzle.Solved, "An incomplete dial combination cannot restore a route.");
                SolvePuzzle(id);
                CollectionAssert.AreEqual(puzzle.solution, puzzle.Values);
                Assert.IsTrue(puzzle.Solved);
                Assert.IsFalse(ActivateNear(dial));
            }
            var chimes = chapter.Puzzles.Single(item => item.id == 5);
            Assert.IsTrue(ActivateNear(Mechanism(5, MechanismKind.SequenceSwitch, 0)));
            Assert.AreEqual(1, chimes.SequenceProgress);
            Assert.IsTrue(ActivateNear(Mechanism(5, MechanismKind.SequenceSwitch, 1)));
            Assert.AreEqual(0, chimes.SequenceProgress, "An incorrect second chime must reset the sequence.");
            Assert.IsFalse(chimes.Solved);
            SolvePuzzle(5);
            Assert.IsTrue(chimes.Solved);
            Assert.AreEqual(chimes.solution.Length, chimes.SequenceProgress);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InteractionsRespectReachPauseAndSingleUseSupplies()
        {
            FreezeForLogic();
            var lever = Mechanism(0, MechanismKind.Lever);
            Assert.IsFalse(ActivateFrom(lever, lever.transform.position + Vector3.back * 20f));
            game.ActiveHero.Teleport(lever.transform.position + Vector3.back * 1.5f);
            game.TogglePause();
            Assert.IsFalse(game.TryInteract());
            Assert.IsFalse(chapter.IsPuzzleSolved(0));
            game.TogglePause();
            Assert.IsTrue(game.TryInteract());
            Assert.IsTrue(chapter.IsPuzzleSolved(0));
            Assert.IsFalse(game.TryInteract(), "A lever must not repeatedly award completion.");
            var chest = chapter.Mechanisms.First(item => item.kind == MechanismKind.Chest);
            game.ActiveHero.Teleport(chest.transform.position + Vector3.back * 1.5f);
            game.ActiveHero.TakeDamage(20f, game.ActiveHero.transform.position + Vector3.right);
            Assert.IsTrue(game.TryInteract());
            Assert.AreEqual(game.ActiveHero.MaxHealth, game.ActiveHero.Health);
            Assert.AreEqual(1, chapter.SupplyCount);
            Assert.IsFalse(game.TryInteract());
            Assert.AreEqual(1, chapter.SupplyCount);
            yield return null;
        }

        void FreezeForLogic()
        {
            game.enabled = false;
            foreach (var actor in game.Heroes.Concat(game.Enemies)) actor.enabled = false;
        }

        GardenMechanism Mechanism(int puzzleId, MechanismKind kind, int index = 0) => chapter.Mechanisms
            .Single(item => item.puzzleId == puzzleId && item.kind == kind && (kind != MechanismKind.Dial && kind != MechanismKind.SequenceSwitch || item.index == index));

        bool ActivateNear(GardenMechanism mechanism) => ActivateFrom(mechanism, mechanism.transform.position + Vector3.back * 1.5f);

        bool ActivateFrom(GardenMechanism mechanism, Vector3 position)
        {
            game.ActiveHero.Teleport(position);
            Physics.SyncTransforms();
            var puzzle = chapter.Puzzles.Single(item => item.id == mechanism.puzzleId);
            return puzzle.TryActivate(mechanism, game.ActiveHero.transform.position);
        }

        void SolvePuzzle(int id)
        {
            var puzzle = chapter.Puzzles.Single(item => item.id == id);
            if (puzzle.Solved) return;
            switch (puzzle.kind)
            {
                case PuzzleKind.Lever:
                    Assert.IsTrue(ActivateNear(Mechanism(id, MechanismKind.Lever)), $"Cannot use lever {id}");
                    break;
                case PuzzleKind.PushBlock:
                    var crate = Mechanism(id, MechanismKind.Crate);
                    foreach (var direction in FindCrateRoute(puzzle))
                    {
                        Assert.IsTrue(ActivateFrom(crate, puzzle.CellPosition(puzzle.CrateCell - direction)),
                            $"Cannot push puzzle {id} from {puzzle.CrateCell} toward {direction}");
                    }
                    break;
                case PuzzleKind.Dials:
                    for (int index = 0; index < puzzle.solution.Length; index++)
                    {
                        int steps = (puzzle.solution[index] - puzzle.Values[index] + 4) % 4;
                        for (int step = 0; step < steps; step++)
                            Assert.IsTrue(ActivateNear(Mechanism(id, MechanismKind.Dial, index)), $"Cannot turn dial {id}:{index}");
                    }
                    break;
                case PuzzleKind.Sequence:
                    foreach (int index in puzzle.solution)
                        Assert.IsTrue(ActivateNear(Mechanism(id, MechanismKind.SequenceSwitch, index)), $"Cannot ring chime {id}:{index}");
                    break;
            }
            Assert.IsTrue(puzzle.Solved, $"Puzzle {id} did not reach its solved state.");
            chapter.TickChapter();
        }

        static List<Vector2Int> FindCrateRoute(GardenPuzzle puzzle)
        {
            var frontier = new Queue<Vector2Int>();
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            frontier.Enqueue(puzzle.CrateCell);
            previous[puzzle.CrateCell] = puzzle.CrateCell;
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (frontier.Count > 0)
            {
                var cell = frontier.Dequeue();
                if (cell == puzzle.crateGoal) break;
                foreach (var direction in directions)
                {
                    var next = cell + direction;
                    // A push requires a free standing tile behind the crate as well as a free destination.
                    if (!puzzle.IsCellOpen(next) || !puzzle.IsCellOpen(cell - direction) || previous.ContainsKey(next)) continue;
                    previous[next] = cell;
                    frontier.Enqueue(next);
                }
            }
            Assert.IsTrue(previous.ContainsKey(puzzle.crateGoal), $"Push puzzle {puzzle.id} has no legal route.");
            var result = new List<Vector2Int>();
            for (var cell = puzzle.crateGoal; cell != puzzle.CrateCell; cell = previous[cell]) result.Add(cell - previous[cell]);
            result.Reverse();
            return result;
        }

        void ClearEncounter(int id)
        {
            game.ActivateEncounter(id);
            Assert.IsTrue(game.IsEncounterActive(id), $"Encounter {id} is gated or did not activate.");
            foreach (var enemy in game.Enemies.Where(item => item.encounterId == id)) enemy.TakeDamage(10000f, Vector3.zero);
            Assert.IsTrue(game.IsEncounterCleared(id), $"Encounter {id} did not clear.");
            chapter.TickChapter();
        }

        void RestoreChapter()
        {
            SolvePuzzle(0);
            for (int id = 1; id <= 3; id++) { SolvePuzzle(id); ClearEncounter(id); }
            SolvePuzzle(4);
            SolvePuzzle(5);
            ClearEncounter(4);
            SolvePuzzle(6);
            ClearEncounter(5);
            ClearEncounter(6);
        }
    }
}
