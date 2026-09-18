namespace Mediatheca.Server

open Thoth.Json.Net
open Mediatheca.Shared

/// The Book aggregate (books-y9kxy) — the server core of the fourth media
/// type. Model of record: ADR-0076 (Books model — progress observations are
/// events; length is cache; status mirrors Games), amended by ADR-0077
/// (status changes carry an effective-on date), ADR-0082 (prior reading
/// progress; manual-finish local date) and ADR-0085 (books-wk67x — history
/// entries append, never overwrite a prior, amending ADR-0076 §2 and
/// ADR-0082 §7). Mirrors `Games.fs`'s shape (DU + decide + Serialization)
/// exactly, per this task's own instructions.
module Books =

    // Data records for events

    type BookAddedData = {
        Title: string
        Authors: string list
        Year: int option
        CoverRef: string option
        Subjects: string list
        Format: BookFormat
        ExternalIds: BookExternalId list
    }

    /// Payload of `Reading_progress_observed` (ADR-0076 §1) — the user's
    /// engagement at a moment that cannot be re-observed later. `Percent` is
    /// computed at observation time (from the source's own percent, or from
    /// page/total) so the event is self-contained and replay never needs the
    /// cache. `Finished` carries a source's explicit finished flag
    /// (Audible's `is_finished`).
    type ReadingProgressObservedData = {
        Percent: int
        Position: ReadingPosition option
        Source: ProgressSource
        ObservedOn: string
        Finished: bool
    }

    // Events

    type BookEvent =
        | Book_added_to_library of BookAddedData
        | Book_removed_from_library
        | Book_cover_replaced of coverRef: string
        | Book_external_id_linked of BookExternalId
        | Book_format_set of format: BookFormat
        /// ADR-0077: `effectiveOn` is the `yyyy-MM-dd` day the status became
        /// true according to a source that knows it; `None` means "the day
        /// this event was appended".
        | Book_status_changed of status: BookStatus * effectiveOn: string option
        | Reading_progress_observed of ReadingProgressObservedData
        /// ADR-0082: a source's first-ever reported position for a book —
        /// where the reader already was, not a session read that day. Same
        /// payload as `Reading_progress_observed`, reused verbatim; seeds
        /// `Observations` identically so the per-source no-op/regression
        /// rules see it, but never promotes to InFocus (only
        /// `Reading_progress_observed` does that).
        | Prior_reading_progress_recorded of ReadingProgressObservedData
        /// Legacy — replay-only (books-wk67x, amending ADR-0076 §2). No
        /// command emits this any more; kept solely so a historical stream
        /// still decodes and replays with its ORIGINAL meaning: removes
        /// every entry of that day/source that existed at that point in the
        /// stream (there was at most one, before this task, since same-day
        /// same-source observations used to collapse). New removals use
        /// `Reading_progress_entry_removed` below.
        | Reading_progress_observation_removed of observedOn: string * source: ProgressSource
        /// books-wk67x: names the ONE history entry to remove, by its entry
        /// id (see `ObservationEntry.EntryId` below) — the mechanism that
        /// lets a user remove one of several same-day, same-source entries
        /// without touching its siblings.
        | Reading_progress_entry_removed of entryId: int64
        | Book_personal_rating_set of rating: int option
        | Book_recommended_by of friendSlug: string
        | Book_recommendation_removed of friendSlug: string

    /// books-wk67x (amending ADR-0076 §2): one append-only history entry —
    /// a prior or an observation never overwrites another; a change in
    /// percent OR position always adds a new entry, even same-day
    /// same-source. `EntryId` identifies the entry for removal
    /// (`Reading_progress_entry_removed`) — assigned by whoever folds the
    /// event stream: `evolve`'s caller supplies it per event, since the
    /// value itself (the event's own position in the store) isn't part of
    /// the event's payload. Production command execution supplies the
    /// event's real `StoredEvent.GlobalPosition`; `reconstituteEvents`
    /// (tests, and any caller with no real store position) supplies a
    /// synthetic but equally monotonic 1-based sequence instead — either
    /// way, `EntryId` values are unique and increase in append order within
    /// one call, which is all `decide`/`evolve` ever rely on.
    type ObservationEntry = {
        EntryId: int64
        ObservedOn: string
        Source: ProgressSource
        Percent: int
        Position: ReadingPosition option
        Kind: ProgressKind
    }

    // State

    type ActiveBook = {
        Title: string
        Authors: string list
        Year: int option
        CoverRef: string option
        Subjects: string list
        Format: BookFormat
        ExternalIds: BookExternalId list
        Status: BookStatus
        /// The last `Finished` status change's `effectiveOn` (ADR-0077) —
        /// the raw value the event carried (possibly `None`), cleared
        /// whenever status leaves `Finished`. The projection, not the
        /// aggregate, defaults a `None` to the event's own local date.
        FinishedOn: string option
        PersonalRating: int option
        RecommendedBy: Set<string>
        /// Every reading-progress history entry (prior or observation) ever
        /// recorded and not since removed, in append order (books-wk67x,
        /// amending ADR-0076 §2 — a later entry never overwrites an
        /// earlier one, even same day/source). The per-source no-op/
        /// promotion rules in `decide` walk this list for the SAME source's
        /// latest entry, not the book's global current percent.
        Observations: ObservationEntry list
    }

    type BookState =
        | Not_created
        | Active of ActiveBook
        | Removed

    // Commands

    type BookCommand =
        | Add_book_to_library of BookAddedData
        | Remove_book_from_library
        | Replace_cover of coverRef: string
        | Link_external_id of BookExternalId
        | Set_format of format: BookFormat
        | Change_status of status: BookStatus * effectiveOn: string option
        | Observe_reading_progress of ReadingProgressObservedData
        /// ADR-0082 §2: issued only by the bulk "Import library" run
        /// (integration-dtdbb) — the aggregate never infers a prior from
        /// state, the intent rides the command.
        | Record_prior_reading_progress of ReadingProgressObservedData
        /// books-wk67x: replaces `Remove_reading_progress_observation`
        /// (day+source) as the ONLY command that removes a history entry —
        /// named by `ObservationEntry.EntryId`, so removing one of several
        /// same-day same-source entries never touches its siblings. The
        /// old day+source SHAPE survives only as `BookEvent`'s
        /// `Reading_progress_observation_removed` case, for replaying
        /// history recorded before this task.
        | Remove_reading_progress_entry of entryId: int64
        | Set_personal_rating of rating: int option
        | Recommend_by of friendSlug: string
        | Remove_recommendation of friendSlug: string

    // Evolve

    /// `entryId` is the store position of `event` — see
    /// `ObservationEntry.EntryId`'s doc comment for who supplies it and why
    /// it isn't part of the event's own payload. Ignored by every event
    /// case except the two that create or reference a history entry.
    let evolve (state: BookState) (entryId: int64, event: BookEvent) : BookState =
        match state, event with
        | Not_created, Book_added_to_library data ->
            Active {
                Title = data.Title
                Authors = data.Authors
                Year = data.Year
                CoverRef = data.CoverRef
                Subjects = data.Subjects
                Format = data.Format
                ExternalIds = data.ExternalIds
                Status = BookStatus.Backlog
                FinishedOn = None
                PersonalRating = None
                RecommendedBy = Set.empty
                Observations = []
            }
        | Active _, Book_removed_from_library -> Removed
        | Active book, Book_cover_replaced coverRef ->
            Active { book with CoverRef = Some coverRef }
        | Active book, Book_external_id_linked externalId ->
            Active { book with ExternalIds = (book.ExternalIds @ [ externalId ]) |> List.distinct }
        | Active book, Book_format_set format ->
            Active { book with Format = format }
        | Active book, Book_status_changed (status, effectiveOn) ->
            let finishedOn = if status = BookStatus.Finished then effectiveOn else None
            Active { book with Status = status; FinishedOn = finishedOn }
        | Active book, Reading_progress_observed data ->
            let entry = { EntryId = entryId; ObservedOn = data.ObservedOn; Source = data.Source; Percent = data.Percent; Position = data.Position; Kind = Observed }
            Active { book with Observations = book.Observations @ [ entry ] }
        | Active book, Prior_reading_progress_recorded data ->
            let entry = { EntryId = entryId; ObservedOn = data.ObservedOn; Source = data.Source; Percent = data.Percent; Position = data.Position; Kind = Prior }
            Active { book with Observations = book.Observations @ [ entry ] }
        | Active book, Reading_progress_observation_removed (observedOn, source) ->
            // Legacy replay semantics (books-wk67x, point 6): removes every
            // entry of that day/source that exists AT THIS POINT in the
            // stream — never an upcast, so history replays identically.
            Active { book with Observations = book.Observations |> List.filter (fun e -> not (e.ObservedOn = observedOn && e.Source = source)) }
        | Active book, Reading_progress_entry_removed removedEntryId ->
            Active { book with Observations = book.Observations |> List.filter (fun e -> e.EntryId <> removedEntryId) }
        | Active book, Book_personal_rating_set rating ->
            Active { book with PersonalRating = rating }
        | Active book, Book_recommended_by friendSlug ->
            Active { book with RecommendedBy = book.RecommendedBy |> Set.add friendSlug }
        | Active book, Book_recommendation_removed friendSlug ->
            Active { book with RecommendedBy = book.RecommendedBy |> Set.remove friendSlug }
        | _ -> state

    /// The real reconstitution path: `entryId` for each event is the exact
    /// value that identifies its history entry everywhere else (the
    /// projection, the DTO, a removal command) — production command
    /// execution supplies each event's true `StoredEvent.GlobalPosition`
    /// here (see `Api.fs`'s `executeBookCommandWithEvents`).
    let reconstitute (events: (int64 * BookEvent) list) : BookState =
        List.fold evolve Not_created events

    /// Convenience for callers with no real store position to hand
    /// `evolve` — every existing test, and any future caller that only
    /// needs correct ORDER (never a removal-by-id round trip against a
    /// real store) — assigning entries a synthetic 1-based sequence by
    /// their position in `events`. Safe because `decide`/`evolve` never
    /// depend on the actual numeric value of `EntryId`, only on it being
    /// unique and increasing in append order.
    let reconstituteEvents (events: BookEvent list) : BookState =
        events |> List.mapi (fun i e -> (int64 (i + 1), e)) |> reconstitute

    // Decide

    let private isValidDate (s: string) : bool =
        match System.DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None) with
        | true, _ -> true
        | false, _ -> false

    let private externalIdKind (eid: BookExternalId) : string =
        match eid with
        | Isbn13 _ -> "isbn13"
        | OpenLibraryWork _ -> "openLibraryWork"
        | OpenLibraryEdition _ -> "openLibraryEdition"
        | AudibleAsin _ -> "audibleAsin"

    /// The comparison baseline for `Observe_reading_progress`'s no-op/
    /// promotion rules: the SAME source's latest entry — ordered by
    /// `ObservedOn`, then by `EntryId` (append order) within a day, so two
    /// same-day same-source entries pick the one recorded LAST as the
    /// baseline for the next observation (books-wk67x, amending ADR-0076
    /// §2 — the old natural key `(observedOn, source)` no longer identifies
    /// a single entry).
    let private latestEntryForSource (book: ActiveBook) (source: ProgressSource) : ObservationEntry option =
        book.Observations
        |> List.filter (fun e -> e.Source = source)
        |> List.sortByDescending (fun e -> e.ObservedOn, e.EntryId)
        |> List.tryHead

    /// ADR-0077 §4: `Change_status (status, effectiveOn)` is a no-op only
    /// when the status is unchanged AND the effective date would not change.
    let private statusChangeIsNoOp (current: BookStatus) (currentFinishedOn: string option) (status: BookStatus) (effectiveOn: string option) : bool =
        status = current && (status <> BookStatus.Finished || effectiveOn = None || effectiveOn = currentFinishedOn)

    /// The shared observation logic behind `Observe_reading_progress` and
    /// `Record_prior_reading_progress` (ADR-0082 §2: once a source already
    /// has an entry, a prior command "behaves exactly as
    /// Observe_reading_progress" — same validation, same per-source
    /// no-op/regression/promotion rules, same `Reading_progress_observed`
    /// event).
    let private decideObserveProgress (book: ActiveBook) (data: ReadingProgressObservedData) : Result<BookEvent list, string> =
        if data.Percent < 0 || data.Percent > 100 then
            Error "Percent must be between 0 and 100"
        elif not (isValidDate data.ObservedOn) then
            Error "ObservedOn must be a yyyy-MM-dd date"
        else
            let latest = latestEntryForSource book data.Source
            // books-wk67x (amending ADR-0076 §2): the latest entry no
            // longer collapses same-day/source — a change in EITHER
            // percent OR position always appends a new entry, even same
            // day, same source. When the source has no entry yet, the
            // baseline is the virtual `{ Percent = 0; Position = None }`
            // entry `latestPercentForSource` used to default to, so a
            // genuine first-ever 0%/no-position observation is still a
            // no-op exactly as before.
            let baselinePercent = latest |> Option.map (fun e -> e.Percent) |> Option.defaultValue 0
            let baselinePosition = latest |> Option.bind (fun e -> e.Position)
            let isNoOp = data.Percent = baselinePercent && data.Position = baselinePosition
            if isNoOp then
                Ok []
            else
                let observedEvent = Reading_progress_observed data
                let statusEvents =
                    if data.Percent = 100 || data.Finished then
                        if book.Status = BookStatus.Finished then []
                        else [ Book_status_changed (BookStatus.Finished, Some data.ObservedOn) ]
                    elif data.Percent > baselinePercent then
                        match book.Status with
                        | BookStatus.Backlog | BookStatus.Abandoned ->
                            [ Book_status_changed (BookStatus.InFocus, Some data.ObservedOn) ]
                        | _ -> []
                    else []
                Ok (observedEvent :: statusEvents)

    let decide (state: BookState) (command: BookCommand) : Result<BookEvent list, string> =
        match state, command with
        | Not_created, Add_book_to_library data ->
            let kinds = data.ExternalIds |> List.map externalIdKind
            if List.length kinds <> List.length (List.distinct kinds) then
                Error "Add_book_to_library carries more than one external id of the same kind"
            else
                Ok [ Book_added_to_library data ]
        | Active _, Add_book_to_library _ ->
            Error "Book already exists in library"
        | Active _, Remove_book_from_library ->
            Ok [ Book_removed_from_library ]
        | Not_created, Remove_book_from_library ->
            Error "Book does not exist"
        | Active _, Replace_cover coverRef ->
            Ok [ Book_cover_replaced coverRef ]
        | Active book, Link_external_id externalId ->
            let kind = externalIdKind externalId
            match book.ExternalIds |> List.tryFind (fun eid -> externalIdKind eid = kind) with
            | Some existing when existing = externalId -> Ok []
            | Some _ -> Error (sprintf "A %s is already linked to this book" kind)
            | None -> Ok [ Book_external_id_linked externalId ]
        | Active book, Set_format format ->
            if book.Format = format then Ok [] else Ok [ Book_format_set format ]
        | Active book, Change_status (status, effectiveOn) ->
            match effectiveOn with
            | Some d when not (isValidDate d) -> Error "effectiveOn must be a yyyy-MM-dd date"
            | _ ->
                if statusChangeIsNoOp book.Status book.FinishedOn status effectiveOn then Ok []
                else Ok [ Book_status_changed (status, effectiveOn) ]
        | Active book, Observe_reading_progress data ->
            decideObserveProgress book data
        | Active book, Record_prior_reading_progress data ->
            // ADR-0082 §2: a prior exists only for the bulk import — the
            // aggregate reads that intent off the command, never infers it.
            // Once the source already has an entry, this behaves exactly
            // like Observe_reading_progress (no second prior, ever).
            if book.Observations |> List.exists (fun e -> e.Source = data.Source) then
                decideObserveProgress book data
            elif data.Percent < 0 || data.Percent > 100 then
                Error "Percent must be between 0 and 100"
            elif not (isValidDate data.ObservedOn) then
                Error "ObservedOn must be a yyyy-MM-dd date"
            else
                let priorEvent = Prior_reading_progress_recorded data
                let statusEvents =
                    if data.Percent = 100 || data.Finished then
                        if book.Status = BookStatus.Finished then []
                        else [ Book_status_changed (BookStatus.Finished, Some data.ObservedOn) ]
                    else
                        // A prior never promotes to InFocus, regardless of
                        // percent — only a raising observation does that.
                        []
                Ok (priorEvent :: statusEvents)
        | Active book, Remove_reading_progress_entry entryId ->
            match book.Observations |> List.tryFind (fun e -> e.EntryId = entryId) with
            | None -> Error "Reading progress entry not found"
            | Some _ -> Ok [ Reading_progress_entry_removed entryId ]
        | Active book, Set_personal_rating rating ->
            if book.PersonalRating = rating then Ok [] else Ok [ Book_personal_rating_set rating ]
        | Active book, Recommend_by friendSlug ->
            if book.RecommendedBy |> Set.contains friendSlug then Ok []
            else Ok [ Book_recommended_by friendSlug ]
        | Active book, Remove_recommendation friendSlug ->
            if book.RecommendedBy |> Set.contains friendSlug then Ok [ Book_recommendation_removed friendSlug ]
            else Ok []
        | Removed, _ ->
            Error "Book has been removed"
        | Not_created, _ ->
            Error "Book does not exist"

    // Stream ID

    let streamId (slug: string) = sprintf "Book-%s" slug

    // Serialization

    module Serialization =

        let private encodeBookFormat (format: BookFormat) =
            match format with
            | Audiobook -> "Audiobook"
            | Print -> "Print"
            | Ebook -> "Ebook"
            | BookFormat.Unknown -> "Unknown"

        let private decodeBookFormat (s: string) : BookFormat =
            match s with
            | "Audiobook" -> Audiobook
            | "Print" -> Print
            | "Ebook" -> Ebook
            | _ -> BookFormat.Unknown

        let private encodeBookStatus (status: BookStatus) =
            match status with
            | BookStatus.Backlog -> "Backlog"
            | BookStatus.InFocus -> "InFocus"
            | BookStatus.Finished -> "Finished"
            | BookStatus.Abandoned -> "Abandoned"

        let private decodeBookStatus (s: string) : BookStatus =
            match s with
            | "InFocus" -> BookStatus.InFocus
            | "Finished" -> BookStatus.Finished
            | "Abandoned" -> BookStatus.Abandoned
            | _ -> BookStatus.Backlog

        let private encodeProgressSource (source: ProgressSource) =
            match source with
            | Audible -> "Audible"
            | ProgressSource.Manual -> "Manual"

        let private decodeProgressSource (s: string) : ProgressSource =
            match s with
            | "Audible" -> Audible
            | _ -> ProgressSource.Manual

        let private encodeReadingPosition (pos: ReadingPosition) =
            match pos with
            | Page (page, total) ->
                Encode.object [ "kind", Encode.string "Page"; "value", Encode.int page; "total", Encode.option Encode.int total ]
            | Minutes (minutes, total) ->
                Encode.object [ "kind", Encode.string "Minutes"; "value", Encode.int minutes; "total", Encode.option Encode.int total ]

        let private decodeReadingPosition: Decoder<ReadingPosition> =
            Decode.object (fun get ->
                let kind = get.Required.Field "kind" Decode.string
                let value = get.Required.Field "value" Decode.int
                let total = get.Optional.Field "total" Decode.int
                match kind with
                | "Minutes" -> Minutes (value, total)
                | _ -> Page (value, total))

        let private encodeBookExternalId (eid: BookExternalId) =
            match eid with
            | Isbn13 v -> Encode.object [ "kind", Encode.string "Isbn13"; "value", Encode.string v ]
            | OpenLibraryWork v -> Encode.object [ "kind", Encode.string "OpenLibraryWork"; "value", Encode.string v ]
            | OpenLibraryEdition v -> Encode.object [ "kind", Encode.string "OpenLibraryEdition"; "value", Encode.string v ]
            | AudibleAsin v -> Encode.object [ "kind", Encode.string "AudibleAsin"; "value", Encode.string v ]

        let private decodeBookExternalId: Decoder<BookExternalId> =
            Decode.object (fun get ->
                let kind = get.Required.Field "kind" Decode.string
                let value = get.Required.Field "value" Decode.string
                match kind with
                | "Isbn13" -> Isbn13 value
                | "OpenLibraryWork" -> OpenLibraryWork value
                | "OpenLibraryEdition" -> OpenLibraryEdition value
                | _ -> AudibleAsin value)

        let private encodeBookAddedData (data: BookAddedData) =
            Encode.object [
                "title", Encode.string data.Title
                "authors", data.Authors |> List.map Encode.string |> Encode.list
                "year", Encode.option Encode.int data.Year
                "coverRef", Encode.option Encode.string data.CoverRef
                "subjects", data.Subjects |> List.map Encode.string |> Encode.list
                "format", Encode.string (encodeBookFormat data.Format)
                "externalIds", data.ExternalIds |> List.map encodeBookExternalId |> Encode.list
            ]

        let private decodeBookAddedData: Decoder<BookAddedData> =
            Decode.object (fun get -> {
                Title = get.Required.Field "title" Decode.string
                Authors = get.Optional.Field "authors" (Decode.list Decode.string) |> Option.defaultValue []
                Year = get.Optional.Field "year" Decode.int
                CoverRef = get.Optional.Field "coverRef" Decode.string
                Subjects = get.Optional.Field "subjects" (Decode.list Decode.string) |> Option.defaultValue []
                Format = get.Optional.Field "format" Decode.string |> Option.map decodeBookFormat |> Option.defaultValue BookFormat.Unknown
                ExternalIds = get.Optional.Field "externalIds" (Decode.list decodeBookExternalId) |> Option.defaultValue []
            })

        let private encodeReadingProgressObservedData (data: ReadingProgressObservedData) =
            Encode.object [
                "percent", Encode.int data.Percent
                "position", Encode.option encodeReadingPosition data.Position
                "source", Encode.string (encodeProgressSource data.Source)
                "observedOn", Encode.string data.ObservedOn
                "finished", Encode.bool data.Finished
            ]

        let private decodeReadingProgressObservedData: Decoder<ReadingProgressObservedData> =
            Decode.object (fun get -> {
                Percent = get.Required.Field "percent" Decode.int
                Position = get.Optional.Field "position" decodeReadingPosition
                Source = get.Required.Field "source" Decode.string |> decodeProgressSource
                ObservedOn = get.Required.Field "observedOn" Decode.string
                Finished = get.Optional.Field "finished" Decode.bool |> Option.defaultValue false
            })

        let serialize (event: BookEvent) : string * string =
            match event with
            | Book_added_to_library data ->
                "Book_added_to_library", Encode.toString 0 (encodeBookAddedData data)
            | Book_removed_from_library ->
                "Book_removed_from_library", "{}"
            | Book_cover_replaced coverRef ->
                "Book_cover_replaced", Encode.toString 0 (Encode.object [ "coverRef", Encode.string coverRef ])
            | Book_external_id_linked externalId ->
                "Book_external_id_linked", Encode.toString 0 (encodeBookExternalId externalId)
            | Book_format_set format ->
                "Book_format_set", Encode.toString 0 (Encode.object [ "format", Encode.string (encodeBookFormat format) ])
            | Book_status_changed (status, effectiveOn) ->
                "Book_status_changed", Encode.toString 0 (Encode.object [
                    "status", Encode.string (encodeBookStatus status)
                    "effectiveOn", Encode.option Encode.string effectiveOn
                ])
            | Reading_progress_observed data ->
                "Reading_progress_observed", Encode.toString 0 (encodeReadingProgressObservedData data)
            | Prior_reading_progress_recorded data ->
                "Prior_reading_progress_recorded", Encode.toString 0 (encodeReadingProgressObservedData data)
            | Reading_progress_observation_removed (observedOn, source) ->
                "Reading_progress_observation_removed", Encode.toString 0 (Encode.object [
                    "observedOn", Encode.string observedOn
                    "source", Encode.string (encodeProgressSource source)
                ])
            | Reading_progress_entry_removed entryId ->
                "Reading_progress_entry_removed", Encode.toString 0 (Encode.object [ "entryId", Encode.int64 entryId ])
            | Book_personal_rating_set rating ->
                "Book_personal_rating_set", Encode.toString 0 (Encode.object [ "rating", Encode.option Encode.int rating ])
            | Book_recommended_by friendSlug ->
                "Book_recommended_by", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Book_recommendation_removed friendSlug ->
                "Book_recommendation_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])

        let deserialize (eventType: string) (data: string) : BookEvent option =
            match eventType with
            | "Book_added_to_library" ->
                Decode.fromString decodeBookAddedData data
                |> Result.toOption
                |> Option.map Book_added_to_library
            | "Book_removed_from_library" ->
                Some Book_removed_from_library
            | "Book_cover_replaced" ->
                Decode.fromString (Decode.field "coverRef" Decode.string) data
                |> Result.toOption
                |> Option.map Book_cover_replaced
            | "Book_external_id_linked" ->
                Decode.fromString decodeBookExternalId data
                |> Result.toOption
                |> Option.map Book_external_id_linked
            | "Book_format_set" ->
                Decode.fromString (Decode.field "format" Decode.string) data
                |> Result.toOption
                |> Option.map (decodeBookFormat >> Book_format_set)
            | "Book_status_changed" ->
                Decode.fromString (Decode.object (fun get ->
                    let status = get.Required.Field "status" Decode.string |> decodeBookStatus
                    let effectiveOn = get.Optional.Field "effectiveOn" Decode.string
                    (status, effectiveOn)
                )) data
                |> Result.toOption
                |> Option.map Book_status_changed
            | "Reading_progress_observed" ->
                Decode.fromString decodeReadingProgressObservedData data
                |> Result.toOption
                |> Option.map Reading_progress_observed
            | "Prior_reading_progress_recorded" ->
                Decode.fromString decodeReadingProgressObservedData data
                |> Result.toOption
                |> Option.map Prior_reading_progress_recorded
            | "Reading_progress_observation_removed" ->
                Decode.fromString (Decode.object (fun get ->
                    let observedOn = get.Required.Field "observedOn" Decode.string
                    let source = get.Required.Field "source" Decode.string |> decodeProgressSource
                    (observedOn, source)
                )) data
                |> Result.toOption
                |> Option.map Reading_progress_observation_removed
            | "Reading_progress_entry_removed" ->
                Decode.fromString (Decode.field "entryId" Decode.int64) data
                |> Result.toOption
                |> Option.map Reading_progress_entry_removed
            | "Book_personal_rating_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "rating" Decode.int)) data
                |> Result.toOption
                |> Option.map Book_personal_rating_set
            | "Book_recommended_by" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Book_recommended_by
            | "Book_recommendation_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Book_recommendation_removed
            | _ -> None

        /// Hand-maintained mirror of the `deserialize` match-arm strings
        /// above (administration-gxd6e pattern) — see
        /// `Games.Serialization.handledEventTypes` for the precedent.
        let handledEventTypes : string list = [
            "Book_added_to_library"
            "Book_removed_from_library"
            "Book_cover_replaced"
            "Book_external_id_linked"
            "Book_format_set"
            "Book_status_changed"
            "Reading_progress_observed"
            "Prior_reading_progress_recorded"
            "Reading_progress_observation_removed"
            "Reading_progress_entry_removed"
            "Book_personal_rating_set"
            "Book_recommended_by"
            "Book_recommendation_removed"
        ]

        let toEventData (event: BookEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : BookEvent option =
            deserialize storedEvent.EventType storedEvent.Data
