using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRPortalToolkit.Presence
{
    [Serializable]
    public sealed class PresenceResult
    {
        public int participant;
        public int trial;
        public string input;
        public string technique;
        public string scenario;
        public int block;
        public int layout;
        public int seed;
        public string graphicsDevice;
        public string graphicsApi;
        public bool automated;
        public double totalSeconds;
        public double navigationSeconds;
        public double sortingSelectSeconds;
        public double sortingPlaceSeconds;
        public double[] reactionSelectSeconds = new double[2];
        public double[] reactionPlaceSeconds = new double[2];
        public int sortingErrors;
        public int reactionErrors;
        public int directionalErrors;
        public int placements;
        public int sorted;
        public int reactions;
        public double overlayPercent;
        public bool overlayMeasured;
        public double headAimPercent;
    }

    public sealed class PresenceRecorder
    {
        private readonly string directory;
        public string DirectoryPath => directory;

        public PresenceRecorder(string output, int participant, bool protocol)
        {
            directory = output;
            Directory.CreateDirectory(directory);
            if (protocol)
            {
                var survey = new StringBuilder(
                    "participant,technique,primary_general,primary_spatial,secondary_general,secondary_spatial,realism,involvement,tlx_effort,tlx_frustration,tlx_mental,tlx_performance,tlx_physical,tlx_temporal,sus,ssq_nausea,ssq_oculomotor,ssq_disorientation,ssq_total\n");
                foreach (OverlayTechnique technique in new[] { OverlayTechnique.NoOverlay,
                    OverlayTechnique.Contours, OverlayTechnique.Blended, OverlayTechnique.Absolute })
                    survey.Append(participant).Append(',').Append(technique).Append(",,,,,,,,,,,,,,,,,\n");
                File.WriteAllText(Path.Combine(directory, "questionnaires.csv"), survey.ToString());
                File.WriteAllText(Path.Combine(directory, "preference.csv"),
                    "participant,NoOverlay,Contours,Blended,Absolute\n" + participant + ",,,,\n");
            }
        }

        public void Write(PresenceResult result)
        {
            File.AppendAllText(Path.Combine(directory, "trials.jsonl"), JsonUtility.ToJson(result) + "\n");
        }
    }
}
