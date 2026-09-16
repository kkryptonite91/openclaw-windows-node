# Archive: Tail Navigation During Unstable Realization Layout

Root-cause and acceptance archive for the chat timeline tail-navigation defect.

Status: CLOSED (production fix merged)

## Root cause

ROOT_CAUSE_CLASS = TAIL_NAVIGATION_DURING_UNSTABLE_REALIZATION_LAYOUT

## Commit map

| Role | Commit | Subject |
|------|--------|---------|
| Control | 811798a21dd3b8898b0c9d52bbb27077edc5c876 | fix(chat): isolate exec history presentation identity |
| Decisive diagnostic | b7346b5e4d24865a81baf5b3ff54de5ed8d9a994 | diag: disable initial tail bring into view (E1 remove StartBringItemIntoView -> survive) |
| Production fix (code) | d75e77556c58164530827b1c1d341976fe303361 | fix: guard tail navigation on realized layout |
| Production fix (tip) | 42deb04c60c12a09ece7ea28d8e43020964b51ba | test: fix tail navigation source-contract boundary after P1 rename |
| Row-key fix (held out) | fb68c81ed335a51bf3664916dcc73f1b1b6385e8 | fix: separate standalone tool and activity row keys |

## Frozen fixture

FROZEN_FIXTURE_MD5 = cf56d0a1a2e0880cde12cc91b1b7ad06

## CI

FINAL_CI = 35018639547 PASS

## Acceptance

P1_ACCEPTANCE = ACCEPT

- CI 35018639547 = PASS
- P1 frozen-runtime = PASS
- INITIAL_AUTO_TAIL = PASS
- MANUAL_SCROLL_HOLD = PASS
- SCROLL_BOUNCE = NO
- CRASH = NO
- AUTOMATED_SMOKE = PASS
- NEW_CRASH_LOG_ENTRY = NO

## Merge

Production fix merged into main as merge commit 8312b16fb742b33b35b3e215ff103016aa1b0dd6
(first parent bd9ce43b4d9c63fa90196a6a33b1c1be409850b2, second parent 42deb04c60c12a09ece7ea28d8e43020964b51ba).

Included in main: 811798a (control) + d75e775 (code fix) + 42deb04 (test fix).

Excluded from main by design: b7346b5 (decisive diagnostic), all diag/snapshot branches, fb68c81 (row-key fix).

## Next action

Review and merge fb68c81 separately as an independent row-key correctness fix.
