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
    | Notes
    | EntryList

type Model = {
    ActiveSection: Section
}

type Msg =
    | Set_section of Section
