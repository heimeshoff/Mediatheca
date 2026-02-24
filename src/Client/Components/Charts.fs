module Mediatheca.Client.Components.Charts

open Feliz

/// Chart color palette (oklch values for SVG fill)
let private pieColors = [|
    "oklch(86.133% 0.141 139.549)"   // primary (green)
    "oklch(86.078% 0.142 206.182)"   // info (cyan)
    "oklch(73.375% 0.165 35.353)"    // secondary (orange)
    "oklch(74.229% 0.133 311.379)"   // accent (purple)
    "oklch(86.163% 0.142 94.818)"    // warning (yellow)
    "oklch(86.171% 0.142 166.534)"   // success (teal)
    "oklch(82.418% 0.099 33.756)"    // error (salmon)
    "oklch(70% 0.12 260)"            // custom blue
    "oklch(65% 0.15 350)"            // pink
    "oklch(75% 0.1 180)"             // teal-dark
|]

/// Tailwind text color classes matching the pie colors
let private pieLegendColors = [|
    "text-primary"
    "text-info"
    "text-secondary"
    "text-accent"
    "text-warning"
    "text-success"
    "text-error"
    "text-[oklch(70%_0.12_260)]"
    "text-[oklch(65%_0.15_350)]"
    "text-[oklch(75%_0.1_180)]"
|]

/// Tailwind bg color classes matching the pie colors
let private pieBgColors = [|
    "bg-primary"
    "bg-info"
    "bg-secondary"
    "bg-accent"
    "bg-warning"
    "bg-success"
    "bg-error"
    "bg-[oklch(70%_0.12_260)]"
    "bg-[oklch(65%_0.15_350)]"
    "bg-[oklch(75%_0.1_180)]"
|]

/// Renders a donut/pie chart as SVG with a legend.
/// data: list of (label, value) pairs.
/// emptyMessage: text shown when there's no data.
let donutChart (data: (string * int) list) (emptyMessage: string) =
    if List.isEmpty data then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text emptyMessage
        ]
    else
        let total = data |> List.sumBy snd |> max 1
        let cx, cy, r = 100.0, 100.0, 80.0
        let innerR = 50.0 // donut hole

        // Build segments
        let segments =
            data
            |> List.mapi (fun i (label, value) ->
                let pct = float value / float total
                i, label, value, pct)

        // Calculate start angles
        let mutable currentAngle = -90.0 // start at top
        let segmentData =
            segments
            |> List.map (fun (i, label, value, pct) ->
                let startAngle = currentAngle
                let sweep = pct * 360.0
                currentAngle <- currentAngle + sweep
                i, label, value, pct, startAngle, sweep)

        Html.div [
            prop.className "flex flex-col sm:flex-row items-center gap-4"
            prop.children [
                // SVG donut chart
                Html.div [
                    prop.className "flex-shrink-0"
                    prop.children [
                        Svg.svg [
                            svg.viewBox (0, 0, 200, 200)
                            svg.width 180
                            svg.height 180
                            svg.children [
                                for (i, _label, _value, pct, startAngle, sweep) in segmentData do
                                    let colorIdx = i % pieColors.Length
                                    if pct > 0.0 then
                                        if pct >= 0.999 then
                                            // Full circle — draw two arcs
                                            Svg.circle [
                                                svg.cx cx
                                                svg.cy cy
                                                svg.r ((r + innerR) / 2.0)
                                                svg.fill "none"
                                                svg.stroke pieColors.[colorIdx]
                                                svg.strokeWidth (r - innerR)
                                                svg.opacity 0.85
                                            ]
                                        else
                                            let startRad = startAngle * System.Math.PI / 180.0
                                            let endRad = (startAngle + sweep) * System.Math.PI / 180.0
                                            let x1Outer = cx + r * System.Math.Cos(startRad)
                                            let y1Outer = cy + r * System.Math.Sin(startRad)
                                            let x2Outer = cx + r * System.Math.Cos(endRad)
                                            let y2Outer = cy + r * System.Math.Sin(endRad)
                                            let x1Inner = cx + innerR * System.Math.Cos(endRad)
                                            let y1Inner = cy + innerR * System.Math.Sin(endRad)
                                            let x2Inner = cx + innerR * System.Math.Cos(startRad)
                                            let y2Inner = cy + innerR * System.Math.Sin(startRad)
                                            let largeArc = if sweep > 180.0 then 1 else 0

                                            let pathD =
                                                sprintf "M %f %f A %f %f 0 %d 1 %f %f L %f %f A %f %f 0 %d 0 %f %f Z"
                                                    x1Outer y1Outer
                                                    r r largeArc x2Outer y2Outer
                                                    x1Inner y1Inner
                                                    innerR innerR largeArc x2Inner y2Inner

                                            Svg.path [
                                                svg.d pathD
                                                svg.fill pieColors.[colorIdx]
                                                svg.opacity 0.85
                                                svg.className "hover:opacity-100 transition-opacity cursor-default"
                                            ]

                                // Center text showing total
                                Svg.text [
                                    svg.x cx
                                    svg.y (cy - 6.0)
                                    svg.textAnchor.middle
                                    svg.dominantBaseline.middle
                                    svg.className "fill-base-content text-2xl font-display font-bold"
                                    svg.text (string total)
                                ]
                                Svg.text [
                                    svg.x cx
                                    svg.y (cy + 14.0)
                                    svg.textAnchor.middle
                                    svg.dominantBaseline.middle
                                    svg.className "fill-base-content/50 text-xs"
                                    svg.text "total"
                                ]
                            ]
                        ]
                    ]
                ]
                // Legend
                Html.div [
                    prop.className "flex flex-col gap-1.5 min-w-0"
                    prop.children [
                        for (i, label, value, pct, _startAngle, _sweep) in segmentData do
                            let colorIdx = i % pieBgColors.Length
                            Html.div [
                                prop.className "flex items-center gap-2"
                                prop.children [
                                    Html.div [
                                        prop.className (pieBgColors.[colorIdx] + " w-3 h-3 rounded-full flex-shrink-0 opacity-85")
                                    ]
                                    Html.span [
                                        prop.className "text-xs text-base-content/70 truncate"
                                        prop.text label
                                    ]
                                    Html.span [
                                        prop.className "text-xs text-base-content/40 ml-auto flex-shrink-0"
                                        prop.text (sprintf "%d (%.0f%%)" value (pct * 100.0))
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]
