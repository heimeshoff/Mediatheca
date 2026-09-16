namespace Mediatheca.Server

open Mediatheca.Shared

/// Curation-owned generalization of the old per-type block system's
/// `convertOldBlocks`/`mapOldType` conversion (ADR-0080 §10, Gate 1 of
/// curation-j4qqt's migration) — lifted out before the legacy modules that
/// used to own it are deleted. Converts a legacy content-block row shape
/// into the target `JournalBlockDto` tree: row-grouped pairs become
/// `columnList`/`column`, and every old type maps onto its
/// `JournalBlockTypes` equivalent (the new vocabulary's h1-h4/bullet/
/// numbered/todo/toggle members need no mapping — old content-blocks never
/// produced them). Legacy `link` content stays block-level, never demoted
/// to an inline link.
module ContentBlockConversion =

    /// A raw legacy content-block row, read straight off the (now-dropped)
    /// `content_blocks` table by Gate 1's migration query — NOT
    /// `Shared.ContentBlockDto` (deleted along with the rest of the legacy
    /// client contract; this shape is private to the migration).
    type LegacyContentBlock = {
        BlockId: string
        BlockType: string
        Content: string
        ImageRef: string option
        Url: string option
        Caption: string option
        Position: int
        RowGroup: string option
        RowPosition: int option
    }

    let private newId () = System.Guid.NewGuid().ToString("N")

    let mapOldType (oldType: string) =
        match oldType with
        | "screenshot" -> JournalBlockTypes.image
        | "quote" -> JournalBlockTypes.quote
        | "callout" -> JournalBlockTypes.callout
        | "code" -> JournalBlockTypes.code
        | "link" -> JournalBlockTypes.link
        | _ -> JournalBlockTypes.text

    /// Walk in position order, emitting row-grouped pairs as columnList/column trees.
    let convertOldBlocks (oldBlocks: LegacyContentBlock list) : JournalBlockDto list =
        let sorted = oldBlocks |> List.sortBy (fun b -> b.Position)
        let emptyBlock id parentId blockType position : JournalBlockDto =
            { Id = id
              ParentId = parentId
              BlockType = blockType
              Content = ""
              Checked = false
              Collapsed = false
              Language = None
              Url = None
              ImageRef = None
              Caption = None
              Position = position
              Width = 1.0 }
        let convertContent (parentId: string option) (position: int) (old: LegacyContentBlock) : JournalBlockDto =
            { emptyBlock old.BlockId parentId (mapOldType old.BlockType) position with
                Content = old.Content
                Url = old.Url
                ImageRef = old.ImageRef
                Caption = old.Caption }
        let mutable result = []
        let mutable seen = Set.empty
        let mutable rootPos = 0
        for block in sorted do
            if not (seen.Contains block.BlockId) then
                let partner =
                    match block.RowGroup with
                    | Some rg ->
                        sorted |> List.tryFind (fun b ->
                            b.BlockId <> block.BlockId && b.RowGroup = Some rg && not (seen.Contains b.BlockId))
                    | None -> None
                match partner with
                | Some p ->
                    seen <- seen.Add(block.BlockId).Add(p.BlockId)
                    let first, second =
                        if (block.RowPosition |> Option.defaultValue 0) <= (p.RowPosition |> Option.defaultValue 1)
                        then block, p
                        else p, block
                    let listId = newId ()
                    let col1Id = newId ()
                    let col2Id = newId ()
                    result <- result @ [
                        emptyBlock listId None JournalBlockTypes.columnList rootPos
                        { emptyBlock col1Id (Some listId) JournalBlockTypes.column 0 with Width = 0.5 }
                        { emptyBlock col2Id (Some listId) JournalBlockTypes.column 1 with Width = 0.5 }
                        convertContent (Some col1Id) 0 first
                        convertContent (Some col2Id) 0 second
                    ]
                    rootPos <- rootPos + 1
                | None ->
                    seen <- seen.Add block.BlockId
                    result <- result @ [ convertContent None rootPos block ]
                    rootPos <- rootPos + 1
        result
