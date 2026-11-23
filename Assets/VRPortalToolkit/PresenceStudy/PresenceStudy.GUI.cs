using UnityEngine;

namespace VRPortalToolkit.Presence
{
    public sealed partial class PresenceStudy
    {
        private GUIStyle titleStyle;
        private GUIStyle largeStyle;
        private GUIStyle labelStyle;
        private GUIStyle dimStyle;
        private GUIStyle badgeStyle;
        private Texture2D panelTexture;
        private TextMesh headsetText;

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint)
                PrepareGui();
            if (labelStyle == null) return;

            Matrix4x4 matrix = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 800f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            if (stage == Stage.Menu)
                DrawMenu(width, height);
            else
                DrawStudy(width, height);
            GUI.matrix = matrix;
        }

        private void PrepareGui()
        {
            if (labelStyle != null) return;
            var white = new Color32(233, 242, 246, 255);
            var dim = new Color32(166, 190, 207, 255);
            var mint = new Color32(152, 241, 211, 255);
            labelStyle = Style(15, white);
            dimStyle = Style(13, dim);
            badgeStyle = Style(13, mint, FontStyle.Bold);
            titleStyle = Style(24, white, FontStyle.Bold);
            largeStyle = Style(34, white, FontStyle.Bold);
            panelTexture = new Texture2D(1, 1);
            panelTexture.SetPixel(0, 0, new Color(0.035f, 0.065f, 0.102f, 0.93f));
            panelTexture.Apply();
        }

        private static GUIStyle Style(int size, Color color, FontStyle font = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = font, wordWrap = true };
            style.normal.textColor = color;
            return style;
        }

        private void DrawMenu(float width, float height)
        {
            const float cardWidth = 564, cardHeight = 452;
            float x = (width - cardWidth) * 0.5f, y = (height - cardHeight) * 0.5f;
            GUI.DrawTexture(new Rect(x, y, cardWidth, cardHeight), panelTexture);
            GUI.Label(new Rect(x + 30, y + 21, 510, 24), "VR PORTAL TOOLKIT", badgeStyle);
            GUI.Label(new Rect(x + 30, y + 48, 520, 52), "Simultaneous Presence", largeStyle);
            GUI.Label(new Rect(x + 30, y + 102, 504, 49),
                "Navigate through a handheld portal, then sort numbered orbs in the primary room.", labelStyle);

            GUI.Label(new Rect(x + 30, y + 156, 504, 24), "PRACTICE CONDITIONS", badgeStyle);
            var content = new[] { "No overlay", "Contours", "Blended", "Absolute", "Stencil" };
            practiceTechnique = (OverlayTechnique)GUI.SelectionGrid(new Rect(x + 30, y + 186, 504, 34),
                (int)practiceTechnique, content, content.Length);
            practiceScenario = (PresenceScenario)GUI.SelectionGrid(new Rect(x + 30, y + 230, 504, 32),
                (int)practiceScenario, new[] { "Maze", "Obstacles" }, 2);

            if (GUI.Button(new Rect(x + 30, y + 276, 246, 42), "Compare overlays"))
                StartComparison();
            if (GUI.Button(new Rect(x + 288, y + 276, 246, 42), "Practice trial"))
                StartSession(false);

            GUI.Label(new Rect(x + 30, y + 342, 145, 26), "Participant number", labelStyle);
            if (GUI.Button(new Rect(x + 179, y + 340, 34, 30), "−"))
                participant = Mathf.Max(1, participant - 1);
            GUI.Label(new Rect(x + 219, y + 342, 40, 26), participant.ToString("00"), labelStyle);
            if (GUI.Button(new Rect(x + 257, y + 340, 34, 30), "+"))
                participant = Mathf.Min(PresenceProtocol.Participants, participant + 1);
            if (GUI.Button(new Rect(x + 310, y + 340, 224, 36), "Start study protocol"))
                StartSession(true);
            GUI.Label(new Rect(x + 30, y + 398, 505, 40),
                "Protocol: 8 practice trials followed by 24 recorded trials. Compare mode supports all five render modes.", dimStyle);
        }

        private void DrawStudy(float width, float height)
        {
            const float x = 25, y = 24;
            bool cue = stage == Stage.Reaction;
            bool compare = stage == Stage.Compare;
            float panelHeight = compare ? 173 : 195;
            GUI.DrawTexture(new Rect(x, y, 348, panelHeight), panelTexture);
            string prefix = protocol ? (trial.Practice ? "TRAINING  " + (trialIndex + 1) + " / 8" :
                "RECORDED TRIAL  " + MeasuredIndex + " / 24") : compare ? "OVERLAY COMPARISON" : "PRACTICE";
            GUI.Label(new Rect(x + 15, y + 12, 314, 22), prefix, badgeStyle);
            GUI.Label(new Rect(x + 15, y + 38, 321, 36), PresenceProtocol.TechniqueNames[(int)trial.Technique], titleStyle);
            GUI.Label(new Rect(x + 15, y + 76, 319, 24),
                trial.Scenario + (protocol && !trial.Practice ? "   ·   Block " + trial.Block : ""), dimStyle);
            Color old = GUI.color;
            if (cue) GUI.color = new Color(1f, 0.68f, 0.67f);
            GUI.Label(new Rect(x + 15, y + 109, 318, 54), TaskText(), labelStyle);
            GUI.color = old;
            if (IsRunning)
                GUI.Label(new Rect(x + 15, y + 165, 316, 20),
                    "Reactions  " + result.reactions + "/2    ·    Sorted  " + result.sorted + "/4", dimStyle);

            if (stage == Stage.Ready || stage == Stage.Survey || stage == Stage.Complete)
            {
                float boxX = (width - 500f) * 0.5f, boxY = height - 246f;
                GUI.DrawTexture(new Rect(boxX, boxY, 500, 145), panelTexture);
                string description = stage == Stage.Ready
                    ? "Navigate the portal to the mint beacon. Sort each orb into the cup with the same number."
                    : stage == Stage.Survey
                        ? "Record the questionnaire scores for this technique in questionnaires.csv."
                        : protocol ? "All 24 trials saved. Record the final ranking in preference.csv."
                                   : "Trial saved. Press R to repeat or Esc to return to the menu.";
                GUI.Label(new Rect(boxX + 20, boxY + 16, 460, 57), description, labelStyle);
                if (GUI.Button(new Rect(boxX + 20, boxY + 78, 460, 37),
                    stage == Stage.Ready ? "Begin trial   ·   Enter" : stage == Stage.Survey ? "Continue   ·   Enter" : "Return to menu"))
                    Advance();
            }

            if (compare || IsRunning)
            {
                Color crosshairColor = cue ? new Color(1f, 0.25f, 0.3f) :
                    validTarget ? new Color(0.6f, 1f, 0.87f) : new Color(0.92f, 0.97f, 1f, 0.85f);
                GUI.color = crosshairColor;
                GUI.DrawTexture(new Rect(width * 0.5f - 8, height * 0.5f - 1, 16, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(width * 0.5f - 1, height * 0.5f - 8, 2, 16), Texture2D.whiteTexture);
                GUI.color = old;
            }

            GUI.DrawTexture(new Rect(25, height - 65, width - 50, 43), panelTexture);
            string keys = compare
                ? "1–5  Overlay mode     Space  Reveal     Q / E  Turn exit     Arrows / right drag  Look     F  Hold / release     Esc  Menu"
                : "Arrows / right drag  Look     Space  Aim, release to place     Q / E  Turn exit     Click / G  Grab or drop     Home  Face portal";
            GUI.Label(new Rect(40, height - 55, width - 80, 29), keys, dimStyle);
            if (feedbackUntil > Now && IsRunning && !string.IsNullOrEmpty(feedback))
            {
                GUI.DrawTexture(new Rect(25, height - 111, 503, 34), panelTexture);
                GUI.Label(new Rect(37, height - 105, 489, 25), feedback, labelStyle);
            }
        }

        private string TaskText()
        {
            switch (stage)
            {
                case Stage.Compare:
                    return overlay.IsActive ? "Overlay active. Release Space to let it retract." :
                        "Hold Space to reveal the secondary room.";
                case Stage.Ready: return "Ready to start";
                case Stage.Navigation: return "Find the mint beacon. Move the portal's exit with the placement arc.";
                case Stage.Reaction: return heldOrb ? "Reaction: place orb " + heldOrb.Number + " in its cup." :
                    "Reaction: grab the orb in the primary room. Exit placement is paused.";
                case Stage.Sorting: return heldOrb ? "Place orb " + heldOrb.Number + " in its cup." :
                    "Reach through the portal and pick up a numbered orb.";
                case Stage.Survey: return "Technique complete";
                case Stage.Complete: return "Session complete";
                default: return "";
            }
        }

        private void UpdateHeadsetStatus()
        {
            if (!headsetText)
            {
                headsetStatus = new GameObject("Study prompt");
                headsetText = headsetStatus.AddComponent<TextMesh>();
                headsetText.fontSize = 72;
                headsetText.characterSize = 0.017f;
                headsetText.anchor = TextAnchor.MiddleCenter;
                headsetText.alignment = TextAlignment.Center;
                headsetText.color = new Color(0.74f, 0.95f, 0.90f);
            }
            headsetText.text = stage == Stage.Menu ? "Primary button: start practice" :
                stage == Stage.Ready ? "Primary button: begin trial" : stage == Stage.Survey ?
                    "Record questionnaire scores\nPrimary button: continue" : TaskText();
            Transform eye = view.transform;
            headsetStatus.transform.SetPositionAndRotation(eye.position + eye.forward * 1.15f + eye.up * 0.30f,
                eye.rotation);
        }
    }
}
