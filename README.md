# account-rotation

Account switching and quota dashboard for Claude Code.

A single local executable that serves a loopback web page showing every one of your Claude Max
accounts with its 5-hour and 7-day headroom, ranks them by earliest weekly reset, and switches the
whole machine to the account you pick, with no browser step. Every open Claude Code session follows
the switch on its next request.

Status: pre-release, under construction. The confirmed Brief and the approved implementation plan
live in `docs/topics/claude-subscription-rotation/PLAN.md`.

## Posture

- Nothing calls the model API outside the unmodified `claude` binary.
- Credential pairs are moved between your own files on your own machine, never copied, so each
  refresh token exists in exactly one place.
- The tool identifies itself honestly on every request it makes.
- Every switch is a human click; nothing rotates on its own.

## Build

Requires the .NET SDK pinned in `global.json`.

```bash
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

## License

MIT, see `LICENSE`.
