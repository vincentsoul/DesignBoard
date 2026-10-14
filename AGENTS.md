# Repository workflow

- After completing each user task, run checks appropriate to the change, commit the completed changes, and push them to this repository's configured GitHub remote unless the user explicitly requests otherwise.
- Preserve unrelated local changes. Include only changes belonging to the completed task, and report any push or authentication failure clearly.
- Never commit passwords, NAS connection secrets, authentication configuration, personal board data, or generated test artifacts. Keep `secure/`, `.env`, board data, and output folders ignored.
