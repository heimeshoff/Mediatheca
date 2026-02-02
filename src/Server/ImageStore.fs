namespace Mediatheca.Server

open System.IO

module ImageStore =

    let saveImage (basePath: string) (relativePath: string) (bytes: byte[]) : unit =
        let fullPath = Path.Combine(basePath, relativePath)
        let dir = Path.GetDirectoryName(fullPath)
        if not (Directory.Exists(dir)) then
            Directory.CreateDirectory(dir) |> ignore
        File.WriteAllBytes(fullPath, bytes)

    let deleteImage (basePath: string) (relativePath: string) : unit =
        let fullPath = Path.Combine(basePath, relativePath)
        if File.Exists(fullPath) then
            File.Delete(fullPath)

    let imageExists (basePath: string) (relativePath: string) : bool =
        let fullPath = Path.Combine(basePath, relativePath)
        File.Exists(fullPath)
