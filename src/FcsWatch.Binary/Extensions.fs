﻿namespace FcsWatch.Binary
open System.IO
open Fake.IO
open Fake.IO.FileSystemOperators
open FcsWatch.Core.CrackedFsproj
open FSharp.Compiler.SourceCodeServices
open FcsWatch.Core
open Fake.DotNet
open Fake.Core

type CompilerResult =
    { Dll: string
      Errors: FSharpErrorInfo []
      ExitCode: int
      ProjPath: string }
with
    member x.Pdb = Path.changeExtension ".pdb" x.Dll

    interface ICompilerOrCheckResult with 
        member x.Errors = x.Errors
        member x.ExitCode = x.ExitCode
        member x.ProjPath = x.ProjPath

[<AutoOpen>]
module internal Global =
    let mutable logger = Logger.create Logger.Level.Minimal

    let private dotnetWith command args dir =
        DotNet.exec
            (fun ops -> {ops with WorkingDirectory = dir})
            command
            (Args.toWindowsCommandLine args)

    let dotnet command args dir =
        let result = dotnetWith command args dir
        if result.ExitCode <> 0
        then failwithf "Error while running %s with args %A" command (List.ofSeq args)


module Extensions =


    type internal Logger.Logger with
        member x.CopyFile src dest =
            File.Copy(src,dest,true)
            logger.Important "%s ->\n%s" src dest

    [<RequireQualifiedAccess>]
    module SingleTargetCrackedFsproj =
        let copyFileFromRefDllToBin originProjectFile (destCrackedFsprojSingleTarget: SingleTargetCrackedFsproj) =

            let targetDir = destCrackedFsprojSingleTarget.TargetDir

            let originDll =
                let projName = Path.GetFileNameWithoutExtension originProjectFile

                destCrackedFsprojSingleTarget.RefDlls
                |> Array.find(fun refDll -> Path.GetFileNameWithoutExtension refDll = projName)

            let fileName = Path.GetFileName originDll

            let destDll = targetDir </> fileName

            logger.CopyFile originDll destDll

            let originPdb = originDll |> Path.changeExtension ".pdb"

            let destPdb = targetDir </> (Path.changeExtension ".pdb" fileName)

            logger.CopyFile originPdb destPdb

        let copyObjToBin (singleTargetCrackedFsproj: SingleTargetCrackedFsproj) =
            logger.CopyFile singleTargetCrackedFsproj.ObjTargetFile singleTargetCrackedFsproj.TargetPath
            logger.CopyFile singleTargetCrackedFsproj.ObjTargetPdb singleTargetCrackedFsproj.TargetPdbPath


        let compile (checker: FSharpChecker) (crackedProjectSingleTarget: SingleTargetCrackedFsproj) = async {
            let tmpDll = crackedProjectSingleTarget.ObjTargetFile

            let baseOptions =
                crackedProjectSingleTarget.FSharpProjectOptions.OtherOptions
                |> Array.map (fun op -> if op.StartsWith "-o:" then "-o:" + tmpDll else op)

            let fscArgs = Array.concat [[|"fsc.exe"|]; baseOptions;[|"--nowin32manifest"|]]
            let! errors,exitCode = checker.Compile(fscArgs)
            return
                { Errors = errors
                  ExitCode = exitCode
                  Dll = tmpDll
                  ProjPath = crackedProjectSingleTarget.ProjPath }
        }

    [<RequireQualifiedAccess>]
    module CrackedFsproj =

        let copyFileFromRefDllToBin projectFile (destCrackedFsproj: CrackedFsproj) =
            destCrackedFsproj.AsList
            |> List.iter (SingleTargetCrackedFsproj.copyFileFromRefDllToBin projectFile)

        let copyObjToBin (crackedFsproj: CrackedFsproj) =
            crackedFsproj.AsList |> List.iter SingleTargetCrackedFsproj.copyObjToBin


        let compile (checker: FSharpChecker) (crackedFsProj: CrackedFsproj) =
            crackedFsProj.AsList
            |> List.map (SingleTargetCrackedFsproj.compile checker)
            |> Async.Parallel

[<AutoOpen>]
module internal InternalExtensions =
    [<RequireQualifiedAccess>]
    module File =
        let rec tryFindUntilRoot makePath dir =
            let file = makePath dir
            match file with 
            | null -> None
            | _ ->
                if File.exists file 
                then Some file
                else tryFindUntilRoot makePath (Path.getDirectory dir)