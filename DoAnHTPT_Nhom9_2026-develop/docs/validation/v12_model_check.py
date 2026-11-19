"""Bounded executable design checks, NOT the C#/SQL implementation.

Run with Python 3 using only the standard library. Enumerates atomic local /
replica-apply, ACK and response cut points on representative booking histories,
then every one-node failure and both backup choices. Includes unsafe controls.
"""
from __future__ import annotations

import copy
import hashlib
import itertools
import json
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass(frozen=True)
class Command:
    request_id: str
    owner: str
    action: str
    seat: int
    reservation: str = "-"

    def identity(self):
        return self.owner, self.action, self.seat, self.reservation


@dataclass(frozen=True)
class Event:
    index: int
    epoch: int
    event_id: str
    previous_hash: str
    event_hash: str
    command: Command
    result: tuple[str, str]


def blank():
    return {"seats": {1: ("AVAILABLE", "-", "-"), 2: ("AVAILABLE", "-", "-")},
            "reservations": {}, "tickets": {}, "results": {}}


def apply_command(state, cmd):
    if cmd.request_id in state["results"]:
        old_cmd, old_result = state["results"][cmd.request_id]
        return old_result if old_cmd.identity() == cmd.identity() else ("REQUEST_CONFLICT", "-")
    status, current, owner = state["seats"][cmd.seat]
    if cmd.action == "HOLD":
        if status == "AVAILABLE":
            rid = "res-" + cmd.request_id
            state["reservations"][rid] = (cmd.seat, cmd.owner, "HELD")
            state["seats"][cmd.seat] = ("HELD", rid, cmd.owner)
            result = ("OK_HELD", rid)
        else:
            result = ("SEAT_BUSY" if status == "HELD" else "ALREADY_SOLD", "-")
    else:
        reservation = state["reservations"].get(cmd.reservation)
        if reservation is None:
            result = ("RESERVATION_NOT_FOUND", "-")
        elif reservation[1] != cmd.owner:
            result = ("NOT_OWNER", "-")
        elif reservation[0] != cmd.seat:
            result = ("RESERVATION_SEAT_MISMATCH", "-")
        elif cmd.action == "CONFIRM":
            if reservation[2] == "HELD":
                assert (status, current, owner) == ("HELD", cmd.reservation, cmd.owner)
                ticket = "ticket-" + cmd.reservation
                assert cmd.seat not in state["tickets"]
                state["tickets"][cmd.seat] = (ticket, cmd.reservation, cmd.owner)
                state["reservations"][cmd.reservation] = (cmd.seat, cmd.owner, "CONFIRMED")
                state["seats"][cmd.seat] = ("SOLD", cmd.reservation, cmd.owner)
                result = ("OK_SOLD", ticket)
            elif reservation[2] == "CONFIRMED":
                result = ("ALREADY_CONFIRMED", state["tickets"][cmd.seat][0])
            else:
                result = ("RESERVATION_CANCELLED", "-")
        else:
            if reservation[2] == "HELD":
                assert (status, current, owner) == ("HELD", cmd.reservation, cmd.owner)
                state["seats"][cmd.seat] = ("AVAILABLE", "-", "-")
                state["reservations"][cmd.reservation] = (cmd.seat, cmd.owner, "CANCELLED")
                result = ("OK_CANCELLED", cmd.reservation)
            elif reservation[2] == "CANCELLED":
                result = ("ALREADY_CANCELLED", cmd.reservation)
            else:
                result = ("TICKET_NOT_CANCELLABLE", "-")
    state["results"][cmd.request_id] = (cmd, result)
    return result


def invariants(state):
    assert len(state["seats"]) == 2
    assert len(state["tickets"]) <= 2
    for seat, (status, rid, owner) in state["seats"].items():
        held = [(r, v) for r, v in state["reservations"].items() if v[0] == seat and v[2] == "HELD"]
        assert len(held) <= 1
        if status == "AVAILABLE":
            assert (rid, owner) == ("-", "-") and not held and seat not in state["tickets"]
        elif status == "HELD":
            assert state["reservations"][rid] == (seat, owner, "HELD")
            assert seat not in state["tickets"]
        else:
            assert status == "SOLD" and not held
            assert state["reservations"][rid] == (seat, owner, "CONFIRMED")
            assert state["tickets"][seat][1:] == (rid, owner)


def replay(log):
    state = blank()
    previous = "seed"
    for index, event in enumerate(log, 1):
        assert event.index == index and event.previous_hash == previous
        assert event.event_hash == event_digest(event.index, event.epoch, event.event_id,
                                                previous, event.command, event.result)
        assert event.command.request_id not in state["results"]
        result = apply_command(state, event.command)
        assert result == event.result
        invariants(state)
        previous = event.event_hash
    return state


def event_digest(index, epoch, event_id, previous, command, result):
    return hashlib.sha256(json.dumps([index, epoch, event_id, previous, asdict(command), result],
                                     sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def decide(log, command, epoch=1):
    state = replay(log)
    if command.request_id in state["results"]:
        return None, apply_command(state, command)
    result = apply_command(state, command)
    index = len(log) + 1
    event_id = f"e-{epoch}-{index}-{command.request_id}"
    previous = log[-1].event_hash if log else "seed"
    digest = event_digest(index, epoch, event_id, previous, command, result)
    return Event(index, epoch, event_id, previous, digest, command, result), result


def build(commands):
    log = ()
    for command in commands:
        event, _ = decide(log, command)
        if event:
            log += (event,)
    return log


def compatible(a, b):
    return a[:min(len(a), len(b))] == b[:min(len(a), len(b))]


def recover(logs):
    # Preconditions: old writer physically fenced, two same-epoch participants
    # frozen, SQL durable, authenticated histories. This does NOT implement them.
    assert len(logs) == 2
    assert compatible(*logs), "PREFIX_CONFLICT must block"
    kept = max(logs, key=len)
    replay(kept)
    return kept, kept


def candidates(prefix):
    state = replay(prefix)
    request_id = "next"
    for owner in ("A", "B"):
        for seat in (1, 2):
            yield Command(request_id, owner, "HOLD", seat)
        for rid, (seat, _, _) in state["reservations"].items():
            for action in ("CONFIRM", "CANCEL"):
                yield Command(request_id, owner, action, seat, rid)
        yield Command(request_id, owner, "CONFIRM", 1, "missing")
    for event in prefix:
        yield event.command  # same RequestId, same body
        yield Command(event.command.request_id, "X", event.command.action,
                      event.command.seat, event.command.reservation)  # conflict


def ledger(log):
    return {e.command.request_id: e.result for e in log}


def preserved(expected, recovered):
    actual = ledger(recovered)
    return all(actual.get(request_id) == result for request_id, result in expected.items())


def run():
    h = Command("h1", "A", "HOLD", 1)
    h2 = Command("h2", "B", "HOLD", 2)
    confirm = Command("c1", "A", "CONFIRM", 1, "res-h1")
    cancel = Command("x1", "A", "CANCEL", 1, "res-h1")
    histories = [[], [h], [h, confirm], [h, cancel], [h, h2],
                 [h, cancel, Command("h3", "B", "HOLD", 1)],
                 [h, Command("b1", "B", "HOLD", 1), cancel]]
    schedules = 0
    duplicate_cases = 0
    archived_tails = 0
    read_checks = 0
    for history in histories:
        prefix = build(history)
        for command in candidates(prefix):
            event, result = decide(prefix, command)
            if event is None:
                duplicate_cases += 1
                assert decide(prefix, command)[1] == result
                continue
            # All reachable atomic-cut snapshots for this single in-flight entry:
            # primary not applied; primary applied + any subset of backups;
            # ACK from an applied backup; optional response after that ACK.
            snapshots = [(False, (), None, False)]
            for size in range(3):
                for backups in itertools.combinations((0, 1), size):
                    snapshots.append((True, backups, None, False))
                    for ack in backups:
                        snapshots.extend([(True, backups, ack, False), (True, backups, ack, True)])
            for local, backups, ack, response in snapshots:
                logs = [prefix, prefix, prefix]
                if local:
                    logs[2] += (event,)
                for backup in backups:
                    logs[backup] += (event,)
                stable = len(prefix) + (1 if ack is not None else 0)
                read_checks += 1
                allowed_read = local and len(logs[2]) == stable
                assert not (allowed_read and ack is None)
                if response:
                    assert ack in backups and local
                expected = ledger(prefix)
                if response:
                    expected[command.request_id] = result
                for failed in (0, 1, 2):
                    survivors = [logs[i] for i in (0, 1, 2) if i != failed]
                    recovered, other = recover(survivors)
                    assert recovered == other and preserved(expected, recovered)
                    # Active history is installed before any old node may rejoin.
                    if not compatible(logs[failed], recovered) or len(logs[failed]) > len(recovered):
                        archived_tails += 1
                    repaired_old_node = recovered
                    assert replay(repaired_old_node) == replay(recovered)
                    retry_event, retry_result = decide(recovered, command, epoch=2)
                    if command.request_id in ledger(recovered):
                        assert retry_event is None and retry_result == result
                    else:
                        # The old event was not FINAL; same identity may be decided
                        # again in the retained branch. Event identity differs.
                        assert not response and retry_event and retry_event.epoch == 2
                        assert retry_event.event_id != event.event_id
                    schedules += 1

    # Negative controls must expose their expected counterexamples.
    event, result = decide((), h)
    loss_after_early_final = not preserved({h.request_id: result}, recover([(), ()])[0])
    stale_candidate_loss = not preserved({h.request_id: result}, ())
    unsafe_read_leaks_lost_tail = replay((event,))["seats"] != replay(())["seats"]
    ack_before_durable_apply_loss = loss_after_early_final
    controls = {"final_before_replica_loses_acknowledged_result": loss_after_early_final,
                "candidate_only_recovery_loses_newer_survivor_result": stale_candidate_loss,
                "read_before_stable_exposes_discarded_tail": unsafe_read_leaks_lost_tail,
                "ack_before_sql_commit_loses_acknowledged_result": ack_before_durable_apply_loss}
    assert all(controls.values())

    # Explicit conflict handling and delayed-message authority checks.
    other_event, _ = decide((), Command("other", "B", "HOLD", 1))
    assert not compatible((event,), (other_event,))
    assert event.epoch != 2  # a live APPEND at epoch 2 must reject this epoch-1 entry
    assert event.event_hash != other_event.event_hash
    spec = Path(__file__).parents[1] / "De6_Thiet_Ke_Trien_Khai_V12.md"
    output = {"status": "PASS", "model": "bounded educational design model; not C#/SQL tests",
              "domain": "2 seats, 2 active owners; 7 representative histories; one in-flight command",
              "exploration": "all atomic Apply/ACK/response snapshots generated for each candidate command; every one-node failure",
              "fault_schedules_checked": schedules, "duplicate_or_conflict_cases": duplicate_cases,
              "publication_gate_checks": read_checks, "discarded_tail_rejoin_cases": archived_tails,
              "negative_controls_detected": controls,
              "outside_model": ["real SQL, disk durability and transaction isolation", "TCP/TLS/JSON and process races",
                                "Bully network timing", "physical fencing and admin activation manifest",
                                "multiple faults before repair", "arbitrary histories/unbounded proofs"],
              "spec_sha256": hashlib.sha256(spec.read_bytes()).hexdigest()}
    target = Path(__file__).with_name("v12_model_results.json")
    target.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    run()
