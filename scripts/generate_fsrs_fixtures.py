"""Generate independent reference vectors with official py-fsrs==6.3.1.

Development only: python -m pip install fsrs==6.3.1
Then: python scripts/generate_fsrs_fixtures.py
Optional --site-packages PATH supports an isolated --target installation.
The released Windows application does not require Python.
"""
from __future__ import annotations

import argparse
import importlib.metadata
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--site-packages")
args = parser.parse_args()
if args.site_packages:
    sys.path.insert(0, args.site_packages)

from fsrs import Card, Rating, Scheduler, State

assert importlib.metadata.version("fsrs") == "6.3.1", "Use exactly fsrs==6.3.1"
PARAMETERS = [
    0.2172, 1.1771, 3.2602, 16.1507, 7.0114, 0.57, 2.0966,
    0.0069, 1.5261, 0.112, 1.0178, 1.849, 0.1133, 0.3127,
    2.2934, 0.2191, 3.0004, 0.7536, 0.3332, 0.1437, 0.2,
]
BASE = datetime(2026, 1, 1, 8, tzinfo=timezone.utc)
CARD_ID = "00000000-0000-0000-0000-000000000001"


def scheduler(maximum_interval=36500):
    return Scheduler(parameters=PARAMETERS, desired_retention=0.9,
                     learning_steps=(timedelta(minutes=1), timedelta(minutes=10)),
                     relearning_steps=(timedelta(minutes=10),),
                     maximum_interval=maximum_interval, enable_fuzzing=False)


def timestamp(value):
    return value.isoformat().replace("+00:00", "Z") if value else None


def snapshot(card):
    return dict(cardId=CARD_ID, state=int(card.state), step=card.step,
                stability=card.stability, difficulty=card.difficulty,
                dueAt=timestamp(card.due), lastReviewAt=timestamp(card.last_review))


cases = []


def append(name, card, rating, now, maximum_interval=36500):
    updated, _ = scheduler(maximum_interval).review_card(card, rating, now)
    cases.append(dict(name=name, maximumInterval=maximum_interval,
                      rating=int(rating), reviewedAt=timestamp(now),
                      before=snapshot(card), after=snapshot(updated)))
    return updated


for rating in Rating:
    append(f"new-{rating.name}", Card(card_id=1, due=BASE), rating, BASE)
    for state, step, stability, difficulty, elapsed in [
        (State.Learning, 0, 2.0, 5.0, timedelta(minutes=1)),
        (State.Learning, 1, 2.0, 5.0, timedelta(days=1, hours=8)),
        (State.Review, None, 18.0, 5.0, timedelta(days=30, hours=12)),
        (State.Review, None, 18.0, 5.0, timedelta(minutes=30)),
        (State.Relearning, 0, 1.5, 7.0, timedelta(minutes=10)),
        (State.Relearning, 0, 1.5, 7.0, timedelta(days=45, hours=20)),
    ]:
        card = Card(card_id=1, state=state, step=step, stability=stability,
                    difficulty=difficulty, due=BASE, last_review=BASE)
        append(f"{state.name}-step{step}-{elapsed}-{rating.name}", card, rating, BASE + elapsed)

# Explicitly catches the upstream C# TotalDays vs py-fsrs whole-day difference.
for elapsed in [timedelta(days=1), timedelta(days=1, hours=23, minutes=59), timedelta(days=3650)]:
    for rating in Rating:
        card = Card(card_id=1, state=State.Review, stability=30, difficulty=6,
                    due=BASE, last_review=BASE)
        append(f"elapsed-{elapsed}-{rating.name}", card, rating, BASE + elapsed)

for rating in Rating:
    card = Card(card_id=1, state=State.Review, stability=10000, difficulty=2,
                due=BASE, last_review=BASE)
    append(f"maximum-interval-{rating.name}", card, rating, BASE + timedelta(days=100), 30)

card = Card(card_id=1, due=BASE)
now = BASE
for index, rating in enumerate([
    Rating.Again, Rating.Hard, Rating.Good, Rating.Good, Rating.Easy,
    Rating.Again, Rating.Hard, Rating.Good, Rating.Good, Rating.Again,
    Rating.Easy, Rating.Good, Rating.Hard, Rating.Easy,
]):
    if index:
        now = card.due + (timedelta(hours=13) if index > 3 else timedelta())
    card = append(f"sequence-{index}-{rating.name}", card, rating, now)

result = dict(generator="official py-fsrs", version="6.3.1", parameters=PARAMETERS,
              desiredRetention=0.9, learningStepsSeconds=[60, 600],
              relearningStepsSeconds=[600], enableFuzzing=False,
              floatAbsoluteTolerance=1e-10, floatRelativeTolerance=1e-10,
              timestampToleranceMilliseconds=1, cases=cases)
destination = Path(__file__).resolve().parents[1] / "tests/Wording.Tests/Fixtures/fsrs-reference.json"
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Generated {len(cases)} cases with official py-fsrs 6.3.1: {destination}")
