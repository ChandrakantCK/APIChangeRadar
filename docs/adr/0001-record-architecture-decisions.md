# 0001. Record architecture decisions

- Status: Accepted
- Date: 2026-10-29

## Context

API Change Radar is built step by step, often months apart, and is also a learning project. Decisions that seem obvious today (why a proxy? why not store tokens in localStorage?) are easy to forget or to undo by accident later.

## Decision

Keep Architecture Decision Records in `docs/adr/`, one Markdown file per significant decision, numbered in order, using the template in [README.md](README.md). Write one whenever a choice is costly to reverse, security-relevant, or surprising.

## Alternatives considered

- **Comments in code** — good for *how*, poor for *why we didn't do X*; scattered.
- **A wiki** — drifts away from the code and isn't reviewed in pull requests.

## Consequences

- Decisions are reviewed in PRs like code and live next to it.
- New contributors (and future me) can read the reasoning instead of rediscovering it.
- Small cost: a page of writing per big decision.
