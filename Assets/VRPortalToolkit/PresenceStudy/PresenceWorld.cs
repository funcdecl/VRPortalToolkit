using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace VRPortalToolkit.Presence
{
    public sealed class PresenceWorld
    {
        public const int WallLayer = 9;
        public const int ObstacleLayer = 10;
        public const int FloorLayer = 11;
        public const int OrbLayer = 12;
        public const int TargetLayer = 13;
        public const int MovementMask = (1 << WallLayer) | (1 << ObstacleLayer);
        public const int PointerMask = MovementMask | (1 << FloorLayer);
        private static readonly Color[] DirectionColors =
        {
            new Color32(55, 93, 156, 255), new Color32(180, 150, 75, 255),
            new Color32(142, 67, 75, 255), new Color32(52, 111, 99, 255)
        };
        private static readonly Color[] OrbColors =
        {
            new Color32(242, 104, 110, 255), new Color32(255, 160, 89, 255),
            new Color32(232, 218, 100, 255), new Color32(139, 212, 113, 255),
            new Color32(76, 212, 204, 255), new Color32(99, 158, 242, 255),
            new Color32(165, 126, 245, 255), new Color32(235, 123, 197, 255)
        };

        private readonly Material floor, primary, trim, marker, label;
        private readonly Font font;
        private readonly Material[] walls = new Material[4];
        private readonly Material[] props = new Material[4];
        private readonly Material[] colors = new Material[8];
        private readonly Material[] grids = new Material[2];
        private readonly List<Material> materials = new List<Material>();
        private readonly Texture2D checker;
        private GameObject root;
        private Transform primaryRoot, secondaryRoot;
        private readonly Transform[] bins = new Transform[8];
        public Camera Eye { get; set; }
        public Portal Entrance { get; set; }

        public PresenceWorld(Material surface, Material labelMaterial)
        {
            label = new Material(labelMaterial);
            materials.Add(label);
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            font.RequestCharactersInTexture("0123456789FINISH", 92);
            label.mainTexture = font.material.mainTexture;
            primary = Make(surface, new Color32(119, 67, 91, 255), "Berry wall");
            floor = Make(surface, new Color32(130, 146, 162, 255), "Floor");
            trim = Make(surface, new Color32(37, 50, 69, 255), "Trim");
            marker = Make(surface, new Color32(159, 253, 215, 255), "Goal marker");
            marker.EnableKeyword("_EMISSION");
            marker.SetColor("_EmissionColor", new Color(0.12f, 0.50f, 0.29f));
            for (int i = 0; i < 4; ++i)
            {
                walls[i] = Make(surface, DirectionColors[i], "Directional wall " + i);
                props[i] = Make(surface, Color.Lerp(new Color32(67, 82, 101, 255),
                    new Color32(160, 171, 183, 255), i / 3f), "Obstacle " + i);
            }

            for (int i = 0; i < 8; ++i)
            {
                colors[i] = Make(surface, OrbColors[i], "Orb " + (i + 1));
                colors[i].EnableKeyword("_EMISSION");
                colors[i].SetColor("_EmissionColor", OrbColors[i] * 0.22f);
            }

            checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var light = new Color(0.95f, 0.96f, 0.99f);
            var dark = new Color(0.85f, 0.88f, 0.92f);
            checker.SetPixels(new[] { light, dark, dark, light });
            checker.Apply();
            for (int i = 0; i < grids.Length; ++i)
            {
                grids[i] = Make(floor, floor.color, "Grid");
                grids[i].SetTexture("_BaseMap", checker);
            }
        }

        private Material Make(Material source, Color color, string name)
        {
            var material = new Material(source) { name = name, color = color };
            materials.Add(material);
            return material;
        }

        public void Dispose()
        {
            if (root) Object.Destroy(root);
            foreach (Material material in materials) Object.Destroy(material);
            Object.Destroy(checker);
        }

        public void Build(PresenceLayout layout, int seed)
        {
            if (root)
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
            root = new GameObject("Trial worlds");
            primaryRoot = Child("Primary", root.transform);
            secondaryRoot = Child(layout.Scenario.ToString(), root.transform);
            BuildPrimary(seed);
            BuildSecondary(layout);
            UnityEngine.Physics.SyncTransforms();
        }

        private void BuildPrimary(int seed)
        {
            var random = new Random(seed);
            Floor(primaryRoot, Vector3.zero, 9, 1f, grids[0], 0);
            Enclose(primaryRoot, Vector3.zero, 9, 3.1f, 0, true);
            for (int z = -4; z <= 4; ++z)
            {
                for (int x = -4; x <= 4; ++x)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 3 || random.NextDouble() > 0.46)
                        continue;
                    Obstacle(primaryRoot, new Vector3(x, 0f, z), random, 0);
                }
            }

            int[] order = {0, 1, 2, 3, 4, 5, 6, 7};
            PresenceProtocol.Shuffle(order, random);
            for (int i = 0; i < 8; ++i)
            {
                float angle = i < 4 ? -108f + i * 24f : 36f + (i - 4) * 24f;
                float height = i % 2 == 0 ? 1.15f : 0.91f;
                Vector3 position = Quaternion.Euler(0, angle, 0) * new Vector3(0f, height, 1.20f);
                Bin(primaryRoot, position, order[i] + 1);
            }
        }

        private void BuildSecondary(PresenceLayout layout)
        {
            float span = layout.Size * layout.CellSize;
            Floor(secondaryRoot, PresenceLayout.Origin, layout.Size, layout.CellSize, grids[1], FloorLayer);
            if (layout.Scenario == PresenceScenario.Maze)
            {
                foreach (Vector2Int cell in layout.Cells)
                {
                    for (int side = 0; side < 4; ++side)
                    {
                        Vector2Int direction = PresenceLayout.Directions[side];
                        Vector2Int neighbour = cell + direction;
                        if (layout.Open(cell, neighbour))
                            continue;
                        float offset = layout.Cells.Contains(neighbour) ? 0.035f : 0f;
                        Vector3 position = layout.Centre(cell) +
                            new Vector3(direction.x, 0f, direction.y) * (layout.CellSize * 0.5f - offset);
                        Wall(secondaryRoot, position, (side + 2) % 4, layout.CellSize + 0.10f,
                            2.6f, WallLayer, walls[(side + 2) % 4]);
                    }
                }
            }
            else
            {
                Enclose(secondaryRoot, PresenceLayout.Origin, span, 3.7f, WallLayer, false);
                var random = new Random(271 + layout.Start.x * 10017 + layout.Goal.y * 997);
                for (int z = 1; z < 19; ++z)
                {
                    for (int x = 1; x < 19; ++x)
                    {
                        var cell = new Vector2Int(x, z);
                        if (Vector2Int.Distance(cell, layout.Start) <= 1.6f ||
                            Vector2Int.Distance(cell, layout.Goal) <= 1.6f || random.NextDouble() > 0.30)
                            continue;
                        Obstacle(secondaryRoot, layout.Centre(cell), random, ObstacleLayer);
                    }
                }
            }

            Vector3 goal = layout.Centre(layout.Goal);
            Shape(PrimitiveType.Cylinder, "Goal pad", secondaryRoot, goal + Vector3.up * 0.027f,
                new Vector3(0.86f, 0.023f, 0.86f), marker);
            Shape(PrimitiveType.Cylinder, "Goal beacon", secondaryRoot, goal + Vector3.up * 1.05f,
                new Vector3(0.075f, 1.05f, 0.075f), marker);
            Label(secondaryRoot, "FINISH", goal + Vector3.up * 2.12f, 0.040f);
        }

        private void Floor(Transform parent, Vector3 centre, int size, float cellSize, Material material, int layer)
        {
            material.SetTextureScale("_BaseMap", Vector2.one * (size * 0.5f));
            float span = size * cellSize;
            Shape(PrimitiveType.Cube, "Grid floor", parent, centre + Vector3.down * 0.10f,
                new Vector3(span, 0.20f, span), material, layer);
        }

        private void Enclose(Transform parent, Vector3 centre, float size, float height, int layer, bool main)
        {
            for (int side = 0; side < 4; ++side)
            {
                Vector2Int direction = PresenceLayout.Directions[side];
                Vector3 position = centre + new Vector3(direction.x, 0f, direction.y) * size * 0.5f;
                int face = (side + 2) % 4;
                Wall(parent, position, face, size + 0.15f, height, layer, main ? primary : walls[face]);
            }
        }

        private void Wall(Transform parent, Vector3 centre, int face, float span, float height, int layer,
            Material material)
        {
            Quaternion rotation = Quaternion.Euler(0, face * 90, 0);
            GameObject wall = Shape(PrimitiveType.Cube, "Wall", parent, centre + Vector3.up * (height * 0.5f),
                new Vector3(span, height, 0.12f), material, layer);
            wall.transform.rotation = rotation;
            GameObject baseboard = Shape(PrimitiveType.Cube, "Baseboard", parent,
                centre + Vector3.up * 0.11f, new Vector3(span, 0.22f, 0.143f), trim);
            baseboard.transform.rotation = rotation;
        }

        private void Obstacle(Transform parent, Vector3 position, Random random, int layer)
        {
            position += new Vector3((float)random.NextDouble() * 0.5f - 0.25f, 0,
                (float)random.NextDouble() * 0.5f - 0.25f);
            int kind = random.Next(4);
            Quaternion rotation = Quaternion.Euler(0, random.Next(360), 0);
            if (kind == 0 || kind == 3)
            {
                float height = kind == 0 ? 2.45f : 0.82f;
                GameObject cylinder = Shape(PrimitiveType.Cylinder, kind == 0 ? "Column" : "Barrel", parent,
                    position + Vector3.up * (height * 0.5f), new Vector3(0.55f, height * 0.5f, 0.55f),
                    props[kind], layer);
                cylinder.transform.rotation = rotation;
                if (kind == 3)
                    Shape(PrimitiveType.Cylinder, "Barrel band", parent, position + Vector3.up * 0.63f,
                        new Vector3(0.57f, 0.038f, 0.57f), trim);
                return;
            }
            GameObject box = Shape(PrimitiveType.Cube, "Crate", parent, position + Vector3.up * 0.29f,
                new Vector3(0.62f, 0.58f, 0.55f), props[kind], layer);
            box.transform.rotation = rotation;
            if (kind == 2)
            {
                box = Shape(PrimitiveType.Cube, "Stacked crate", parent, position + Vector3.up * 0.79f,
                    new Vector3(0.49f, 0.42f, 0.48f), props[3], layer);
                box.transform.rotation = rotation * Quaternion.Euler(0, 15f, 0);
            }
        }

        private void Bin(Transform parent, Vector3 position, int number)
        {
            Transform stand = Child("Bin " + number, parent);
            stand.position = position;
            bins[number - 1] = stand;
            Shape(PrimitiveType.Cylinder, "Stand", stand, new Vector3(position.x, position.y * 0.5f, position.z), new Vector3(0.11f, position.y * 0.5f, 0.11f), trim);
            Shape(PrimitiveType.Cylinder, "Cup", stand, position, new Vector3(0.34f, 0.065f, 0.34f), colors[number - 1]);
            Shape(PrimitiveType.Cylinder, "Recess", stand, position + Vector3.up * 0.065f,
                new Vector3(0.25f, 0.012f, 0.25f), trim);
            GameObject target = new GameObject("Drop target " + number);
            target.transform.SetParent(stand, false);
            target.transform.localPosition = Vector3.up * 0.16f;
            target.layer = TargetLayer;
            target.AddComponent<BoxCollider>().size = new Vector3(0.34f, 0.31f, 0.34f);
            target.AddComponent<PresenceBin>().Number = number;
            Label(stand, number.ToString(), position + Vector3.up * 0.19f, 0.020f);
        }

        public PresenceOrb Orb(int number, Vector3 position, bool primaryWorld)
        {
            GameObject orb = Shape(PrimitiveType.Sphere, "Orb " + number,
                primaryWorld ? primaryRoot : secondaryRoot, position, Vector3.one * 0.063f,
                colors[number - 1], primaryWorld ? TargetLayer : OrbLayer);
            var item = orb.AddComponent<PresenceOrb>();
            item.Number = number;
            item.Home = position;
            item.HomeParent = orb.transform.parent;
            item.HomeLayer = orb.layer;
            Label(orb.transform, number.ToString(), position, 0.006f, 0.035f);
            return item;
        }

        public Transform BinAt(int number) => bins[number - 1];

        private TextMesh Label(Transform parent, string text, Vector3 position, float size, float offset = 0f)
        {
            Transform label = Child("Label " + text, parent);
            label.position = position;
            // Undo sphere scaling so the character size remains in metres.
            if (parent.GetComponent<PresenceOrb>()) label.localScale = Vector3.one / parent.lossyScale.x;
            TextMesh mesh = label.gameObject.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.text = text;
            mesh.color = Color.white;
            mesh.characterSize = size;
            mesh.fontSize = 92;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            label.GetComponent<MeshRenderer>().sharedMaterial = this.label;
            var facing = label.gameObject.AddComponent<PresenceLabel>();
            facing.Offset = offset;
            facing.Eye = Eye;
            facing.Entrance = Entrance;
            return mesh;
        }

        private static Transform Child(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        internal static GameObject Shape(PrimitiveType type, string name, Transform parent,
            Vector3 position, Vector3 scale, Material material, int layer = -1)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            shape.transform.SetParent(parent, false);
            shape.transform.position = position;
            shape.transform.localScale = scale;
            shape.GetComponent<Renderer>().sharedMaterial = material;
            if (layer < 0)
                shape.GetComponent<Collider>().enabled = false;
            else
                shape.layer = layer;
            return shape;
        }
    }
}
