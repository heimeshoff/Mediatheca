module Mediatheca.Client.Pages.StyleGuide.Types

type Section =
    | Overview
    | Typography
    | Colors
    | Spacing
    | Glassmorphism
    | Animations
    | Components
    | ContentBlocks
    | ContentZone
    | EntryList

type Model = {
    ActiveSection: Section
}

type Msg =
    | Set_section of Section
