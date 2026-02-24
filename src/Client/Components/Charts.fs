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

/// Color palette exposed for external consumers (e.g. per-game monthly chart)
let chartColors = pieColors
let chartBgColors = pieBgColors

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
                                                svg.custom("opacity", "0.85")
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
                                                svg.custom("opacity", "0.85")
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

/// Renders a spider/radar chart as SVG with a legend.
/// data: list of (label, value) pairs.
/// emptyMessage: text shown when there's no data.
let radarChart (data: (string * int) list) (emptyMessage: string) =
    if List.isEmpty data then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text emptyMessage
        ]
    else
        let data = data |> List.truncate 12 // max 12 axes for readability
        let n = List.length data
        let maxVal = data |> List.map snd |> List.max |> max 1
        let cx, cy, r = 150.0, 150.0, 110.0
        let rings = 4

        // Calculate points for each axis
        let axisAngle i = -System.Math.PI / 2.0 + (2.0 * System.Math.PI * float i / float n)

        let pointAt i (radius: float) =
            let angle = axisAngle i
            cx + radius * System.Math.Cos(angle), cy + radius * System.Math.Sin(angle)

        // Build the polygon path for data values
        let dataPoints =
            data |> List.mapi (fun i (_label, value) ->
                let pct = float value / float maxVal
                let pr = r * pct
                pointAt i pr)

        let polygonPath =
            dataPoints
            |> List.mapi (fun i (x, y) ->
                if i = 0 then sprintf "M %f %f" x y
                else sprintf "L %f %f" x y)
            |> String.concat " "
            |> fun s -> s + " Z"

        Html.div [
            prop.className "flex flex-col items-center gap-3"
            prop.children [
                Svg.svg [
                    svg.viewBox (0, 0, 300, 300)
                    svg.width 260
                    svg.height 260
                    svg.children [
                        // Grid rings
                        for ring in 1 .. rings do
                            let ringR = r * float ring / float rings
                            let ringPath =
                                [0 .. n - 1]
                                |> List.mapi (fun i _ ->
                                    let (x, y) = pointAt i ringR
                                    if i = 0 then sprintf "M %f %f" x y
                                    else sprintf "L %f %f" x y)
                                |> String.concat " "
                                |> fun s -> s + " Z"
                            Svg.path [
                                svg.d ringPath
                                svg.fill "none"
                                svg.stroke "oklch(100% 0 0 / 0.08)"
                                svg.strokeWidth 1
                            ]

                        // Axis lines
                        for i in 0 .. n - 1 do
                            let (x, y) = pointAt i r
                            Svg.line [
                                svg.x1 cx
                                svg.y1 cy
                                svg.x2 x
                                svg.y2 y
                                svg.stroke "oklch(100% 0 0 / 0.06)"
                                svg.strokeWidth 1
                            ]

                        // Data polygon fill
                        Svg.path [
                            svg.d polygonPath
                            svg.fill pieColors.[0]
                            svg.className "opacity-20"
                        ]

                        // Data polygon outline
                        Svg.path [
                            svg.d polygonPath
                            svg.fill "none"
                            svg.stroke pieColors.[0]
                            svg.strokeWidth 2
                            svg.className "opacity-80"
                        ]

                        // Data points
                        for (x, y) in dataPoints do
                            Svg.circle [
                                svg.cx x
                                svg.cy y
                                svg.r 3.5
                                svg.fill pieColors.[0]
                                svg.className "opacity-90"
                            ]

                        // Axis labels
                        for i in 0 .. n - 1 do
                            let (label, value) = data.[i]
                            let labelR = r + 18.0
                            let (lx, ly) = pointAt i labelR
                            let anchor =
                                let angle = axisAngle i
                                let cos = System.Math.Cos(angle)
                                if cos > 0.3 then "start"
                                elif cos < -0.3 then "end"
                                else "middle"
                            Svg.text [
                                svg.x lx
                                svg.y ly
                                svg.dominantBaseline.middle
                                svg.custom ("text-anchor", anchor)
                                svg.className "fill-base-content/60 text-[10px]"
                                svg.text (sprintf "%s (%d)" label value)
                            ]
                    ]
                ]
            ]
        ]
