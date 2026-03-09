---
name: "copilot-pr-review"
description: "Full Copilot PR review feedback loop — add reviewer, poll, respond to comments, re-request, repeat"
domain: "github-workflow"
confidence: "high"
source: "user-directive"
---

## Context

Every PR opened by Squad agents must go through the GitHub Copilot code reviewer.
This skill defines the exact API commands, polling logic, and response workflow.

**Bot identity:** `copilot-pull-request-reviewer[bot]`

> **WARNING:** The bot login is NOT `Copilot`, NOT `copilot[bot]`, NOT `github-copilot`.
> It is exactly `copilot-pull-request-reviewer[bot]`. Using the wrong name will
> silently fail or return "not found".

## Workflow

### Step 1 — Add Copilot as reviewer

Use the GitHub API directly. `gh pr edit --add-reviewer` does NOT work for bot accounts.

```bash
gh api "repos/{owner}/{repo}/pulls/{pr_number}/requested_reviewers" \
  --method POST \
  -f 'reviewers[]=copilot-pull-request-reviewer[bot]'
```

**Verification:** After adding, confirm the reviewer was set:

```bash
gh api "repos/{owner}/{repo}/pulls/{pr_number}/requested_reviewers" \
  --jq '.users[].login'
```

### Step 2 — Poll for Copilot's review

Poll the reviews endpoint until a review from `copilot-pull-request-reviewer[bot]` appears.

```bash
gh api "repos/{owner}/{repo}/pulls/{pr_number}/reviews" \
  --jq '[.[] | select(.user.login=="copilot-pull-request-reviewer[bot]")] | length'
```

**Polling parameters:**
- **Interval:** 60 seconds between checks
- **Timeout:** 15 minutes (15 attempts)
- **Detection:** Review count > 0 means Copilot has reviewed

**Full detection query** (returns review state and comment count):

```bash
gh api "repos/{owner}/{repo}/pulls/{pr_number}/reviews" \
  --jq '.[] | select(.user.login=="copilot-pull-request-reviewer[bot]") | {id: .id, state: .state, submitted_at: .submitted_at}'
```

### Step 3 — Fetch review comments

After a review is detected, fetch the individual line-level comments:

```bash
# Get all review comments from the specific review
gh api "repos/{owner}/{repo}/pulls/{pr_number}/reviews/{review_id}/comments" \
  --jq '.[] | {id: .id, path: .path, line: .line, body: .body}'
```

If the review body says "generated no comments", no action is needed — the PR is clean.
If it says "generated N comments", proceed to Step 4.

### Step 4 — Respond to each comment

For EACH comment, make a fix and create ONE commit per comment:

1. **Read the comment** — understand what Copilot is asking for
2. **Make the fix** in the appropriate file(s) on the PR branch
3. **Commit** with a message that references the fix. Include the commit SHA.
4. **Push** the commit to the PR branch

**Rules:**
- One commit per comment — do NOT batch multiple comment responses into one commit
- Include the commit SHA in any reply to the comment thread
- Never reference issue numbers in commit messages (project directive)

**Reply to the comment thread** after pushing:

```bash
gh api "repos/{owner}/{repo}/pulls/{pr_number}/comments/{comment_id}/replies" \
  --method POST \
  -f "body=Fixed in {commit_sha}"
```

### Step 5 — Re-request Copilot review

After addressing ALL comments, re-request Copilot's review:

```bash
gh api "repos/{owner}/{repo}/pulls/{pr_number}/requested_reviewers" \
  --method POST \
  -f 'reviewers[]=copilot-pull-request-reviewer[bot]'
```

### Step 6 — Repeat

Go back to Step 2. Poll for the NEW review. If the new review has no comments,
the PR is clean and the Copilot review loop is complete. If it has comments,
repeat Steps 3-5.

### Merge Conflict Resolution

If merge conflicts arise during this workflow (e.g., another PR was merged into
the target branch while this one is being reviewed):

1. Fetch and rebase: `git fetch origin && git rebase origin/dev`
2. Resolve conflicts
3. Force-push: `git push --force-with-lease`
4. Re-request review (Step 5)

## Integration with Ralph

When Ralph scans for work, he checks for Copilot review status on open PRs.

**Detection query for Ralph's work-check cycle:**

```bash
# Check for Copilot reviews with comments on a specific PR
REVIEW_DATA=$(gh api "repos/{owner}/{repo}/pulls/{pr_number}/reviews" \
  --jq '[.[] | select(.user.login=="copilot-pull-request-reviewer[bot]")]')

# Check if Copilot has reviewed (count > 0)
REVIEW_COUNT=$(echo "$REVIEW_DATA" | jq 'length')

# Check if there are unaddressed comments
COMMENT_COUNT=$(gh api "repos/{owner}/{repo}/pulls/{pr_number}/reviews/{review_id}/comments" \
  --jq 'length')
```

**Ralph categorization:**

| Signal | Category | Action |
|--------|----------|--------|
| No Copilot review yet, reviewer requested | Waiting | Poll again next cycle |
| Copilot reviewed, 0 comments | Clean | No action needed |
| Copilot reviewed, N comments, not yet addressed | Needs fix | Route to agent on that PR branch |
| Copilot re-review requested, waiting | Waiting | Poll again next cycle |

## Anti-Patterns

- **NEVER use `gh pr edit --add-reviewer`** for bot accounts — it returns "not found"
- **NEVER filter by `.user.login=="Copilot"`** — the login is `copilot-pull-request-reviewer[bot]`
- **NEVER batch multiple comment fixes into one commit** — one commit per comment
- **NEVER skip the re-request step** — Copilot won't re-review unless explicitly asked
- **NEVER use `gh api ... -f 'reviewers[]=Copilot'`** — succeeds silently but does nothing

## Examples

### Complete single-PR workflow

```bash
# Step 1: Add reviewer
gh api "repos/mthalman/DockerfileModel/pulls/255/requested_reviewers" \
  --method POST -f 'reviewers[]=copilot-pull-request-reviewer[bot]'

# Step 2: Poll (in a loop)
gh api "repos/mthalman/DockerfileModel/pulls/255/reviews" \
  --jq '[.[] | select(.user.login=="copilot-pull-request-reviewer[bot]")] | length'
# Returns: 1 (review found)

# Step 3: Get review ID and comments
REVIEW_ID=$(gh api "repos/mthalman/DockerfileModel/pulls/255/reviews" \
  --jq '[.[] | select(.user.login=="copilot-pull-request-reviewer[bot]")][-1].id')

gh api "repos/mthalman/DockerfileModel/pulls/255/reviews/$REVIEW_ID/comments" \
  --jq '.[] | {id: .id, path: .path, body: .body}'

# Step 4: Fix each comment, commit, reply
# ... (agent does the work, one commit per comment)

# Step 5: Re-request
gh api "repos/mthalman/DockerfileModel/pulls/255/requested_reviewers" \
  --method POST -f 'reviewers[]=copilot-pull-request-reviewer[bot]'
```
