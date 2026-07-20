namespace Mediatheca.Server

open Microsoft.Data.Sqlite
open Mediatheca.Shared

/// Server-side implementation of IAdminApi — the Administration console's
/// Remoting contract. Kept separate from Api.fs (ADR-0004: multiple Fable.Remoting
/// APIs are supported) so admin plumbing (event store browser today; projection
/// dashboard, health, jobs, and surgery tooling in follow-up tasks) doesn't bloat
/// IMediathecaApi. Mounted under /api/admin/{Method} via AdminRoute.builder.
module Administration =

    let create (conn: SqliteConnection) : IAdminApi =
        {
            getEvents = fun query -> async {
                let events = EventStore.queryEvents conn query.StreamFilter query.EventTypeFilter query.Limit query.Offset
                return events |> List.map (fun e ->
                    { Mediatheca.Shared.EventDto.GlobalPosition = e.GlobalPosition
                      StreamId = e.StreamId
                      StreamPosition = e.StreamPosition
                      EventType = e.EventType
                      Data = e.Data
                      Timestamp = e.Timestamp.ToString("o") }
                )
            }

            getEventStreams = fun () -> async {
                return EventStore.getDistinctStreams conn
            }

            getEventTypes = fun () -> async {
                return EventStore.getDistinctEventTypes conn
            }
        }
