using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace VRPortalToolkit.Presence
{
    public sealed class PresenceLayout
    {
        public static readonly Vector3 Origin = new Vector3(100f, 0f, 0f);
        public static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        public readonly struct Branch
        {
            public readonly Vector2Int From;
            public readonly Vector2Int To;

            public Branch(Vector2Int from, Vector2Int to)
            {
                From = from;
                To = to;
            }
        }

        public readonly PresenceScenario Scenario;
        public readonly int Size;
        public readonly float CellSize;
        public readonly Vector2Int Start;
        public readonly Vector2Int Goal;
        public readonly float Yaw;
        public readonly List<Vector2Int> Route;
        public readonly Branch[] Branches;
        public readonly HashSet<Vector2Int> Cells;

        private PresenceLayout(PresenceScenario scenario, int size, float cellSize, Vector2Int start,
            Vector2Int goal, float yaw, List<Vector2Int> route = null, Branch[] branches = null)
        {
            Scenario = scenario;
            Size = size;
            CellSize = cellSize;
            Start = start;
            Goal = goal;
            Yaw = yaw;
            Route = route;
            Branches = branches;
            if (route != null)
            {
                Cells = new HashSet<Vector2Int>(route);
                foreach (Branch branch in branches)
                    Cells.Add(branch.To);
            }
        }

        public static PresenceLayout Create(PresenceScenario scenario, int variant)
        {
            var random = new Random(1091 + variant * 907);
            return scenario == PresenceScenario.Maze ? CreateMaze(random) : CreateObstacles(random);
        }

        private static PresenceLayout CreateMaze(Random random)
        {
            for (int attempt = 0; attempt < 12000; ++attempt)
            {
                var start = new Vector2Int(random.Next(7), random.Next(7));
                var route = new List<Vector2Int> { start };
                var visited = new HashSet<Vector2Int> { start };
                int direction = random.Next(4);
                int initialDirection = direction;
                bool valid = true;
                for (int run = 0; run < 8 && valid; ++run)
                {
                    int length = random.Next(1, 5);
                    for (int step = 0; step < length; ++step)
                    {
                        Vector2Int next = route[route.Count - 1] + Directions[direction];
                        if (!InBounds(next, 7) || !visited.Add(next))
                        {
                            valid = false;
                            break;
                        }
                        route.Add(next);
                    }
                    direction = (direction + (random.Next(2) == 0 ? 1 : 3)) % 4;
                }
                if (!valid || route.Count < 17 || route.Count > 24)
                    continue;

                var candidates = new List<Branch>();
                for (int i = 2; i < route.Count - 2; ++i)
                {
                    foreach (Vector2Int offset in Directions)
                    {
                        Vector2Int branch = route[i] + offset;
                        if (InBounds(branch, 7) && !visited.Contains(branch))
                            candidates.Add(new Branch(route[i], branch));
                    }
                }
                Branch[] choices = candidates.ToArray();
                PresenceProtocol.Shuffle(choices, random);
                for (int i = 0; i < choices.Length; ++i)
                {
                    for (int j = i + 1; j < choices.Length; ++j)
                    {
                        if (choices[i].To == choices[j].To ||
                            Mathf.Abs(route.IndexOf(choices[i].From) - route.IndexOf(choices[j].From)) < 4)
                            continue;
                        return new PresenceLayout(PresenceScenario.Maze, 7, 1.5f, start, route[route.Count - 1],
                            initialDirection * 90f, route, new[] { choices[i], choices[j] });
                    }
                }
            }
            throw new InvalidOperationException("Maze seed could not generate seven bends and two dead ends.");
        }

        private static PresenceLayout CreateObstacles(Random random)
        {
            while (true)
            {
                var start = new Vector2Int(random.Next(3, 17), random.Next(3, 17));
                var goal = new Vector2Int(random.Next(1, 19), random.Next(1, 19));
                Vector2Int delta = goal - start;
                if (delta.magnitude < 14f || delta.magnitude > 16f)
                    continue;

                float yaw = Mathf.Atan2(-delta.x, -delta.y) * Mathf.Rad2Deg + random.Next(-45, 46);
                return new PresenceLayout(PresenceScenario.Obstacles, 20, 1f, start, goal, yaw);
            }
        }

        public Vector3 Centre(Vector2Int cell)
        {
            float half = Size * 0.5f;
            return Origin + new Vector3((cell.x + 0.5f - half) * CellSize, 0f,
                (cell.y + 0.5f - half) * CellSize);
        }

        public Vector2Int CellAt(Vector3 point)
        {
            Vector3 local = (point - Origin) / CellSize;
            return new Vector2Int(Mathf.FloorToInt(local.x + Size * 0.5f),
                Mathf.FloorToInt(local.z + Size * 0.5f));
        }

        public bool Contains(Vector3 point)
        {
            Vector2Int cell = CellAt(point);
            return InBounds(cell, Size) && (Cells == null || Cells.Contains(cell));
        }

        public bool Open(Vector2Int cell, Vector2Int adjacent)
        {
            if (Route == null)
                return InBounds(adjacent, Size);

            int i = Route.IndexOf(cell), j = Route.IndexOf(adjacent);
            if (i >= 0 && j >= 0 && Mathf.Abs(i - j) == 1)
                return true;
            foreach (Branch branch in Branches)
                if ((branch.From == cell && branch.To == adjacent) ||
                    (branch.To == cell && branch.From == adjacent))
                    return true;
            return false;
        }

        public float RouteProgress(Vector3 point)
        {
            float distance = float.PositiveInfinity, progress = 0f;
            for (int i = 0; i + 1 < Route.Count; ++i)
            {
                Vector3 a = Centre(Route[i]);
                Vector3 segment = Centre(Route[i + 1]) - a;
                float t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / segment.sqrMagnitude);
                float sqrDistance = (a + segment * t - point).sqrMagnitude;
                if (sqrDistance < distance)
                {
                    distance = sqrDistance;
                    progress = (i + t) * CellSize;
                }
            }
            return progress;
        }

        private static bool InBounds(Vector2Int cell, int size)
        {
            return cell.x >= 0 && cell.x < size && cell.y >= 0 && cell.y < size;
        }
    }
}
