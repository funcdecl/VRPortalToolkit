#!/usr/bin/env python3
"""Summarize measured trial rows and optional, scored questionnaires."""

import argparse
import csv
import json
import math
from collections import defaultdict
from pathlib import Path
from statistics import mean, stdev


TECHNIQUES = ("NoOverlay", "Contours", "Blended", "Absolute")
TRIAL_METRICS = (
    "navigationSeconds", "sortingSelectSeconds", "sortingPlaceSeconds",
    "sortingErrors", "reactionErrors", "directionalErrors", "headAimPercent",
)
PRESENCE_FIELDS = ("primary_general", "primary_spatial", "secondary_general", "secondary_spatial")
SURVEY_FIELDS = (
    "realism", "involvement", "tlx_effort", "tlx_frustration", "tlx_mental",
    "tlx_performance", "tlx_physical", "tlx_temporal", "sus", "ssq_nausea",
    "ssq_oculomotor", "ssq_disorientation", "ssq_total",
)


def records(directory, automated):
    with (directory / "trials.jsonl").open(encoding="utf-8") as file:
        for line in file:
            row = json.loads(line)
            if row["block"] < 1 or row["automated"] != automated:
                continue
            if row["sorted"] != 4 or row["reactions"] != 2:
                raise ValueError(f"Incomplete trial {row['trial']} in {directory}")
            yield row


def optional_scores(path):
    if not path.exists():
        return
    with path.open(encoding="utf-8-sig", newline="") as file:
        yield from csv.DictReader(file)


def score(row, field):
    text = (row.get(field) or "").strip()
    if not text:
        return None
    value = float(text)
    if not math.isfinite(value):
        raise ValueError(f"Invalid {field} for participant {row['participant']}")
    return value


def write_rows(path, header, rows):
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.writer(file)
        writer.writerow(header)
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("sessions", nargs="+", type=Path, help="Folders containing trials.jsonl")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--automated", action="store_true", help="Summarize software verification records")
    args = parser.parse_args()
    metrics = defaultdict(list)
    surveys = defaultdict(list)
    continuum = []
    completed = defaultdict(set)
    seen = set()

    for session in args.sessions:
        rows = list(records(session, args.automated))
        for row in rows:
            technique, scenario = row["technique"], row["scenario"]
            person = (row["input"], row["participant"])
            key = (*person, technique, scenario, row["block"])
            if key in seen:
                raise ValueError(f"Duplicate participant, condition and block: {key}")
            seen.add(key)
            completed[person].add((technique, scenario, row["block"]))
            group = (row["input"], technique, scenario)
            for field in TRIAL_METRICS:
                if field == "directionalErrors" and scenario != "Maze":
                    continue
                metrics[(*group, field)].append(row[field])
            for field in ("reactionSelectSeconds", "reactionPlaceSeconds"):
                metrics[(*group, field)].extend(row[field])
            if row["overlayMeasured"]:
                metrics[(*group, "overlayPercent")].append(row["overlayPercent"])

        if args.automated or not rows:
            continue
        input_type = rows[0]["input"]
        expected_by_person = {str(r["participant"]) for r in rows}
        for row in optional_scores(session / "questionnaires.csv"):
            if row["participant"] not in expected_by_person or row["technique"] not in TECHNIQUES:
                raise ValueError(f"Questionnaire row does not match session {session}")
            technique = row["technique"]
            for field in (*PRESENCE_FIELDS, *SURVEY_FIELDS):
                value = score(row, field)
                if value is not None:
                    surveys[(input_type, technique, field)].append(value)

            values = [score(row, field) for field in PRESENCE_FIELDS]
            if all(value is not None for value in values):
                primary = (values[0] + values[1]) * 0.5
                secondary = (values[2] + values[3]) * 0.5
                continuum.append((input_type, row["participant"], technique, primary, secondary))

        for row in optional_scores(session / "preference.csv"):
            values = [score(row, name) for name in TECHNIQUES]
            if any(value is not None for value in values):
                if sorted(value for value in values if value is not None) != [1, 2, 3, 4]:
                    raise ValueError(f"Preference ranks must be a permutation from 1 to 4 in {session}")
                for technique, value in zip(TECHNIQUES, values):
                    surveys[(input_type, technique, "preference_rank")].append(value)

    if not metrics:
        parser.error("No completed recorded trials matched the selected data source.")
    args.out.mkdir(parents=True, exist_ok=True)
    data_source = "software_verification" if args.automated else "participant_session"

    def summaries(table):
        return [(*key, len(values), round(mean(values), 6),
                 round(stdev(values), 6) if len(values) > 1 else "", round(sum(values), 6))
                for key, values in sorted(table.items())]

    write_rows(args.out / "trial-summary.csv",
               ("source", "input", "technique", "scenario", "measure", "n", "mean", "sd", "sum"),
               [(data_source, *row) for row in summaries(metrics)])
    write_rows(args.out / "session-coverage.csv",
               ("source", "input", "participant", "completed_trials", "planned_trials"),
               [(data_source, *person, len(conditions), 24) for person, conditions in sorted(completed.items())])
    if not args.automated:
        write_rows(args.out / "questionnaire-summary.csv",
                   ("input", "technique", "measure", "n", "mean", "sd", "sum"), summaries(surveys))
        write_rows(args.out / "sp-continuum.csv",
                   ("input", "participant", "technique", "primary_presence", "secondary_presence"), continuum)
    print(f"{data_source}: {len(seen)} recorded trials, {len(continuum)} scored presence pairs")


if __name__ == "__main__":
    main()
