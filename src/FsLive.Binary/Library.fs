﻿namespace FsLive
open FsLive.Core.FsLive
open FsLive.Core.CompilerTmpEmiiter
open FsLive.Core.CrackedFsproj
open FsLive.Core
open Suave
open System.Threading
open FsLive.Core.CrackedFsprojBundle

module Binary =

    type Plugin =
        { Load: unit -> unit
          Unload: unit -> unit
          Calculate: unit -> unit
          DebuggerAttachTimeDelay: int }

    [<RequireQualifiedAccess>]
    type DevelopmentTarget =
        | Plugin of Plugin
        | Program

    let private compile checker crackedFsProj = async {
        let! compileResults = CrackedFsproj.compile checker crackedFsProj
        return 
            compileResults |> Array.map CompileOrCheckResult.CompileResult
    } 
    
    let private tryEmit binaryDevelopmentTarget (logger: Logger.Logger) config (cache: CrackedFsprojBundleCache) compilerTmpEmiiterState =
        logger.Info "tryEmitAction: current emitReplyChannels number is %d" compilerTmpEmiiterState.EmitReplyChannels.Length

        match compilerTmpEmiiterState.CompilingNumber,compilerTmpEmiiterState.EmitReplyChannels with 
        | 0, h::t ->
            let replySuccess() = h.Reply (Successful.OK "fcswatch: Ready to debug") 

            let replyFailure errorText = h.Reply (RequestErrors.BAD_REQUEST errorText)

            logger.Info "Current valid compier task is %d" compilerTmpEmiiterState.CompilerTasks.Length

            match compilerTmpEmiiterState.CompilerTasks with
            | [] ->
                replySuccess()

                match binaryDevelopmentTarget with 
                | DevelopmentTarget.Plugin plugin ->
                    Thread.Sleep(plugin.DebuggerAttachTimeDelay)

                    plugin.Calculate()
                | _ -> ()

                compilerTmpEmiiterState
            | _ ->
                let lastTasks = 
                    compilerTmpEmiiterState.CompilerTasks 
                    |> List.groupBy (fun compilerTask ->
                        compilerTask.Task.Result.[0].ProjPath
                    )
                    |> List.map (fun (projPath, compilerTasks) ->
                        compilerTasks |> List.maxBy (fun compilerTask -> compilerTask.StartTime)
                    )

                let allResults = lastTasks |> List.collect (fun task -> task.Task.Result)

                match List.tryFind CompileOrCheckResult.isFail allResults with 
                | Some result ->
                    let errorText =  
                        result.Errors 
                        |> Seq.map (fun error -> error.ToString())
                        |> String.concat "\n"

                    replyFailure errorText
                    { compilerTmpEmiiterState with EmitReplyChannels = [] } 

                | None ->

                    let projRefersMap = cache.ProjRefersMap

                    let projLevelMap = cache.ProjLevelMap

                    match binaryDevelopmentTarget with 
                    | DevelopmentTarget.Plugin plugin ->
                        plugin.Unload()
                    | _ -> ()

                    compilerTmpEmiiterState.CompilerTmp
                    |> Seq.sortByDescending (fun projPath ->
                        projLevelMap.[projPath]
                    )
                    |> Seq.iter (fun projPath ->
                        let currentCrackedFsproj = cache.ProjectMap.[projPath]

                        CrackedFsproj.copyObjToBin currentCrackedFsproj

                        let refCrackedFsprojs = projRefersMap.[projPath]

                        refCrackedFsprojs |> Seq.sortByDescending (fun refCrackedFsproj ->
                            projLevelMap.[refCrackedFsproj.ProjPath]
                        )
                        |> Seq.iter (CrackedFsproj.copyFileFromRefDllToBin projPath)
                    )

                    replySuccess()

                    match binaryDevelopmentTarget with 
                    | DevelopmentTarget.Plugin plugin ->
                        plugin.Load()
                        plugin.Calculate()
                    | _ -> ()
                
                    { CompilerTmpEmiiterState.createEmpty cache with GetTmpReplyChannels = compilerTmpEmiiterState.GetTmpReplyChannels }
        | _ -> compilerTmpEmiiterState

    let private developmentTarget binaryDevelopmentTarget =
        { CompileOrCheck = compile 
          TryEmit = tryEmit binaryDevelopmentTarget }

    let fsLive binaryDevelopmentTarget config checker entryProjectFile =
        fsLive config (developmentTarget binaryDevelopmentTarget) checker entryProjectFile
