# A release watches exactly one workflow run

GitHub happily starts several workflow runs from a single tag push, but a
release here locates and watches exactly one. Every managed repository already
follows the convention that tag pushes belong to one workflow — one repo's CI
file says so in a comment, restricting itself to branches so that "tag pushes
are owned by release.yml" — so the app treats one-run-per-release as a rule of
the domain rather than modelling a fan-out nobody uses.

## Considered options

Relating a release to many runs and showing them all was the alternative. It was
rejected as complexity in service of a situation that does not arise, and one
that would make the common case — a single run's job and step timeline — harder
to read.

Leaving the previous behaviour alone was not acceptable: it picked the most
recent matching run and silently ignored the rest, so the day the convention
broke would surface as a run that mysteriously never appeared.

## Consequences

The assumption fails loudly in both places it can break. When a project is added
or edited, detection names any additional tag-triggered workflow as unwatched
rather than dropping it. And at release time, if more than one run matches, the
locate step reports how many were found and that only the newest is watched.

If a project ever genuinely needs several release workflows, this decision has to
be revisited rather than worked around.
