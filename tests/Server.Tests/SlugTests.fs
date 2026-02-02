module Mediatheca.Tests.SlugTests

open Expecto
open Mediatheca.Shared

[<Tests>]
let slugTests =
    testList "Slug" [

        testCase "basic slugify" <| fun _ ->
            let result = Slug.slugify "The Matrix"
            Expect.equal result "the-matrix" "Should lowercase and replace spaces"

        testCase "special chars are removed" <| fun _ ->
            let result = Slug.slugify "Spider-Man: No Way Home"
            Expect.equal result "spider-man-no-way-home" "Should remove colons and keep hyphens"

        testCase "multiple spaces become single hyphen" <| fun _ ->
            let result = Slug.slugify "The   Big   Lebowski"
            Expect.equal result "the-big-lebowski" "Should collapse multiple spaces"

        testCase "leading and trailing hyphens removed" <| fun _ ->
            let result = Slug.slugify "  Hello World  "
            Expect.equal result "hello-world" "Should trim hyphens"

        testCase "movieSlug combines name and year" <| fun _ ->
            let result = Slug.movieSlug "The Matrix" 1999
            Expect.equal result "the-matrix-1999" "Should append year"

        testCase "friendSlug uses name only" <| fun _ ->
            let result = Slug.friendSlug "Marco"
            Expect.equal result "marco" "Should slugify name"

        testCase "numbers are preserved" <| fun _ ->
            let result = Slug.slugify "Ocean's 11"
            Expect.equal result "oceans-11" "Should keep numbers"

        testCase "empty string" <| fun _ ->
            let result = Slug.slugify ""
            Expect.equal result "" "Should handle empty string"

        testCase "accents and special unicode removed" <| fun _ ->
            let result = Slug.slugify "Caf! L'amour"
            Expect.equal result "caf-lamour" "Should remove special chars"
    ]
