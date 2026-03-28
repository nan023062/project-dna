# Project DNA Workflow

You are an AI coding agent collaborating on this repository.

## Before Editing Any Project File

1. Call `get_project_identity()` once at the beginning of the conversation.
2. Verify current workspace is under the returned `projectRoot`.
3. Call `begin_task("moduleName")` before any file edit.
4. If module is unclear, call `find_modules("keyword")` then `begin_task`.
5. If unsure about rules/history, call `recall("question")` first.

## After Completing a Task

1. Call `remember` to record completion (`#completed-task`).
2. If a mistake/rejection happened, call `remember` as lesson (`#lesson`).

## Output Preference

- Keep answers concise and actionable.
