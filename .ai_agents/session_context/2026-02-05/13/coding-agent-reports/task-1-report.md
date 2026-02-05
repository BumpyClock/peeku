# Task 1 Report

## Summary
- Added `peeku.Daemon` named-pipe JSON-RPC server + dispatcher/codec
- Added daemon JSON-RPC tests using in-memory streams
- Updated solution to include daemon projects

## Files Changed
- `src/peeku.Daemon/peeku.Daemon.csproj`
- `src/peeku.Daemon/InternalsVisibleTo.cs`
- `src/peeku.Daemon/DaemonShutdown.cs`
- `src/peeku.Daemon/JsonRpcModels.cs`
- `src/peeku.Daemon/JsonRpcDispatcher.cs`
- `src/peeku.Daemon/JsonRpcCodec.cs`
- `src/peeku.Daemon/JsonRpcConnection.cs`
- `src/peeku.Daemon/DaemonServer.cs`
- `src/peeku.Daemon/Program.cs`
- `tests/peeku.Daemon.Tests/peeku.Daemon.Tests.csproj`
- `tests/peeku.Daemon.Tests/JsonRpcConnectionTests.cs`
- `peeku.slnx`
- `LEARNINGS.md`

## Tests
- `dotnet test tests\peeku.Daemon.Tests\peeku.Daemon.Tests.csproj`
