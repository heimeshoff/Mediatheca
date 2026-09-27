namespace Mediatheca.Server

open System.IO

/// integration-qqpq9 (ADR-0043/ADR-0089): the companion-PDF cache tier, a
/// sibling of `ImageStore`'s `images/` cache — one file per Audible ASIN,
/// named `<asin>.pdf`, under `<DATA_DIR>/pdfs/`. `save` writes to a temp
/// name first, then renames (`File.Move` within the same directory is
/// atomic on both Windows and Linux) so a failed/partial download never
/// leaves a truncated file behind (this task's own Notes).
module PdfStore =

    let relativePath (asin: string) : string = sprintf "%s.pdf" asin

    let exists (basePath: string) (asin: string) : bool =
        File.Exists(Path.Combine(basePath, relativePath asin))

    let save (basePath: string) (asin: string) (bytes: byte[]) : unit =
        if not (Directory.Exists(basePath)) then
            Directory.CreateDirectory(basePath) |> ignore
        let finalPath = Path.Combine(basePath, relativePath asin)
        let tempPath = finalPath + ".tmp"
        File.WriteAllBytes(tempPath, bytes)
        if File.Exists(finalPath) then
            File.Delete(finalPath)
        File.Move(tempPath, finalPath)
