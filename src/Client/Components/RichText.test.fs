/// books-nvnyk: coverage for `RichText.parse`, the pure allowlisting
/// tokenizer/tree-builder behind the book description renderer. No DOM/
/// `jsdom` needed -- runs under this project's plain Vitest `node`
/// environment (`vite.config.mts`).
module Mediatheca.Client.Components.RichTextTests

open Fable.Mocha
open Mediatheca.Client.Components.RichText

let richTextTests =
    testList "books-nvnyk: RichText.parse" [

        testCase "keeps allowlisted nesting -- two paragraphs (one with an em, one with a br) and one ul with one li" <| fun () ->
            let input = "<p>One <em>two</em></p><p>Three<br>Four</p><ul><li>a</li></ul>"
            let expected =
                [ Elem("p", [ Text "One "; Elem("em", [ Text "two" ]) ])
                  Elem("p", [ Text "Three"; Elem("br", []); Text "Four" ])
                  Elem("ul", [ Elem("li", [ Text "a" ]) ]) ]
            Expect.equal (parse input) expected "p/em/br/ul/li survive, correctly nested"

        testCase "unwraps span/img to their text content, dropping all attributes" <| fun () ->
            let input = """<span onclick="x">t</span><img src=x>u"""
            let expected = [ Elem("p", [ Text "tu" ]) ]
            Expect.equal (parse input) expected "span/img gone, their text (\"t\"/nothing) plus \"u\" survives as one paragraph"

        testCase "plain text with a blank line splits into two paragraphs (legacy plain-text rows)" <| fun () ->
            let input = "First paragraph.\n\nSecond paragraph."
            let expected =
                [ Elem("p", [ Text "First paragraph." ])
                  Elem("p", [ Text "Second paragraph." ]) ]
            Expect.equal (parse input) expected "blank-line split, backward compatible with a pre-fix stripHtml row"

        testCase "a raw, unsanitized legacy Audnexus-style row (anchor + div, no prior sanitization) still renders safely" <| fun () ->
            let input = """<div class="q"><p>A <a href="x"><b>bold</b></a> claim.</p></div>"""
            let expected = [ Elem("p", [ Text "A "; Elem("b", [ Text "bold" ]); Text " claim." ]) ]
            Expect.equal (parse input) expected "div/a unwrapped, b kept -- no re-import or backfill needed"

        testCase "plain text with no blank line stays one paragraph" <| fun () ->
            let input = "Just plain text."
            Expect.equal (parse input) [ Elem("p", [ Text "Just plain text." ]) ] "single paragraph, unchanged"
    ]

Mocha.runTests richTextTests |> ignore
