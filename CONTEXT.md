# PublishManager

A desktop control panel for releasing many GitHub-hosted projects through one
consistent flow, when each project's own release process differs.

## Language

### The project and its versions

**Project**:
One release line the app manages — where its repository sits, and how that line
is versioned and released. A repository may host several, told apart by tag
prefix; two may never share both a path and a prefix.
_Avoid_: Repository, app, solution

**Version**:
The SemVer number identifying what was released. An identifier shared by a Tag
and a Release Ledger entry — not a thing in its own right, so a Tag carrying no
valid SemVer simply has no Version.
_Avoid_: Release number, build number

**Tag**:
A live git tag in a project's repository. The only releasable thing that can be
deleted; once deleted, only the Release Ledger remembers it.
_Avoid_: Ref, label

**Release Ledger**:
The local record of every Release this app has performed, kept because tags get
pruned and git then remembers nothing. Holds the facts of a release — version,
time, outcome, commit, run link — never its logs.
_Avoid_: History, audit log

### Performing a release

**Release**:
One execution of a project's release flow, performed by this app.
_Avoid_: Publish, deploy, ship

**Release Source**:
The commit a Release is cut from, given as a branch, tag, or commit sha, and
defaulting to whatever is checked out. Naming one never changes the working
copy — the tag is written straight onto that commit.
_Avoid_: Target, ref, base

**Release Trigger**:
How a project starts its Workflow Run — by pushing a tag, or by dispatching the
workflow directly. Chosen per project.
_Avoid_: Release model, strategy, mode

**Stage**:
One of the fixed phases a Release passes through, owned by the app and not
configurable — preflight, version, tag or dispatch, locate run.
_Avoid_: Step, phase

**Progress Row**:
A line in a release's progress list — a Stage or a Step. Named so that neither
term has to stand for both, and identified by a key so that a Step named after
a Stage stays its own row.
_Avoid_: Stage (when steps are included)

**Step**:
A command the user configures a project to run locally during a Release —
building, packing, testing. Repeatable and free of external side effects, so a
dry run can execute it; anything irreversible belongs in the workflow instead.
Arbitrary in number, freely named, and never identified by its name.
_Avoid_: Stage, task, action

**Dry Run**:
A Release that performs every local Step but withholds the irreversible ones —
no tag pushed, no workflow dispatched, no GitHub Release created. It answers
whether the release would succeed.
_Avoid_: Preview, simulation, test run

### What GitHub owns

**GitHub Release**:
The release page GitHub publishes for a tag, with its notes and assets. Always
named in full — never the bare word "Release", which belongs to the act above.
_Avoid_: Release (unqualified)

**Workflow Run**:
The GitHub Actions run a Release triggers and then watches. A Release has
exactly one: a project is expected to own tag pushes with a single workflow.
_Avoid_: Build, job, pipeline

**Workflow Step**:
A step inside a GitHub Actions job, reported by GitHub while a Workflow Run is
watched. Never the bare word "Step", which belongs to the user's own commands.
_Avoid_: Step (unqualified)
