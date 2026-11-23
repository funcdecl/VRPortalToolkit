using System;
using System.Collections.Generic;

namespace VRPortalToolkit.Presence
{
    public enum PresenceScenario { Maze, Obstacles }

    public readonly struct PresenceTrial
    {
        public readonly OverlayTechnique Technique;
        public readonly PresenceScenario Scenario;
        public readonly int Block;
        public readonly int Layout;
        public readonly int Seed;
        public readonly bool Practice;

        public PresenceTrial(OverlayTechnique technique, PresenceScenario scenario, int block, int layout,
            int seed, bool practice)
        {
            Technique = technique;
            Scenario = scenario;
            Block = block;
            Layout = layout;
            Seed = seed;
            Practice = practice;
        }
    }

    public static class PresenceProtocol
    {
        public const int Participants = 25;
        public static readonly string[] TechniqueNames =
            { "No overlay", "Contours", "Blended 33.3%", "Absolute", "Blended stencil" };

        public static List<PresenceTrial> Trials(int participant)
        {
            if (participant < 1 || participant > Participants)
                throw new ArgumentOutOfRangeException(nameof(participant));

            var order = new List<OverlayTechnique>
            {
                OverlayTechnique.NoOverlay, OverlayTechnique.Contours,
                OverlayTechnique.Blended, OverlayTechnique.Absolute
            };
            var random = new Random(3419 + participant * 59);
            var permutations = new[] { 6, 2, 1, 1 };
            int orderIndex = participant <= 24 ? participant - 1 : random.Next(24);
            var techniques = new OverlayTechnique[4];
            for (int i = 0; i < techniques.Length; ++i)
            {
                int next = orderIndex / permutations[i];
                orderIndex %= permutations[i];
                techniques[i] = order[next];
                order.RemoveAt(next);
            }

            int[] mazes = new int[16];
            for (int i = 0; i < mazes.Length; ++i)
                mazes[i] = i;
            Shuffle(mazes, random);

            // Twelve start in the maze and thirteen in the obstacle room.
            bool mazeFirst = (participant * 7 % 25) < 12;
            var result = new List<PresenceTrial>(32);
            foreach (OverlayTechnique technique in techniques)
            {
                for (int i = 0; i < 2; ++i)
                    result.Add(new PresenceTrial(technique, ScenarioAt(i, mazeFirst), 0, 16, random.Next(), true));
            }

            int layout = 0;
            foreach (OverlayTechnique technique in techniques)
            {
                for (int block = 1; block <= 3; ++block)
                {
                    for (int i = 0; i < 2; ++i)
                    {
                        PresenceScenario scenario = ScenarioAt(i, mazeFirst);
                        result.Add(new PresenceTrial(technique, scenario, block,
                            scenario == PresenceScenario.Maze ? mazes[layout] : layout, random.Next(), false));
                    }
                    ++layout;
                }
            }
            return result;
        }

        private static PresenceScenario ScenarioAt(int index, bool mazeFirst)
        {
            return (index == 0) == mazeFirst ? PresenceScenario.Maze : PresenceScenario.Obstacles;
        }

        public static void Shuffle<T>(T[] values, Random random)
        {
            for (int i = values.Length - 1; i > 0; --i)
            {
                int j = random.Next(i + 1);
                T value = values[i];
                values[i] = values[j];
                values[j] = value;
            }
        }
    }
}
