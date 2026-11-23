using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VRPortalToolkit.Rendering;
using Random = System.Random;

namespace VRPortalToolkit.Presence
{
    public sealed partial class PresenceStudy : MonoBehaviour
    {
        private enum Stage { Menu, Compare, Ready, Navigation, Reaction, Sorting, Survey, Complete }

        public Camera view;
        public PortalMeshRenderer entrance;
        public Portal exit;
        public Material surface;
        public Material composite;
        public Material pointerMaterial;
        public Material labelMaterial;

        private readonly PresenceInput input = new PresenceInput();
        private PresenceWorld world;
        private PresenceOverlay overlay;
        private AudioSource chime;
        private AudioClip chimeClip;
        private LineRenderer line;
        private GameObject ghost;
        private Material pointerPaint;
        private Material framePaint;
        private readonly Transform[] frame = new Transform[4];
        private readonly Vector3[] trajectory = new Vector3[30];
        private readonly List<PresenceOrb> orbs = new List<PresenceOrb>(4);

        private PresenceScenario practiceScenario = PresenceScenario.Obstacles;
        private OverlayTechnique practiceTechnique = OverlayTechnique.Blended;
        private int practiceLayout = 16;
        private bool leftHanded;
        private int participant = 1;
        private bool protocol;
        private bool automated;
        private Stage stage;
        private List<PresenceTrial> plan;
        private int trialIndex;
        private PresenceTrial trial;
        private PresenceLayout layout;
        private PresenceResult result;
        private PresenceRecorder recorder;

        private Vector3 destination;
        private float exitYaw;
        private bool changedExit;
        private bool stretched;
        private bool validTarget;
        private Vector3 target;
        private int points;
        private PresenceOrb reactionOrb;
        private PresenceOrb heldOrb;
        private double started;
        private double lastMotion;
        private double pausedSeconds;
        private double firstGrab;
        private double nextReaction;
        private double cueStarted;
        private double cueGrabbed;
        private double overlaySeconds;
        private double headAimSeconds;
        private float previousProgress;
        private float backwards;
        private bool backwardsCounted;
        private string feedback = "";
        private double feedbackUntil;
        private string outputRoot;
        private GameObject headsetStatus;

        private double Now => Time.realtimeSinceStartupAsDouble;
        private int MeasuredIndex => trialIndex - 7;
        private bool IsRunning => stage == Stage.Navigation || stage == Stage.Reaction || stage == Stage.Sorting;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            input.Recenter(view.transform);
            overlay = gameObject.AddComponent<PresenceOverlay>();
            overlay.Attach(entrance, composite);
            overlay.WallMask = 1 << PresenceWorld.WallLayer;
            world = new PresenceWorld(surface, labelMaterial);
            world.Eye = view;
            world.Entrance = entrance.portal;
            CreateFrameAndPointer();
            CreateChime();
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            outputRoot = GetArgument(args, "-presence-output") ?? Path.Combine(Application.persistentDataPath, "Studies");
            leftHanded = args.Contains("-presence-left");
            if (int.TryParse(GetArgument(args, "-presence-participant"), out int id))
                participant = Mathf.Clamp(id, 1, PresenceProtocol.Participants);

            string smoke = GetArgument(args, "-presence-smoke");
            if (smoke != null)
            {
                automated = true;
                StartCoroutine(RunSmoke(smoke));
                return;
            }

            if (args.Contains("-presence-protocol"))
                StartSession(true);
            else
            {
                LoadTrial(new PresenceTrial(practiceTechnique, practiceScenario, 0, practiceLayout, 723, true));
                stage = Stage.Menu;
                if (args.Contains("-presence-preview"))
                    StartSession(false);
            }
        }

        private void CreateFrameAndPointer()
        {
            framePaint = new Material(pointerMaterial);
            framePaint.color = new Color(0.78f, 0.91f, 1f);
            for (int i = 0; i < frame.Length; ++i)
                frame[i] = PresenceWorld.Shape(PrimitiveType.Cube, "Portal frame", entrance.transform.parent,
                    Vector3.zero, Vector3.one, framePaint).transform;

            pointerPaint = new Material(pointerMaterial);
            line = new GameObject("Placement arc").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = pointerPaint;
            line.widthMultiplier = 0.018f;
            line.numCapVertices = 3;
            line.enabled = false;

            ghost = new GameObject("Exit preview");
            ghost.transform.SetParent(transform, false);
            PresenceWorld.Shape(PrimitiveType.Cylinder, "Landing disc", ghost.transform, Vector3.zero,
                new Vector3(0.44f, 0.013f, 0.44f), pointerPaint);
            PresenceWorld.Shape(PrimitiveType.Cube, "Preview mast", ghost.transform, Vector3.up * 0.74f,
                new Vector3(0.018f, 1.46f, 0.018f), pointerPaint);
            for (int i = 0; i < 4; ++i)
            {
                bool horizontal = i < 2;
                Vector3 position = Vector3.up * 1.42f;
                position += (horizontal ? Vector3.up : Vector3.right) * (i % 2 == 0 ? 0.15f : -0.15f);
                PresenceWorld.Shape(PrimitiveType.Cube, "Preview frame", ghost.transform, position,
                    horizontal ? new Vector3(0.32f, 0.015f, 0.018f) : new Vector3(0.015f, 0.30f, 0.018f),
                    pointerPaint);
            }
            ghost.SetActive(false);
        }

        private void CreateChime()
        {
            chime = gameObject.AddComponent<AudioSource>();
            chime.playOnAwake = false;
            const int rate = 22050;
            var samples = new float[rate / 3];
            for (int i = 0; i < samples.Length; ++i)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Min(t * 100f, 1f) * Mathf.Exp(-9f * t);
                samples[i] = envelope * 0.25f * (Mathf.Sin(2f * Mathf.PI * 740f * t) +
                    0.55f * Mathf.Sin(2f * Mathf.PI * 988f * t));
            }
            chimeClip = AudioClip.Create("Reaction chime", samples.Length, 1, rate, false);
            chimeClip.SetData(samples, 0);
            chime.clip = chimeClip;
        }

        private void StartSession(bool measured)
        {
            protocol = measured;
            plan = protocol ? PresenceProtocol.Trials(participant) : null;
            string name = "P" + participant.ToString("00") + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            recorder = new PresenceRecorder(Path.Combine(outputRoot, name), participant, protocol);
            trialIndex = 0;
            LoadTrial(protocol ? plan[trialIndex] :
                new PresenceTrial(practiceTechnique, practiceScenario, 0, practiceLayout, 723, true));
        }

        private void LoadTrial(PresenceTrial next)
        {
            trial = next;
            layout = PresenceLayout.Create(trial.Scenario, trial.Layout);
            world.Build(layout, trial.Seed);
            orbs.Clear();
            heldOrb = reactionOrb = null;
            input.Recenter(view.transform);
            destination = layout.Centre(layout.Start);
            exitYaw = layout.Yaw;
            entrance.transform.parent.SetPositionAndRotation(new Vector3(0f, 1.41f, 0.57f), Quaternion.Euler(0, 180f, 0));
            UpdateExitPose();
            stretched = false;
            FitAperture(false);
            overlay.Technique = trial.Technique;
            overlay.ResetState();
            changedExit = false;
            HidePointer();
            feedback = "";
            stage = Stage.Ready;
        }

        private void BeginTrial(double time)
        {
            result = new PresenceResult
            {
                participant = participant,
                trial = protocol ? (trial.Practice ? trialIndex + 1 : MeasuredIndex) : 1,
                technique = trial.Technique.ToString(),
                scenario = trial.Scenario.ToString(),
                block = trial.Block,
                layout = trial.Layout,
                seed = trial.Seed,
                input = input.UsingHeadset ? "OpenXR controller" : "desktop",
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                automated = automated
            };
            started = lastMotion = time;
            firstGrab = pausedSeconds = cueGrabbed = overlaySeconds = headAimSeconds = 0;
            backwards = previousProgress = 0f;
            backwardsCounted = false;
            ScheduleReaction(time);
            stage = Stage.Navigation;
        }

        private void ScheduleReaction(double time)
        {
            var random = new Random(trial.Seed ^ (197 + result.reactions * 723));
            nextReaction = time + 5.0 + random.NextDouble() * 4.0;
        }

        private void Update()
        {
            if (automated) return;

            input.Read(view.transform, stage != Stage.Menu, leftHanded);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (stage == Stage.Menu)
                    Application.Quit();
                else
                {
                    stage = Stage.Menu;
                    HidePointer();
                    overlay.ResetState();
                }
                return;
            }

            if (!protocol) ReadPracticeKeys();
            if (input.Advance) Advance();
            if (stage == Stage.Menu)
            {
                if (input.UsingHeadset) UpdateHeadsetStatus();
                return;
            }

            if (input.UsingHeadset && input.Held)
            {
                entrance.transform.parent.SetPositionAndRotation(input.PortalPosition, input.PortalRotation);
                exit.transform.position = new Vector3(exit.transform.position.x, input.PortalPosition.y,
                    exit.transform.position.z);
            }

            double time = Now;
            bool pointing = input.Aim && (stage == Stage.Navigation || stage == Stage.Reaction);
            bool compare = stage == Stage.Compare;
            FitAperture(pointing);
            if (Mathf.Abs(input.Turn) > 0.001f)
                TurnExit(input.Turn, time);
            if (input.ReleasedAim && validTarget)
                TryMoveExit(target, time);
            if (pointing)
            {
                Ray aim = input.Ray;
                if (input.UsingHeadset)
                {
                    Vector3 forward = -entrance.transform.forward;
                    aim = new Ray(entrance.transform.position - forward * 0.12f, forward + Vector3.down * 0.12f);
                }
                UpdatePointer(aim);
            }
            else
                HidePointer();

            if (input.Select)
            {
                if (pointing && validTarget)
                    TryMoveExit(target, time);
                else if (!pointing)
                    Select(input.Ray, time);
            }
            if (heldOrb)
            {
                heldOrb.transform.position = input.Ray.GetPoint(input.UsingHeadset ? 0.16f : 0.44f)
                    - Vector3.up * 0.075f + view.transform.right * 0.075f;
            }

            TickTrial(time, input.Held, pointing || (compare && input.Aim), Time.unscaledDeltaTime);
            if (input.UsingHeadset) UpdateHeadsetStatus();
        }

        private void ReadPracticeKeys()
        {
            for (int i = 0; i < 5; ++i)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                    SetPracticeTechnique((OverlayTechnique)i);
            }

            if (Input.GetKeyDown(KeyCode.M))
            {
                practiceScenario = practiceScenario == PresenceScenario.Maze ? PresenceScenario.Obstacles : PresenceScenario.Maze;
                if (stage == Stage.Compare) StartComparison();
                else StartSession(false);
            }

            if (Input.GetKeyDown(KeyCode.R) && stage != Stage.Menu)
            {
                if (stage == Stage.Compare) StartComparison();
                else StartSession(false);
            }
        }

        private void StartComparison()
        {
            protocol = false;
            LoadTrial(new PresenceTrial(practiceTechnique, practiceScenario, 0, practiceLayout, 723, true));
            stage = Stage.Compare;
        }

        private void SetPracticeTechnique(OverlayTechnique technique)
        {
            practiceTechnique = technique;
            if (stage == Stage.Menu) return;
            trial = new PresenceTrial(technique, trial.Scenario, trial.Block, trial.Layout, trial.Seed, true);
            overlay.Technique = technique;
            overlay.ResetState();
            if (result != null) result.technique = technique.ToString();
        }

        private void Advance()
        {
            if (stage == Stage.Menu)
                StartSession(protocol);
            else if (stage == Stage.Ready)
                BeginTrial(Now);
            else if (stage == Stage.Survey)
                NextTrial();
            else if (stage == Stage.Complete)
                stage = Stage.Menu;
        }

        private void FitAperture(bool extend)
        {
            stretched = extend;
            float height = stretched ? 0.52f : 0.30f;
            float down = -(height - 0.30f) * 0.5f;
            entrance.transform.localPosition = new Vector3(0f, down, 0f);
            entrance.transform.localScale = new Vector3(0.30f, height, 1f);
            for (int i = 0; i < 4; ++i)
            {
                bool horizontal = i < 2;
                float sign = i % 2 == 0 ? 1f : -1f;
                frame[i].localPosition = horizontal ? new Vector3(0f, down + sign * height * 0.5f, 0f) :
                    new Vector3(sign * 0.15f, down, 0f);
                frame[i].localScale = horizontal ? new Vector3(0.315f, 0.010f, 0.008f) :
                    new Vector3(0.010f, height, 0.008f);
            }
        }

        private void UpdateExitPose()
        {
            Quaternion rotation = Quaternion.Euler(0f, exitYaw, 0f);
            Vector3 handOffset = new Vector3(0f, entrance.transform.parent.position.y, 0.57f);
            exit.transform.SetPositionAndRotation(destination + rotation * handOffset, rotation);
        }

        private void TurnExit(float degrees, double time)
        {
            if (stage != Stage.Navigation && stage != Stage.Compare) return;
            exitYaw = Mathf.Repeat(exitYaw + degrees, 360f);
            UpdateExitPose();
            changedExit = true;
            lastMotion = time;
        }

        private bool CanLand(Vector3 position)
        {
            if (!layout.Contains(position)) return false;
            Vector3 movement = position - destination;
            movement.y = 0f;
            if (movement.sqrMagnitude < 0.025f || movement.sqrMagnitude > 4.4f * 4.4f)
                return false;
            Vector3 origin = destination + Vector3.up * 0.43f;
            Vector3 end = position + Vector3.up * 0.43f;
            return !UnityEngine.Physics.CheckSphere(end, 0.14f, PresenceWorld.MovementMask, QueryTriggerInteraction.Ignore) &&
                   !UnityEngine.Physics.SphereCast(origin, 0.11f, movement.normalized, out _, movement.magnitude,
                       PresenceWorld.MovementMask, QueryTriggerInteraction.Ignore);
        }

        private bool TryMoveExit(Vector3 position, double time)
        {
            position.y = 0f;
            if (stage != Stage.Navigation || !CanLand(position)) return false;
            destination = position;
            UpdateExitPose();
            changedExit = true;
            lastMotion = time;
            ++result.placements;
            if (layout.Route != null)
            {
                float progress = layout.RouteProgress(destination);
                float delta = progress - previousProgress;
                if (delta < -0.03f) backwards -= delta;
                if (delta > 0.08f) { backwards = 0f; backwardsCounted = false; }
                if (backwards >= 1f && !backwardsCounted)
                {
                    ++result.directionalErrors;
                    backwardsCounted = true;
                }
                previousProgress = progress;
            }
            return true;
        }

        private bool ThroughAperture(Ray ray, out Ray secondary)
        {
            secondary = default;
            Transform window = entrance.transform;
            var plane = new Plane(window.forward, window.position);
            if (Vector3.Dot(ray.direction, window.forward) >= 0f || !plane.Raycast(ray, out float t)) return false;
            Vector3 point = ray.GetPoint(t);
            Vector3 local = window.InverseTransformPoint(point);
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
            Matrix4x4 map = entrance.portal.teleportMatrix;
            Vector3 direction = map.MultiplyVector(ray.direction).normalized;
            secondary = new Ray(map.MultiplyPoint3x4(point) + direction * 0.017f, direction);
            return true;
        }

        private void UpdatePointer(Ray ray)
        {
            validTarget = FindLanding(ray);
            line.enabled = points > 1;
            line.positionCount = points;
            for (int i = 0; i < points; ++i)
                line.SetPosition(i, trajectory[i]);
            pointerPaint.color = stage == Stage.Reaction ? new Color(1f, 0.18f, 0.24f) :
                validTarget ? new Color(0.54f, 1.0f, 0.87f) : new Color(1f, 0.65f, 0.26f);
            ghost.SetActive(validTarget);
            if (validTarget)
                ghost.transform.SetPositionAndRotation(target, Quaternion.Euler(0, exitYaw, 0));
        }

        private bool FindLanding(Ray ray)
        {
            points = 0;
            if (!ThroughAperture(ray, out Ray secondary)) return false;
            Vector3 start = secondary.origin;
            trajectory[points++] = start;
            for (int i = 1; i < trajectory.Length; ++i)
            {
                float t = i * 0.053f;
                Vector3 next = start + secondary.direction * (5.4f * t) + Vector3.down * (4.9f * t * t);
                Vector3 prior = trajectory[points - 1];
                if (UnityEngine.Physics.Linecast(prior, next, out RaycastHit hit, PresenceWorld.PointerMask,
                    QueryTriggerInteraction.Ignore))
                {
                    trajectory[points++] = hit.point;
                    target = hit.point;
                    target.y = 0f;
                    return stage == Stage.Navigation && hit.collider.gameObject.layer == PresenceWorld.FloorLayer &&
                        CanLand(target);
                }
                trajectory[points++] = next;
            }
            return false;
        }

        private void HidePointer()
        {
            validTarget = false;
            points = 0;
            line.enabled = false;
            ghost.SetActive(false);
        }

        private void TickTrial(double time, bool held, bool intent, float delta)
        {
            overlay.Tick(held, intent, changedExit, delta);
            changedExit = false;
            if (IsRunning)
            {
                if (overlay.IsActive) overlaySeconds += delta;
                if (ThroughAperture(new Ray(view.transform.position, view.transform.forward), out _))
                    headAimSeconds += delta;
            }

            if (stage != Stage.Navigation) return;
            bool atGoal = Vector3.Distance(destination, layout.Centre(layout.Goal)) <= 0.60f;
            if (result.reactions < 2 && (time >= nextReaction || atGoal))
                StartReaction(time);
            else if (atGoal)
                BeginSorting();
        }

        private void StartReaction(double time)
        {
            var random = new Random(trial.Seed ^ (2719 + result.reactions * 1543));
            int number = random.Next(1, 9);
            reactionOrb = world.Orb(number, new Vector3(0f, 1.12f, 0.56f), true);
            cueStarted = time;
            cueGrabbed = 0;
            stage = Stage.Reaction;
            validTarget = false;
            chime.Play();
        }

        private void BeginSorting()
        {
            FitAperture(false);
            HidePointer();
            destination = layout.Centre(layout.Goal);
            UpdateExitPose();
            int[] order = {1, 2, 3, 4, 5, 6, 7, 8};
            PresenceProtocol.Shuffle(order, new Random(trial.Seed ^ 731));
            Matrix4x4 map = entrance.portal.teleportMatrix;
            for (int i = 0; i < 4; ++i)
            {
                Vector3 behindAperture = entrance.transform.TransformPoint(
                    new Vector3(i % 2 == 0 ? -0.24f : 0.24f, i < 2 ? 0.20f : -0.23f, -0.060f));
                orbs.Add(world.Orb(order[i], map.MultiplyPoint3x4(behindAperture), false));
            }
            stage = Stage.Sorting;
        }

        private void Select(Ray ray, double time)
        {
            if (stage != Stage.Reaction && stage != Stage.Sorting) return;
            UnityEngine.Physics.SyncTransforms();
            if (heldOrb)
            {
                if (UnityEngine.Physics.Raycast(ray, out RaycastHit drop, 3f, 1 << PresenceWorld.TargetLayer,
                    QueryTriggerInteraction.Ignore) && drop.collider.TryGetComponent(out PresenceBin bin))
                    Drop(bin.Number, time);
                else
                    ShowFeedback("Aim at a numbered cup to place the orb.", time);
                return;
            }

            bool primary = stage == Stage.Reaction;
            Ray reach = ray;
            if (!primary && !ThroughAperture(ray, out reach))
            {
                ShowFeedback("Pick up the sorting orbs through the portal window.", time);
                return;
            }

            int mask = primary ? 1 << PresenceWorld.TargetLayer : PresenceWorld.PointerMask | (1 << PresenceWorld.OrbLayer);
            if (UnityEngine.Physics.Raycast(reach, out RaycastHit pick, primary ? 2.5f : 0.76f, mask,
                QueryTriggerInteraction.Ignore) && pick.collider.TryGetComponent(out PresenceOrb orb))
                Grab(orb, time);
        }

        private void Grab(PresenceOrb orb, double time)
        {
            heldOrb = orb;
            if (stage == Stage.Reaction && cueGrabbed == 0) cueGrabbed = time;
            if (stage == Stage.Sorting)
            {
                if (firstGrab == 0) firstGrab = time;
                exit.Teleport(orb.transform);
            }
            orb.transform.SetParent(transform, true);
            orb.GetComponent<Collider>().enabled = false;
        }

        private void Drop(int number, double time)
        {
            PresenceOrb orb = heldOrb;
            heldOrb = null;
            bool reaction = stage == Stage.Reaction;
            if (number != orb.Number)
            {
                if (reaction) ++result.reactionErrors;
                else ++result.sortingErrors;
                orb.ReturnHome();
                ShowFeedback("Wrong cup. The orb returned to its starting position.", time);
                return;
            }

            orb.gameObject.SetActive(false);
            Destroy(orb.gameObject);
            if (reaction)
            {
                int index = result.reactions++;
                reactionOrb = null;
                reactionEnd[index] = time;
                result.reactionSelectSeconds[index] = cueGrabbed - cueStarted;
                result.reactionPlaceSeconds[index] = time - cueGrabbed;
                pausedSeconds += time - cueStarted;
                stage = Stage.Navigation;
                ScheduleReaction(time);
                ShowFeedback("Reaction complete. Continue navigating.", time);
            }
            else
            {
                ++result.sorted;
                if (result.sorted == 4) CompleteTrial(time);
                else ShowFeedback("Placed " + result.sorted + " of 4 sorting orbs.", time);
            }
        }

        private void CompleteTrial(double time)
        {
            result.totalSeconds = time - started;
            result.navigationSeconds = Math.Max(0, lastMotion - started - ReactionBefore(lastMotion));
            result.sortingSelectSeconds = Math.Max(0, firstGrab - lastMotion - (pausedSeconds - ReactionBefore(lastMotion)));
            result.sortingPlaceSeconds = time - firstGrab;
            result.overlayMeasured = trial.Technique != OverlayTechnique.NoOverlay;
            result.overlayPercent = result.overlayMeasured ? Percent(overlaySeconds, result.totalSeconds) : 0;
            result.headAimPercent = Percent(headAimSeconds, result.totalSeconds);
            overlay.ResetState();
            HidePointer();
            if (!trial.Practice || !protocol) recorder.Write(result);

            if (!protocol)
            {
                stage = Stage.Complete;
                return;
            }
            int measured = MeasuredIndex;
            if (!trial.Practice && measured % 6 == 0)
                stage = Stage.Survey;
            else
                NextTrial();
        }

        private double ReactionBefore(double time)
        {
            double seconds = 0;
            for (int i = 0; i < result.reactions; ++i)
            {
                if (reactionEnd[i] <= time)
                    seconds += result.reactionSelectSeconds[i] + result.reactionPlaceSeconds[i];
            }
            return seconds;
        }

        private readonly double[] reactionEnd = new double[2];

        private void NextTrial()
        {
            ++trialIndex;
            if (trialIndex >= plan.Count)
                stage = Stage.Complete;
            else
                LoadTrial(plan[trialIndex]);
        }

        private static double Percent(double duration, double total) => total > 0 ? Math.Min(100, 100 * duration / total) : 0;

        private void ShowFeedback(string text, double time)
        {
            feedback = text;
            feedbackUntil = time + 3.1;
        }

        private static string GetArgument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
        }

        private void OnDestroy()
        {
            world?.Dispose();
            if (chimeClip) Destroy(chimeClip);
            if (framePaint) Destroy(framePaint);
            if (pointerPaint) Destroy(pointerPaint);
            if (panelTexture) Destroy(panelTexture);
            if (headsetStatus) Destroy(headsetStatus);
        }
    }
}
