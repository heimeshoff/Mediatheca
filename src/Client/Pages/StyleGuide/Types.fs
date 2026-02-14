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

type Model = {
    ActiveSection: Section
}

type Msg =
    | Set_section of Section
