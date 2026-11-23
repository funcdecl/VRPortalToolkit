using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRPortalToolkit.Presence
{
    public sealed partial class PresenceStudy
    {
        [Serializable]
        private sealed class SmokeReport
        {
            public bool passed;
            public string graphicsDevice;
            public string graphicsApi;
            public int screenshotCount;
            public int trainingTrials;
            public int recordedTrials;
            public int questionnaireStops;
            public int successfulOrbTransfers;
            public int successfulReactions;
            public int placementLockChecks;
            public bool movementTrigger;
            public bool inactivityRetraction;
            public bool releaseRetraction;
            public bool wallClipping;
            public bool nearPlaneFallback;
            public bool pointerLanding;
            public List<string> failures = new List<string>();
        }

        private IEnumerator RunSmoke(string directory)
        {
            Directory.CreateDirectory(directory);
            var report = new SmokeReport
            {
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString()
            };
            Application.LogCallback onLog = (message, trace, kind) =>
            {
                if (kind == LogType.Error || kind == LogType.Assert || kind == LogType.Exception)
                    report.failures.Add(message);
            };
            Application.logMessageReceived += onLog;

            StartComparison();
            yield return null;
            overlay.Technique = OverlayTechnique.Blended;
            overlay.ResetState();
            Vector3 pose = exit.transform.position;
            exit.transform.position = pose + Vector3.right * 0.02f;
            overlay.Tick(true, false, true, 0.1f);
            bool quiet = !overlay.IsActive;
            exit.transform.position = pose + Vector3.right * 0.04f;
            overlay.Tick(true, false, true, 0.1f);
            bool shift = overlay.IsActive;
            exit.transform.position = pose;
            overlay.ResetState();
            Quaternion rotation = exit.transform.rotation;
            exit.transform.rotation = rotation * Quaternion.Euler(0f, 6f, 0f);
            overlay.Tick(true, false, true, 0.1f);
            report.movementTrigger = quiet && shift && overlay.IsActive;
            exit.transform.rotation = rotation;
            overlay.Tick(true, false, false, 2f);
            overlay.Tick(true, false, false, PresenceOverlay.TransitionSeconds);
            report.inactivityRetraction = !overlay.IsActive && overlay.Progress == 0f;
            overlay.Tick(true, true, false, 0.5f);
            overlay.Tick(false, false, false, PresenceOverlay.TransitionSeconds);
            report.releaseRetraction = !overlay.IsActive && overlay.Progress == 0f;

            foreach (OverlayTechnique technique in Enum.GetValues(typeof(OverlayTechnique)))
            {
                SetPracticeTechnique(technique);
                overlay.Tick(true, true, false, 0.25f);
                if (technique == OverlayTechnique.Blended)
                {
                    yield return Capture(directory, "blended-transition.png");
                    ++report.screenshotCount;
                }

                overlay.Tick(true, true, false, 0.25f);
                yield return Capture(directory, technique.ToString().ToLowerInvariant() + ".png");
                ++report.screenshotCount;
            }

            SetPracticeTechnique(OverlayTechnique.Absolute);
            overlay.Tick(true, true, false, 0.5f);
            yield return new WaitForEndOfFrame();
            report.nearPlaneFallback = !overlay.Clipped;
            Vector3 mappedEye = entrance.portal.teleportMatrix.MultiplyPoint3x4(view.transform.position);
            Vector3 mappedPortal = entrance.portal.teleportMatrix.MultiplyPoint3x4(entrance.transform.position);
            GameObject testWall = PresenceWorld.Shape(PrimitiveType.Cube, "Clipping test barrier", transform,
                Vector3.Lerp(mappedEye, mappedPortal, 0.52f), new Vector3(2f, 3.2f, 0.06f),
                surface, PresenceWorld.WallLayer);
            testWall.transform.rotation = Quaternion.LookRotation(mappedPortal - mappedEye);
            UnityEngine.Physics.SyncTransforms();
            yield return Capture(directory, "wall-clipping.png");
            ++report.screenshotCount;
            report.wallClipping = overlay.Clipped;
            testWall.SetActive(false);
            Destroy(testWall);

            protocol = true;
            participant = 1;
            plan = PresenceProtocol.Trials(participant);
            recorder = new PresenceRecorder(Path.Combine(directory, "protocol"), participant, true);
            trialIndex = 0;
            LoadTrial(plan[0]);
            double time = 10;
            for (int i = 0; i < plan.Count; ++i)
            {
                yield return null;
                try
                {
                    PresenceResult finished = CheckTrial(ref time, report);
                    if (finished.block == 0) ++report.trainingTrials;
                    else ++report.recordedTrials;
                    report.successfulOrbTransfers += finished.sorted;
                    report.successfulReactions += finished.reactions;
                    if (stage == Stage.Survey)
                    {
                        ++report.questionnaireStops;
                        NextTrial();
                    }
                }
                catch (Exception error)
                {
                    report.failures.Add("Trial " + (i + 1) + ": " + error.Message);
                    break;
                }
            }

            Application.logMessageReceived -= onLog;
            report.passed = report.failures.Count == 0 && report.trainingTrials == 8 && report.recordedTrials == 24 &&
                report.questionnaireStops == 4 && report.successfulOrbTransfers == 128 &&
                report.successfulReactions == 64 && report.placementLockChecks == 64 &&
                report.movementTrigger && report.inactivityRetraction && report.releaseRetraction &&
                report.wallClipping && report.nearPlaneFallback && report.pointerLanding && stage == Stage.Complete;
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("PRESENCE_SMOKE " + (report.passed ? "PASS" : "FAIL"));
            Application.Quit(report.passed ? 0 : 1);
        }

        private IEnumerator Capture(string folder, string name)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(folder, name), image.EncodeToPNG());
            Destroy(image);
        }

        private PresenceResult CheckTrial(ref double time, SmokeReport report)
        {
            BeginTrial(time);
            PresenceResult current = result;
            // The test drives the public study actions with a scripted clock.
            if (!report.pointerLanding && layout.Route != null)
            {
                FitAperture(true);
                UpdatePointer(new Ray(view.transform.position, view.transform.forward));
                if (validTarget)
                {
                    report.pointerLanding = TryMoveExit(target, time + 0.05);
                    Require(report.pointerLanding, "The confirmed pointer destination was rejected.");
                }
                FitAperture(false);
            }

            if (layout.Route != null)
            {
                foreach (Vector2Int cell in layout.Route)
                    WalkStep(layout.Centre(cell), ref time, report);
            }
            else
            {
                foreach (Vector2Int cell in WalkableObstacleRoute())
                    WalkStep(layout.Centre(cell), ref time, report);
            }

            // Finishing navigation can bring forward a pending reaction prompt.
            for (int i = 0; i < 3 && stage != Stage.Sorting; ++i)
            {
                TickTrial(time += 0.2, true, false, 0.2f);
                if (stage == Stage.Reaction) CheckReaction(ref time, report);
            }
            Require(stage == Stage.Sorting && current.reactions == 2, "Navigation did not lead to sorting after two reactions.");
            Select(new Ray(view.transform.position, Vector3.back), time + 0.03);
            Require(!heldOrb, "Outside-aperture ray picked a secondary orb.");
            Matrix4x4 map = entrance.portal.teleportMatrix.inverse;
            int sorted = 0;
            foreach (PresenceOrb orb in orbs.ToArray())
            {
                Vector3 virtualTarget = map.MultiplyPoint3x4(orb.transform.position);
                Select(RayTo(virtualTarget), time += 0.08);
                Require(heldOrb == orb && heldOrb.transform.position.x < 50f, "Sorting orb did not transfer through the aperture.");
                if (sorted == 0)
                {
                    int wrong = orb.Number == 8 ? 1 : orb.Number + 1;
                    Select(RayTo(world.BinAt(wrong).position + Vector3.up * 0.15f), time += 0.08);
                    Require(!heldOrb && orb.gameObject.activeSelf && current.sortingErrors == 1 &&
                        Vector3.Distance(orb.transform.position, orb.Home) < 0.01f, "Wrong cup did not return the sorting orb.");
                    Select(RayTo(virtualTarget), time += 0.08);
                    Require(heldOrb == orb, "Returned sorting orb could not be picked again.");
                }
                Select(RayTo(world.BinAt(orb.Number).position + Vector3.up * 0.15f), time += 0.08);
                ++sorted;
            }
            Require(current.sorted == 4 && current.totalSeconds > 0, "The four matching drops did not complete the trial.");
            time += 0.5;
            return current;
        }

        private void CheckReaction(ref double time, SmokeReport report)
        {
            Vector3 oldPosition = exit.transform.position;
            Quaternion oldRotation = exit.transform.rotation;
            TurnExit(36f, time);
            bool moved = TryMoveExit(destination + Vector3.forward, time);
            Require(!moved && exit.transform.position == oldPosition && exit.transform.rotation == oldRotation,
                "Portal exit moved during the reaction prompt.");
            ++report.placementLockChecks;
            time += 0.12;
            Select(RayTo(reactionOrb.transform.position), time);
            Require(heldOrb == reactionOrb, "The reaction orb was not selectable in the primary room.");
            int correct = heldOrb.Number;
            int wrong = correct == 8 ? 1 : correct + 1;
            int oldErrors = result.reactionErrors;
            Select(RayTo(world.BinAt(wrong).position + Vector3.up * 0.15f), time += 0.12);
            Require(!heldOrb && result.reactionErrors == oldErrors + 1, "Wrong reaction drop was not counted.");
            Select(RayTo(reactionOrb.transform.position), time += 0.12);
            Require(heldOrb == reactionOrb, "The reaction orb did not return for a retry.");
            Select(RayTo(world.BinAt(correct).position + Vector3.up * 0.15f), time += 0.12);
            Require(stage == Stage.Navigation, "Correct reaction drop did not resume navigation.");
        }

        private void WalkStep(Vector3 point, ref double time, SmokeReport report)
        {
            if (Vector3.Distance(destination, point) < 0.16f || stage != Stage.Navigation) return;
            time += 0.35;
            Require(TryMoveExit(point, time), "The valid route was obstructed near " + layout.CellAt(point) + ".");
            TickTrial(time, true, true, 0.35f);
            if (stage == Stage.Reaction) CheckReaction(ref time, report);
        }

        private IEnumerable<Vector2Int> WalkableObstacleRoute()
        {
            Vector2Int start = layout.CellAt(destination);
            var queue = new Queue<Vector2Int>();
            var parent = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (cell == layout.Goal)
                {
                    var route = new List<Vector2Int>();
                    while (cell != start)
                    {
                        route.Add(cell);
                        cell = parent[cell];
                    }
                    route.Reverse();
                    return route;
                }

                Vector3 from = layout.Centre(cell);
                foreach (Vector2Int direction in PresenceLayout.Directions)
                {
                    Vector2Int next = cell + direction;
                    Vector3 to = layout.Centre(next);
                    if (parent.ContainsKey(next) || !layout.Contains(to) ||
                        UnityEngine.Physics.CheckSphere(to + Vector3.up * 0.43f, 0.14f, PresenceWorld.MovementMask) ||
                        UnityEngine.Physics.SphereCast(from + Vector3.up * 0.43f, 0.11f, (to - from).normalized,
                            out _, layout.CellSize, PresenceWorld.MovementMask))
                        continue;
                    parent[next] = cell;
                    queue.Enqueue(next);
                }
            }
            throw new InvalidOperationException("No path across obstacle layout " + layout.Goal + ".");
        }

        private Ray RayTo(Vector3 targetPosition) =>
            new Ray(view.transform.position, (targetPosition - view.transform.position).normalized);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
