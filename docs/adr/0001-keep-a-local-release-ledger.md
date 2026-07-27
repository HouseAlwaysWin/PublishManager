# Keep a local release ledger

Tags are the version source of truth, but they get pruned aggressively — one
managed repository sits at v0.66 with only five tags left, so roughly sixty
released versions leave no trace in git or on GitHub. So the app records every
release it performs to a local append-only ledger, which is the only thing that
can still answer "when did v0.20.0 ship, and from which commit?" once the tag is
gone.

## Considered options

Relying on git and GitHub alone was the obvious choice, and it is what the app
did until now: no duplicate state, nothing to keep in sync. It was rejected
precisely because the user prunes tags, which is exactly when the record is
wanted.

Storing full logs alongside the facts was also considered and rejected: a single
observed job log ran to 111,303 characters, so keeping logs would turn the app
into a poor log database duplicating what GitHub already serves. The ledger
stores a link to the workflow run instead.

## Consequences

The ledger only knows about releases made *through this app*; versions released
by other means are absent, and the history view says so rather than implying the
list is complete. Releases that fail before reaching GitHub are not recorded —
nothing was released, so there is nothing to remember.
