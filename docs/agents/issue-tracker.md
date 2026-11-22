# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --json number,title,body,labels,comments`.
- **List issues**: `gh issue list --state open --json number,title,body,labels --jq '[.[] | {number, title, body, labels: [.labels[].name]}]'` filtered by label/state.
- **Make an issue a sub-issue of a parent**: `gh issue create --parent <parent> ...`, or `gh issue edit <parent> --add-sub-issue <child>` afterwards.
- **Blocking**: native issue dependencies, UI-visible. Add with `gh issue create --blocked-by <n>` or `gh issue edit <child> --add-blocked-by <blocker>`.
- **Count open blockers**: `gh api repos/{owner}/{repo}/issues/<n> --jq .issue_dependencies_summary.blocked_by`.
- **Comment / close**: `gh issue comment <number> --body "..."`. Close = post explanation with `gh issue comment`, then `gh issue close <number>`.
