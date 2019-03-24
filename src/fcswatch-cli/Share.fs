﻿module FcsWatch.Cli.Share

open System
open FcsWatch
open Argu
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.Core

type Arguments =
    | Working_Dir of string
    | Project_File of string
    | Debuggable
    | Logger_Level of Logger.Level
    | No_Build
with
    interface IArgParserTemplate with
        member x.Usage =
            match x with
            | Working_Dir _  -> "Specfic working directory, default is current directory"
            | Project_File _ -> "Entry project file, default is exact fsproj file in working dir"
            | Debuggable _ -> "Enable debuggable in vscode, This will disable auto Reload"
            | Logger_Level _ -> "Default is Minimal"
            | No_Build -> "--no-build"

type ProcessResult =
    { Config: Config
      ProjectFile: string }

let processParseResults usage (results: ParseResults<Arguments>) =
    try
        let execContext = Fake.Core.Context.FakeExecutionContext.Create false "generate.fsx" []
        Fake.Core.Context.setExecutionContext (Fake.Core.Context.RuntimeContext.Fake execContext)
        let defaultConfigValue = Config.DefaultValue

        let workingDir = results.GetResult (Working_Dir,defaultConfigValue.WorkingDir)

        let projectFile =
            match results.TryGetResult Project_File with
            | Some projectFile -> projectFile
            | None ->
                (!! (workingDir </> "*.fsproj")
                |> Seq.filter (fun file -> file.EndsWith ".fsproj")
                |> List.ofSeq
                |> function
                    | [ ] ->
                        failwithf "no project file found, no compilation arguments given and no project file found in \"%s\"" Environment.CurrentDirectory
                    | [ file ] ->
                        printfn "using implicit project file '%s'" file
                        file
                    | file1 :: file2 :: _ ->
                        failwithf "multiple project files found, e.g. %s and %s" file1 file2 )

        let noBuild =
            match results.TryGetResult No_Build with
            | Some _ -> true
            | None ->
                false

        { ProjectFile = projectFile
          Config =
            { Config.DefaultValue with
                WorkingDir = workingDir
                DevelopmentTarget = 
                    match results.TryGetResult Debuggable with
                    | Some _ -> DevelopmentTarget.debuggableProgram
                    | None -> DevelopmentTarget.autoReloadProgram

                LoggerLevel = results.GetResult(Logger_Level, defaultConfigValue.LoggerLevel)
                NoBuild = noBuild } 

        }
    with ex ->
        let usage = usage()
        failwithf "%A\n%s" ex.Message usage



let parser = ArgumentParser.Create<Arguments>(programName = "fcswatch.exe")