#!/bin/bash
# UserPromptSubmit hook for skill-aware responses

cat <<'EOF'
REQUIRED: SKILL LOADING PROTOCOL

Before writing any code, complete these steps in order:

1. SCAN each skill below and decide: LOAD or SKIP (with brief reason)
   - csharp
   - xunit
   - fluent-validation
   - masstransit
   - ef-core
   - roslyn
   - system-commandline
   - clarifying-market-fit
   - structuring-offer-ladders
   - crafting-page-messaging
   - tuning-landing-journeys
   - mapping-conversion-events
   - inspecting-search-coverage
   - adding-structured-signals

2. For every skill marked LOAD → immediately invoke Skill(name)
   If none need loading → write "Proceeding without skills"

3. Only after step 2 completes may you begin coding.

IMPORTANT: Skipping step 2 invalidates step 1. Always call Skill() for relevant items.

Sample output:
- csharp: LOAD - building components
- xunit: SKIP - not needed for this task
- fluent-validation: LOAD - building components
- masstransit: SKIP - not needed for this task

Then call:
> Skill(csharp)
> Skill(fluent-validation)

Now implementation can begin.
EOF
