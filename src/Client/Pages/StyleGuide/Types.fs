module Mediatheca.Client.Pages.StyleGuide.Types

type Section =
    | Overview
    | Typography
    | Colors
    | Spacing
    | PaperOverlay
    | Animations
    | Components
    | VelvetLobbyPatterns
    | ContentBlocks
    | ContentZone
    | EntryList

type Model = {
    ActiveSection: Section
}

type Msg =
    | Set_section of Section
