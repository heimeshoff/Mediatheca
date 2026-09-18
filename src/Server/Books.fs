namespace Mediatheca.Server

open Thoth.Json.Net
open Mediatheca.Shared

/// The Book aggregate (books-y9kxy) — the server core of the fourth media
/// type. Model of record: ADR-0076 (Books model — progress observations are
/// events; length is cache; status mirrors Games), amended by ADR-0077
/// (status changes carry an effective-on date). Mirrors `Games.fs`'s shape
/// (DU + decide + Serialization) exactly, per this task's own instructions.
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
        | Reading_progress_observation_removed of observedOn: string * source: ProgressSource
        | Book_personal_rating_set of rating: int option
        | Book_recommended_by of friendSlug: string
        | Book_recommendation_removed of friendSlug: string

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
        /// `(observedOn, source) -> percent` — needed for the per-source
        /// no-op/promotion rules in `decide` (the comparison baseline is the
        /// latest percent previously observed from that SAME source, not the
        /// book's global current percent) and for exact
        /// `Reading_progress_observation_removed` handling.
        Observations: Map<string * ProgressSource, int>
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
        | Remove_reading_progress_observation of observedOn: string * source: ProgressSource
        | Set_personal_rating of rating: int option
        | Recommend_by of friendSlug: string
        | Remove_recommendation of friendSlug: string

    // Evolve

    let evolve (state: BookState) (event: BookEvent) : BookState =
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
                Observations = Map.empty
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
            Active { book with Observations = book.Observations |> Map.add (data.ObservedOn, data.Source) data.Percent }
        | Active book, Prior_reading_progress_recorded data ->
            Active { book with Observations = book.Observations |> Map.add (data.ObservedOn, data.Source) data.Percent }
        | Active book, Reading_progress_observation_removed (observedOn, source) ->
            Active { book with Observations = book.Observations |> Map.remove (observedOn, source) }
        | Active book, Book_personal_rating_set rating ->
            Active { book with PersonalRating = rating }
        | Active book, Book_recommended_by friendSlug ->
            Active { book with RecommendedBy = book.RecommendedBy |> Set.add friendSlug }
        | Active book, Book_recommendation_removed friendSlug ->
            Active { book with RecommendedBy = book.RecommendedBy |> Set.remove friendSlug }
        | _ -> state

    let reconstitute (events: BookEvent list) : BookState =
        List.fold evolve Not_created events

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
    /// promotion rules (ADR-0076 §2): the latest percent previously observed
    /// from the SAME source (0 when the source has no prior observation),
    /// found by walking `Observations` for that source and taking the entry
    /// with the most recent `observedOn`.
    let private latestPercentForSource (book: ActiveBook) (source: ProgressSource) : int =
        book.Observations
        |> Map.toList
        |> List.choose (fun ((observedOn, src), percent) -> if src = source then Some (observedOn, percent) else None)
        |> List.sortByDescending fst
        |> List.tryHead
        |> Option.map snd
        |> Option.defaultValue 0

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
            let baseline = latestPercentForSource book data.Source
            if data.Percent = baseline then
                // Per-source no-op (ADR-0076 §2): a daily sync reporting
                // the same percent as last time from the same source
                // appends nothing — not even the observation itself.
                Ok []
            else
                let observedEvent = Reading_progress_observed data
                let statusEvents =
                    if data.Percent = 100 || data.Finished then
                        if book.Status = BookStatus.Finished then []
                        else [ Book_status_changed (BookStatus.Finished, Some data.ObservedOn) ]
                    elif data.Percent > baseline then
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
            if book.Observations |> Map.exists (fun (_, src) _ -> src = data.Source) then
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
        | Active book, Remove_reading_progress_observation (observedOn, source) ->
            match book.Observations |> Map.tryFind (observedOn, source) with
            | None -> Error "Reading progress observation not found"
            | Some _ -> Ok [ Reading_progress_observation_removed (observedOn, source) ]
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
            "Book_personal_rating_set"
            "Book_recommended_by"
            "Book_recommendation_removed"
        ]

        let toEventData (event: BookEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : BookEvent option =
            deserialize storedEvent.EventType storedEvent.Data
